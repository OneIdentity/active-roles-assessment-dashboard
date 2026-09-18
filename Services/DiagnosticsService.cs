using System.Diagnostics;
using System.DirectoryServices;
using System.DirectoryServices.Protocols;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using ActiveRolesDashboard.Models;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// Runs live, on-demand performance / connectivity diagnostics against Active Roles,
/// Active Directory and Entra Id infrastructure. Probes are intentionally NOT part of the
/// cached <see cref="DashboardSummary"/> model: results are volatile and reflect the state
/// at the moment the run is triggered.
/// </summary>
public class DiagnosticsService
{
    // Per-probe time budget. Kept short so a sweep across many servers cannot hang the UI.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
    // Longer budget for HTTP-based probes (REST GET, Web Interface, Entra OpenID). Cloud
    // endpoints (e.g. Entra) can legitimately take well over the 5s default on a cold call.
    private static readonly TimeSpan HttpProbeTimeout = TimeSpan.FromSeconds(20);
    // Upper bound on concurrent probes so a large DC estate does not exhaust sockets/threads.
    private const int MaxConcurrency = 8;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly RstsAuthService _rstsAuthService;
    private readonly ServiceAccountTokenProvider _serviceTokens;
    private readonly ServiceAccountSecretProtector _secretProtector;
    private readonly IOptionsMonitor<ActiveRolesConfig> _config;
    private readonly ILogger<DiagnosticsService> _logger;

