using ActiveRolesDashboard.Models;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// Builds the list of connectivity/performance probe targets for a dashboard from the
/// collected <see cref="DashboardSummary"/> (AR servers, domain controllers, global catalogs)
/// combined with static configuration (RSTS URL, Web Interface URL, Entra tenants).
/// </summary>
public class DiagnosticsTargetProvider
{
    // Default LDAP/LDAPS/GC/HTTP ports used for the ping TCP fallback and LDAP bind.
    private const int LdapPort = 389;
    private const int GcPort = 3268;
    private const int HttpsPort = 443;

    // Default SQL Server TCP port used for the SQL ping TCP fallback.
    private const int SqlPort = 1433;

    // Active Roles ADSI provider port. The ARS ADSI provider is a superset of the Microsoft
    // ADSI provider and services LDAP requests on this port rather than the standard 389.
    private const int ArsAdsiPort = 15172;

    private readonly IOptionsMonitor<ActiveRolesConfig> _config;

    public DiagnosticsTargetProvider(IOptionsMonitor<ActiveRolesConfig> config)
    {
        _config = config;
    }

    public IReadOnlyList<DiagnosticsTarget> BuildTargets(DiagnosticsDashboard dashboard, DashboardSummary summary)
    {
        return dashboard switch
        {
            DiagnosticsDashboard.ActiveRoles => BuildActiveRolesTargets(summary),
            DiagnosticsDashboard.ActiveDirectory => BuildActiveDirectoryTargets(summary),
            DiagnosticsDashboard.EntraId => BuildEntraTargets(summary),
            DiagnosticsDashboard.Exchange => BuildExchangeTargets(summary),
            _ => Array.Empty<DiagnosticsTarget>()
        };
    }

    private List<DiagnosticsTarget> BuildActiveRolesTargets(DashboardSummary summary)
    {
        var config = _config.CurrentValue;
        var targets = new List<DiagnosticsTarget>();

        // Each Active Roles administration server: ping, LDAP bind, REST GET.
        foreach (var server in summary.Servers?.Items ?? Enumerable.Empty<ServerInfo>())
        {
            var host = server.ServerName;
            if (string.IsNullOrWhiteSpace(host)) continue;
            targets.Add(new DiagnosticsTarget
            {
                Id = $"ar-server:{host}",
                Name = host,
                Host = host,
                Dn = server.Guid,
                ServerType = DiagnosticsServerType.ActiveRolesServer,
                PrimaryPort = ArsAdsiPort,
                ApplicableTests =
                {
                    DiagnosticsTestType.Ping,
                    DiagnosticsTestType.LdapBind,
                    DiagnosticsTestType.RestGet
                }
            });
        }

        // RSTS: ping (TCP 443 fallback) + authentication request.
        if (!string.IsNullOrWhiteSpace(config.RstsUrl) && Uri.TryCreate(config.RstsUrl, UriKind.Absolute, out var rstsUri))
        {
            targets.Add(new DiagnosticsTarget
            {
                Id = "rsts",
                Name = rstsUri.Host,
                Host = rstsUri.Host,
                Url = config.RstsUrl,
                ServerType = DiagnosticsServerType.Rsts,
                PrimaryPort = rstsUri.Port > 0 ? rstsUri.Port : HttpsPort,
                ApplicableTests =
                {
                    DiagnosticsTestType.Ping,
                    DiagnosticsTestType.RstsAuth
                }
            });
        }

        // Web Interface site (only when configured): ping + HTTP request.
        if (!string.IsNullOrWhiteSpace(config.WebInterfaceUrl) && Uri.TryCreate(config.WebInterfaceUrl, UriKind.Absolute, out var webUri))
        {
            targets.Add(new DiagnosticsTarget
            {
                Id = "web-interface",
                Name = webUri.Host,
                Host = webUri.Host,
                Url = config.WebInterfaceUrl,
                ServerType = DiagnosticsServerType.WebInterface,
                PrimaryPort = webUri.Port > 0 ? webUri.Port : HttpsPort,
                ApplicableTests =
                {
                    DiagnosticsTestType.Ping,
                    DiagnosticsTestType.WebHttp
                }
            });
        }

        // Active Roles configuration SQL Server(s): ping + a lightweight query against the
        // ARServices table of the configuration database. Derived from the collected
        // configuration databases (SQL alias + database name). Deduped per alias+database.
        var seenSql = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var db in summary.ConfigDatabases?.Items ?? Enumerable.Empty<DatabaseInfo>())
        {
            var sqlHost = db.SqlAlias;
            if (string.IsNullOrWhiteSpace(sqlHost)) continue;
            var key = sqlHost + "|" + db.DatabaseName;
            if (!seenSql.Add(key)) continue;
            targets.Add(new DiagnosticsTarget
            {
                Id = $"sql:{sqlHost}:{db.DatabaseName}",
                Name = string.IsNullOrWhiteSpace(db.DatabaseName) ? sqlHost : $"{sqlHost} ({db.DatabaseName})",
                Host = sqlHost,
                // Reuse Dn to carry the configuration database name consumed by the SQL query probe.
                Dn = db.DatabaseName,
                ServerType = DiagnosticsServerType.SqlServer,
                PrimaryPort = SqlPort,
                ApplicableTests =
                {
                    DiagnosticsTestType.Ping,
                    DiagnosticsTestType.SqlQuery
                }
            });
        }

        return targets;
    }

    private List<DiagnosticsTarget> BuildActiveDirectoryTargets(DashboardSummary summary)
    {
        var targets = new List<DiagnosticsTarget>();

        foreach (var dc in summary.DomainControllers?.Items ?? Enumerable.Empty<DomainControllerInfo>())
        {
            var host = dc.Name;
            if (string.IsNullOrWhiteSpace(host)) continue;

            // Domain controller: ping, LDAP bind (389), REST GET.
            targets.Add(new DiagnosticsTarget
            {
                Id = $"dc:{host}",
                Name = host,
                Host = host,
                Dn = dc.Dn,
                ServerType = DiagnosticsServerType.DomainController,
                PrimaryPort = LdapPort,
                ApplicableTests =
                {
                    DiagnosticsTestType.Ping,
                    DiagnosticsTestType.LdapBind
                }
            });

            // Global catalog: only DCs flagged msDS-isGC. Probe GC port 3268 + REST GET.
            if (dc.IsGlobalCatalog)
            {
                targets.Add(new DiagnosticsTarget
                {
                    Id = $"gc:{host}",
                    Name = host,
                    Host = host,
                    Dn = dc.Dn,
                    ServerType = DiagnosticsServerType.GlobalCatalog,
                    PrimaryPort = GcPort,
                    ApplicableTests =
                    {
                        DiagnosticsTestType.Ping,
                        DiagnosticsTestType.LdapBind
                    }
                });
            }
        }

        return targets;
    }

    private List<DiagnosticsTarget> BuildEntraTargets(DashboardSummary summary)
    {
        var targets = new List<DiagnosticsTarget>();

        foreach (var tenant in summary.EntraTotals?.Tenants ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(tenant)) continue;

            // Entra reachability: OpenID Connect discovery (well-known) document acts as a ping.
            var openIdUrl = $"https://login.microsoftonline.com/{Uri.EscapeDataString(tenant)}/v2.0/.well-known/openid-configuration";
            targets.Add(new DiagnosticsTarget
            {
                Id = $"entra:{tenant}",
                Name = tenant,
                Host = "login.microsoftonline.com",
                Url = openIdUrl,
                Dn = tenant,
                ServerType = DiagnosticsServerType.EntraId,
                PrimaryPort = HttpsPort,
                ApplicableTests =
                {
                    DiagnosticsTestType.EntraOpenId,
                    DiagnosticsTestType.RestGet
                }
            });
        }

        return targets;
    }

    private List<DiagnosticsTarget> BuildExchangeTargets(DashboardSummary summary)
    {
        var targets = new List<DiagnosticsTarget>();

        // Exchange servers are AD-joined hosts. Probe reachability (ping / TCP 443 fallback) and
        // the client-access HTTPS endpoint. Server inventory comes from the Exchange Servers KPI.
        if (summary.ExchangeKpis != null
            && summary.ExchangeKpis.TryGetValue("ExchangeServersKpi", out var exchangeServers)
            && exchangeServers?.Items != null)
        {
            foreach (var server in exchangeServers.Items)
            {
                var host = server.Name;
                if (string.IsNullOrWhiteSpace(host)) continue;

                targets.Add(new DiagnosticsTarget
                {
                    Id = $"exchange:{host}",
                    Name = host,
                    Host = host,
                    Dn = server.Dn,
                    // Exchange's built-in OWA health-check endpoint returns HTTP 200 (body "200")
                    // when the mailbox role is healthy and requires no authentication, making it the
                    // most meaningful zero-permission reachability/health probe for a CAS/mailbox host.
                    Url = $"https://{host}/owa/healthcheck.htm",
                    ServerType = DiagnosticsServerType.ExchangeServer,
                    PrimaryPort = HttpsPort,
                    ApplicableTests =
                    {
                        DiagnosticsTestType.Ping,
                        DiagnosticsTestType.WebHttp
                    }
                });
            }
        }

        return targets;
    }
}