    public DiagnosticsService(
        IHttpClientFactory httpClientFactory,
        RstsAuthService rstsAuthService,
        ServiceAccountTokenProvider serviceTokens,
        ServiceAccountSecretProtector secretProtector,
        IOptionsMonitor<ActiveRolesConfig> config,
        ILogger<DiagnosticsService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _rstsAuthService = rstsAuthService;
        _serviceTokens = serviceTokens;
        _secretProtector = secretProtector;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Executes a diagnostics run, honouring the request's server-type / test-type / target-id
    /// filters. Probes are fanned out with bounded concurrency and a per-probe timeout.
    /// </summary>
    public async Task<DiagnosticsResult> RunAsync(
        DiagnosticsRequest request,
        IReadOnlyList<DiagnosticsTarget> targets,
        CancellationToken cancellationToken = default)
    {
        var result = new DiagnosticsResult
        {
            Dashboard = request.Dashboard,
            StartedUtc = DateTimeOffset.UtcNow
        };

        // Build the (target, test) work list after applying filters.
        var work = new List<(DiagnosticsTarget Target, DiagnosticsTestType Test)>();
        foreach (var target in targets)
        {
            if (request.ServerType.HasValue && target.ServerType != request.ServerType.Value)
                continue;
            if (request.TargetIds.Count > 0 && !request.TargetIds.Contains(target.Id))
                continue;

            foreach (var test in target.ApplicableTests)
            {
                if (request.TestType.HasValue && test != request.TestType.Value)
                    continue;
                work.Add((target, test));
            }
        }

        using var throttler = new SemaphoreSlim(MaxConcurrency);
        var tasks = work.Select(async item =>
        {
            await throttler.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await ExecuteProbeAsync(item.Target, item.Test, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                throttler.Release();
            }
        });

        var probes = await Task.WhenAll(tasks).ConfigureAwait(false);
        result.Probes.AddRange(probes.OrderBy(p => p.TargetName).ThenBy(p => p.TestType));
        result.CompletedUtc = DateTimeOffset.UtcNow;
        return result;
    }

    private async Task<DiagnosticsProbeResult> ExecuteProbeAsync(
        DiagnosticsTarget target, DiagnosticsTestType test, CancellationToken cancellationToken)
    {
        var probe = new DiagnosticsProbeResult
        {
            TargetId = target.Id,
            TargetName = target.Name,
            ServerType = target.ServerType,
            TestType = test
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // HTTP-based probes get a longer budget; everything else uses the short default.
        var budget = test is DiagnosticsTestType.RestGet or DiagnosticsTestType.WebHttp or DiagnosticsTestType.EntraOpenId
            ? HttpProbeTimeout
            : ProbeTimeout;
        cts.CancelAfter(budget);

        try
        {
            switch (test)
            {
                case DiagnosticsTestType.Ping:
                    await PingOrTcpAsync(target, probe, cts.Token).ConfigureAwait(false);
                    break;
                case DiagnosticsTestType.LdapBind:
                    await LdapBindAsync(target, probe).ConfigureAwait(false);
                    break;
                case DiagnosticsTestType.RestGet:
                    await RestGetAsync(target, probe, cts.Token).ConfigureAwait(false);
                    break;
                case DiagnosticsTestType.RstsAuth:
                    await RstsAuthAsync(probe).ConfigureAwait(false);
                    break;
                case DiagnosticsTestType.WebHttp:
                    // Web Interface / Exchange CAS endpoints commonly present internal-CA or
                    // self-signed certificates. This is a reachability/latency probe, so certificate
                    // trust is intentionally not validated.
                    await HttpGetAsync(target.Url, probe, cts.Token, allowUntrustedCertificate: true).ConfigureAwait(false);
                    break;
                case DiagnosticsTestType.EntraOpenId:
                    await HttpGetAsync(target.Url, probe, cts.Token).ConfigureAwait(false);
                    break;
                case DiagnosticsTestType.SqlQuery:
                    await SqlQueryAsync(target, probe, cts.Token).ConfigureAwait(false);
                    break;
                default:
                    probe.Status = DiagnosticsStatus.Skipped;
                    probe.Message = "Unsupported test type.";
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            probe.Status = DiagnosticsStatus.Fail;
            probe.Message = $"Timed out after {budget.TotalSeconds:0}s.";
        }
        catch (Exception ex)
        {
            probe.Status = DiagnosticsStatus.Fail;
            probe.Message = ex.Message;
        }

        return probe;
    }

    /// <summary>
    /// ICMP ping first; if ICMP fails or is blocked, fall back to a TCP connect on the
    /// target's primary port so firewall-blocked-ICMP hosts are not reported as down.
    /// </summary>
    private async Task PingOrTcpAsync(DiagnosticsTarget target, DiagnosticsProbeResult probe, CancellationToken ct)
    {
        var host = string.IsNullOrWhiteSpace(target.Host) ? target.Name : target.Host;
        var sw = Stopwatch.StartNew();

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, (int)ProbeTimeout.TotalMilliseconds).ConfigureAwait(false);
            if (reply.Status == IPStatus.Success)
            {
                probe.Status = DiagnosticsStatus.Ok;
                probe.LatencyMs = reply.RoundtripTime;
                probe.Message = $"ICMP reply from {reply.Address} in {reply.RoundtripTime} ms.";
                return;
            }
        }
        catch
        {
            // ICMP may be unavailable (permissions) or blocked; fall through to TCP.
        }

        // TCP-connect fallback.
        if (target.PrimaryPort <= 0)
        {
            probe.Status = DiagnosticsStatus.Fail;
            probe.Message = "ICMP failed and no TCP fallback port is defined.";
            return;
        }

        sw.Restart();
        try
        {
            using var tcp = new TcpClient();
            var connectTask = tcp.ConnectAsync(host, target.PrimaryPort);
            var completed = await Task.WhenAny(connectTask, Task.Delay(ProbeTimeout, ct)).ConfigureAwait(false);
            if (completed == connectTask && tcp.Connected)
            {
                probe.Status = DiagnosticsStatus.Warn;
                probe.LatencyMs = sw.ElapsedMilliseconds;
                probe.Message = $"ICMP blocked; TCP {target.PrimaryPort} reachable in {sw.ElapsedMilliseconds} ms.";
            }
            else
            {
                probe.Status = DiagnosticsStatus.Fail;
                probe.Message = $"ICMP failed and TCP {target.PrimaryPort} unreachable.";
            }
        }
        catch (Exception ex)
        {
            probe.Status = DiagnosticsStatus.Fail;
            probe.Message = $"ICMP failed and TCP {target.PrimaryPort} error: {ex.Message}";
        }
    }

    /// <summary>
    /// Binds to the target host's RootDSE using the configured service account. A successful
    /// bind confirms both LDAP reachability and that the service account can authenticate.
    /// For Active Roles administration servers the bind is performed through the Active Roles
    /// ADSI provider (EDMS://) so the probe exercises the ARS directory path rather than raw LDAP.
    /// </summary>
    private Task LdapBindAsync(DiagnosticsTarget target, DiagnosticsProbeResult probe)
    {
        var sa = _config.CurrentValue.ServiceAccount;
        if (string.IsNullOrWhiteSpace(sa.Username))
        {
            probe.Status = DiagnosticsStatus.Skipped;
            probe.Message = "Service account is not configured.";
            return Task.CompletedTask;
        }

        string password;
        try
        {
            password = _secretProtector.Unprotect(sa.ProtectedPassword);
        }
        catch (Exception ex)
        {
            probe.Status = DiagnosticsStatus.Fail;
            probe.Message = $"Could not read service-account password: {ex.Message}";
            return Task.CompletedTask;
        }

        var host = string.IsNullOrWhiteSpace(target.Host) ? target.Name : target.Host;

        // Active Roles administration servers expose the directory via the ARS ADSI provider,
        // which is a superset of the Microsoft ADSI provider. Bind through EDMS:// so the probe
        // measures the real Active Roles path; other targets (DCs / GCs) use raw LDAP.
        if (target.ServerType == DiagnosticsServerType.ActiveRolesServer)
        {
            if (!OperatingSystem.IsWindows())
            {
                probe.Status = DiagnosticsStatus.Skipped;
                probe.Message = "Active Roles ADSI bind is only available on Windows.";
                return Task.CompletedTask;
            }

            return Task.Run(() => ArsAdsiBind(host, sa.Username, password, probe));
        }

        var port = target.PrimaryPort > 0 ? target.PrimaryPort : 389;
        var sw = Stopwatch.StartNew();

        try
        {
            var identifier = new LdapDirectoryIdentifier(host, port);
            using var connection = new LdapConnection(identifier)
            {
                AuthType = AuthType.Negotiate,
                Timeout = ProbeTimeout
            };
            connection.SessionOptions.ProtocolVersion = 3;
            if (port == 636 || port == 3269)
            {
                connection.SessionOptions.SecureSocketLayer = true;
                if (_config.CurrentValue.IgnoreSslErrors)
                    connection.SessionOptions.VerifyServerCertificate = (_, _) => true;
            }

            connection.Bind(BuildLdapCredential(sa.Username, password));

            // Read RootDSE to confirm a working LDAP session, not just a bind handshake.
            var request = new SearchRequest(null, "(objectClass=*)", System.DirectoryServices.Protocols.SearchScope.Base, "defaultNamingContext");
            var response = (SearchResponse)connection.SendRequest(request);

            probe.Status = DiagnosticsStatus.Ok;
            probe.LatencyMs = sw.ElapsedMilliseconds;
            probe.Message = response.Entries.Count > 0
                ? $"LDAP bind + RootDSE read on port {port} in {sw.ElapsedMilliseconds} ms."
                : $"LDAP bind on port {port} succeeded in {sw.ElapsedMilliseconds} ms.";
        }
        catch (LdapException ex)
        {
            probe.Status = DiagnosticsStatus.Fail;
            probe.Message = $"LDAP bind failed on port {port}: {ex.Message}";
        }
        catch (Exception ex)
        {
            probe.Status = DiagnosticsStatus.Fail;
            probe.Message = $"LDAP error on port {port}: {ex.Message}";
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds a <see cref="NetworkCredential"/> for a Negotiate LDAP bind. Windows Negotiate /
    /// Kerberos requires the domain to be supplied explicitly for the down-level
    /// (<c>DOMAIN\user</c>) form; passing "DOMAIN\user" in the user-name field alone yields
    /// "The supplied credential is invalid." UPN (<c>user@domain</c>) credentials are passed
    /// whole with an empty domain, which Negotiate resolves natively.
    /// </summary>
    private static NetworkCredential BuildLdapCredential(string username, string password)
    {
        if (!string.IsNullOrWhiteSpace(username))
        {
            var slash = username.IndexOf('\\');
            if (slash > 0)
            {
                // DOMAIN\user -> split domain out so Negotiate/Kerberos accepts it.
                var domain = username.Substring(0, slash);
                var user = username.Substring(slash + 1);
                return new NetworkCredential(user, password, domain);
            }
        }

        // UPN (user@domain) or a bare account name: pass as-is with no separate domain.
        return new NetworkCredential(username, password);
    }

    /// <summary>
    /// Binds to an Active Roles administration server through the Active Roles ADSI provider
    /// (EDMS://). Binding to CN=Configuration (an object that always exists and is visible to all)
    /// on the specific server confirms the service account can authenticate against the ARS
    /// directory path and records the latency.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private void ArsAdsiBind(string host, string username, string password, DiagnosticsProbeResult probe)
    {
        // ADS_EDMSERVER_BIND (32768): forces ADSI to connect to the Administration Service running
        // on the server named in the binding string. Without it the provider connects to ANY
        // available Administration Service in the domain, which would not measure this target.
        const AuthenticationTypes ArsEdmServerBind = (AuthenticationTypes)0x8000;

        var sw = Stopwatch.StartNew();
        try
        {
            // EDMS://<server>/<DistinguishedName>. CN=Configuration is stable and always readable.
            var path = $"EDMS://{host}/CN=Configuration";
            using var entry = new DirectoryEntry(
                path,
                username,
                password,
                AuthenticationTypes.Secure | ArsEdmServerBind);

            // Force the bind + a real read by touching a property (Guid is always present).
            _ = entry.Guid;
            sw.Stop();

            probe.Status = DiagnosticsStatus.Ok;
            probe.LatencyMs = sw.ElapsedMilliseconds;
            probe.Message = $"Active Roles ADSI bind (CN=Configuration) in {sw.ElapsedMilliseconds} ms.";
        }
        catch (Exception ex)
        {
            sw.Stop();
            probe.Status = DiagnosticsStatus.Fail;
            probe.LatencyMs = sw.ElapsedMilliseconds;
            probe.Message = $"Active Roles ADSI bind failed: {ex.Message}";
        }
    }

    /// <summary>Authenticated Active Roles REST GET of the target directory object.</summary>
    private async Task RestGetAsync(DiagnosticsTarget target, DiagnosticsProbeResult probe, CancellationToken ct)
    {
        var config = _config.CurrentValue;

        string token;
        try
        {
            token = await _serviceTokens.GetTokenAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            probe.Status = DiagnosticsStatus.Fail;
            probe.Message = $"Could not acquire service-account token: {ex.Message}";
            return;
        }

        var client = _httpClientFactory.CreateClient("ActiveRolesApi");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Entra targets are not Active Roles servers (their host is login.microsoftonline.com),
        // so the REST GET must hit the configured Active Roles API server and request the tenant
        // container object, e.g. {ApiBaseUrl}/objects/{tenantGuid}?includeattributes=all. The
        // target only carries the tenant name (RDN), so resolve its objectGUID first.
        string url;
        if (target.ServerType == DiagnosticsServerType.EntraId)
        {
            if (string.IsNullOrWhiteSpace(config.ApiBaseUrl))
            {
                probe.Status = DiagnosticsStatus.Skipped;
                probe.Message = "No Active Roles API base URL is configured.";
                return;
            }

            var apiBase = config.ApiBaseUrl.TrimEnd('/');
            var tenantName = !string.IsNullOrWhiteSpace(target.Dn) ? target.Dn : target.Name;
            if (string.IsNullOrWhiteSpace(tenantName))
            {
                probe.Status = DiagnosticsStatus.Skipped;
                probe.Message = "No tenant name is available for this target.";
                return;
            }

            var (tenantGuid, resolveError) = await ResolveEntraTenantGuidAsync(client, apiBase, config.DefaultAzureConfigurationDN, tenantName, ct)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(tenantGuid))
            {
                probe.Status = DiagnosticsStatus.Fail;
                probe.Message = resolveError ?? $"Could not resolve the Active Roles object for tenant '{tenantName}'.";
                return;
            }

            url = $"{apiBase}/objects/{Uri.EscapeDataString(tenantGuid)}?includeattributes=all";
        }
        else
        {
            var host = !string.IsNullOrWhiteSpace(target.Host) ? target.Host : target.Name;
            if (string.IsNullOrWhiteSpace(host))
            {
                probe.Status = DiagnosticsStatus.Skipped;
                probe.Message = "No server host is available for this target.";
                return;
            }

            url = $"{BuildServerApiBaseUrl(host, config.ApiBaseUrl)}/containers";
        }

        var sw = Stopwatch.StartNew();
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        sw.Stop();

        if (response.IsSuccessStatusCode)
        {
            probe.Status = DiagnosticsStatus.Ok;
            probe.LatencyMs = sw.ElapsedMilliseconds;
            probe.Message = $"REST GET {(int)response.StatusCode} in {sw.ElapsedMilliseconds} ms.";
        }
        else
        {
            probe.Status = DiagnosticsStatus.Fail;
            probe.LatencyMs = sw.ElapsedMilliseconds;
            probe.Message = $"REST GET returned {(int)response.StatusCode} {response.ReasonPhrase}.";
        }
    }

    /// <summary>
    /// Resolves the Active Roles objectGUID of an Entra tenant container from its name (RDN) by
    /// searching the Azure configuration base for the <c>edsAzureTenantcontainer</c> with that name.
    /// Returns the GUID, or a diagnostic error message describing why resolution failed.
    /// </summary>
    private static async Task<(string? Guid, string? Error)> ResolveEntraTenantGuidAsync(
        HttpClient client, string apiBase, string azureConfigDn, string tenantName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(azureConfigDn))
            return (null, "No Azure configuration base DN is configured.");

        var filter = $"(&(objectClass=edsAzureTenantcontainer)(name={tenantName}))";
        // No attributes restriction: the API returns objectGUID by default, and restricting the
        // attribute set can suppress it. The GUID is read case-insensitively from the response.
        var searchUrl =
            $"{apiBase}/objects?base={Uri.EscapeDataString(azureConfigDn)}&filter={Uri.EscapeDataString(filter)}&scope=one";

        using var response = await client.GetAsync(searchUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return (null, $"Tenant lookup returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        if (!doc.RootElement.TryGetProperty("items", out var items)
            || items.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return (null, $"Tenant '{tenantName}' was not found under {azureConfigDn}.");
        }

        foreach (var item in items.EnumerateArray())
        {
            var guid = ReadGuidValue(item);
            if (!string.IsNullOrWhiteSpace(guid))
            {
                return (guid.Trim('{', '}'), null);
            }
        }

        return (null, $"Tenant '{tenantName}' matched no object (or the object had no GUID) under {azureConfigDn}.");
    }

    /// <summary>
    /// Reads an object GUID from an Active Roles REST object element. Active Roles returns the GUID
    /// as an <c>objectGUID</c> property (casing varies) and may also nest it under <c>attributes</c>.
    /// Property matching is case-insensitive and single-element arrays are unwrapped.
    /// </summary>
    private static string? ReadGuidValue(System.Text.Json.JsonElement item)
    {
        if (TryGetGuid(item, out var guid)) return guid;
        if (TryGetProperty(item, "attributes", out var attrs) && TryGetGuid(attrs, out guid)) return guid;
        return null;

        static bool TryGetGuid(System.Text.Json.JsonElement source, out string? value)
        {
            value = null;
            // The Active Roles REST API returns the GUID as "objectGUID"; also accept "id".
            if (!TryGetProperty(source, "objectGUID", out var prop)
                && !TryGetProperty(source, "id", out prop))
            {
                return false;
            }

            if (prop.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var element in prop.EnumerateArray())
                {
                    if (element.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        value = element.GetString();
                        return !string.IsNullOrWhiteSpace(value);
                    }
                }
                return false;
            }

            if (prop.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                value = prop.GetString();
                return !string.IsNullOrWhiteSpace(value);
            }

            return false;
        }
    }

    /// <summary>Case-insensitive lookup of a JSON object property.</summary>
    private static bool TryGetProperty(System.Text.Json.JsonElement source, string name, out System.Text.Json.JsonElement value)
    {
        value = default;
        if (source.ValueKind != System.Text.Json.JsonValueKind.Object) return false;

        foreach (var property in source.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Builds the per-server Active Roles REST base URL (e.g. https://server/api/v1). The scheme,
    /// port and API path (/api/v1) are taken from the configured <see cref="ActiveRolesConfig.ApiBaseUrl"/>
    /// so alternate ports / API versions are honoured, but the host is replaced with the probe target's
    /// host so each server is tested directly rather than always hitting the primary API server.
    /// </summary>
    private static string BuildServerApiBaseUrl(string host, string? configuredApiBaseUrl)
    {
        if (!string.IsNullOrWhiteSpace(configuredApiBaseUrl)
            && Uri.TryCreate(configuredApiBaseUrl, UriKind.Absolute, out var configuredUri))
        {
            var builder = new UriBuilder(configuredUri) { Host = host };
            return builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        }

        return $"https://{host}/api/v1";
    }

    /// <summary>RSTS OAuth2 token request using the service account (authentication probe).</summary>
    private async Task RstsAuthAsync(DiagnosticsProbeResult probe)
    {
        var sa = _config.CurrentValue.ServiceAccount;
        if (string.IsNullOrWhiteSpace(sa.Username))
        {
            probe.Status = DiagnosticsStatus.Skipped;
            probe.Message = "Service account is not configured.";
            return;
        }

        string password;
        try
        {
            password = _secretProtector.Unprotect(sa.ProtectedPassword);
        }
        catch (Exception ex)
        {
            probe.Status = DiagnosticsStatus.Fail;
            probe.Message = $"Could not read service-account password: {ex.Message}";
            return;
        }

        var sw = Stopwatch.StartNew();
        var tokenResult = await _rstsAuthService.GetTokenAsync(sa.Username, password).ConfigureAwait(false);
        sw.Stop();

        if (tokenResult.Success)
        {
            probe.Status = DiagnosticsStatus.Ok;
            probe.LatencyMs = sw.ElapsedMilliseconds;
            probe.Message = $"RSTS token acquired in {sw.ElapsedMilliseconds} ms.";
        }
        else
        {
            probe.Status = DiagnosticsStatus.Fail;
            probe.LatencyMs = sw.ElapsedMilliseconds;
            probe.Message = $"RSTS authentication failed: {tokenResult.Error ?? "unknown error"}.";
        }
    }

    /// <summary>Plain HTTP GET of a URL (Web Interface reachability or Entra OpenID metadata).</summary>
    /// <param name="allowUntrustedCertificate">
    /// When true, server certificate trust errors are ignored. Used for internal reachability probes
    /// (Web Interface / Exchange CAS) that frequently present self-signed or internal-CA certificates.
    /// </param>
    private async Task HttpGetAsync(string url, DiagnosticsProbeResult probe, CancellationToken ct, bool allowUntrustedCertificate = false)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            probe.Status = DiagnosticsStatus.Skipped;
            probe.Message = "No URL is configured for this target.";
            return;
        }

        HttpClient client;
        HttpClientHandler? ownedHandler = null;
        if (allowUntrustedCertificate)
        {
            // Dedicated handler so ignoring certificate trust never leaks into the shared clients.
            ownedHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            client = new HttpClient(ownedHandler) { Timeout = HttpProbeTimeout };
        }
        else
        {
            client = _httpClientFactory.CreateClient("RSTS");
            client.Timeout = HttpProbeTimeout;
        }

        try
        {
            var sw = Stopwatch.StartNew();
            var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            sw.Stop();

            // This is a reachability / latency probe: any HTTP response means the server answered and
            // we have a valid timing. Auth challenges (401/403) and other client-side statuses are
            // therefore treated as OK. Only server-side failures (5xx) are surfaced as a warning; a
            // total failure to connect throws and is classified as Fail by the caller.
            probe.LatencyMs = sw.ElapsedMilliseconds;
            var statusCode = (int)response.StatusCode;
            if (statusCode >= 500)
            {
                probe.Status = DiagnosticsStatus.Warn;
                probe.Message = $"HTTP {statusCode} {response.ReasonPhrase} in {sw.ElapsedMilliseconds} ms.";
            }
            else
            {
                probe.Status = DiagnosticsStatus.Ok;
                probe.Message = $"HTTP {statusCode} in {sw.ElapsedMilliseconds} ms.";
            }
        }
        finally
        {
            if (ownedHandler != null)
            {
                client.Dispose();
                ownedHandler.Dispose();
            }
        }
    }

    /// <summary>
    /// Connects to the Active Roles configuration SQL Server (using the service identity /
    /// integrated security) and runs a lightweight COUNT against the ARServices table of the
    /// configuration database. Measures connect + query latency.
    /// </summary>
    private async Task SqlQueryAsync(DiagnosticsTarget target, DiagnosticsProbeResult probe, CancellationToken ct)
    {
        var server = string.IsNullOrWhiteSpace(target.Host) ? target.Name : target.Host;
        var database = target.Dn; // config database name carried on the target's Dn.
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
        {
            probe.Status = DiagnosticsStatus.Skipped;
            probe.Message = "No SQL server / configuration database is available for this target.";
            return;
        }

        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = database,
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            Encrypt = true,
            ConnectTimeout = (int)ProbeTimeout.TotalSeconds,
            ApplicationName = "ActiveRolesDashboard-Diagnostics"
        };

        var sw = Stopwatch.StartNew();
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM dbo.ARServices";
        command.CommandTimeout = (int)ProbeTimeout.TotalSeconds;
        var scalar = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        sw.Stop();

        var rows = scalar is int i ? i : Convert.ToInt32(scalar);
        probe.Status = DiagnosticsStatus.Ok;
        probe.LatencyMs = sw.ElapsedMilliseconds;
        probe.Message = $"SQL query returned {rows} ARServices row(s) in {sw.ElapsedMilliseconds} ms.";
    }
}
