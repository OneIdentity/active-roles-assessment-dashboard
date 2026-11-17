using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;

namespace ActiveRolesDashboard.Tests.Diagnostics;

/// <summary>
/// Target discovery for each diagnostics dashboard: which targets are built from the collected
/// summary and configuration, their ids, ports and applicable tests, and which inputs are skipped.
/// </summary>
[Trait("Stage", "Diagnostics")]
public class DiagnosticsTargetProviderTests
{
    private static DiagnosticsTargetProvider Create(string rstsUrl = "", string webInterfaceUrl = "")
        => new(new TestOptionsMonitor<ActiveRolesConfig>(new ActiveRolesConfig { RstsUrl = rstsUrl, WebInterfaceUrl = webInterfaceUrl }));

    private static DiagnosticsTarget Single(IReadOnlyList<DiagnosticsTarget> targets, string id)
        => Assert.Single(targets, t => t.Id == id);

    // ------------------------------------------------------------------ Active Roles

    [Fact]
    public void Active_roles_servers_use_ars_adsi_port_and_skip_blank_names()
    {
        var summary = new DashboardSummary();
        summary.Servers.Items.Add(new ServerInfo { ServerName = "ars01.test.local", Guid = "g1" });
        summary.Servers.Items.Add(new ServerInfo { ServerName = " " });

        var targets = Create().BuildTargets(DiagnosticsDashboard.ActiveRoles, summary);

        var ars = Assert.Single(targets);
        Assert.Equal("ar-server:ars01.test.local", ars.Id);
        Assert.Equal("ars01.test.local", ars.Host);
        Assert.Equal("g1", ars.Dn);
        Assert.Equal(DiagnosticsServerType.ActiveRolesServer, ars.ServerType);
        Assert.Equal(15172, ars.PrimaryPort);
        Assert.Equal([DiagnosticsTestType.Ping, DiagnosticsTestType.LdapBind, DiagnosticsTestType.RestGet], ars.ApplicableTests);
    }

    [Theory]
    [InlineData("https://rsts.test.local/RSTS", 443)]
    [InlineData("https://rsts.test.local:8443/RSTS", 8443)]
    [InlineData("http://rsts.test.local/RSTS", 80)]
    public void Rsts_target_built_from_configured_url(string url, int expectedPort)
    {
        var targets = Create(rstsUrl: url).BuildTargets(DiagnosticsDashboard.ActiveRoles, new DashboardSummary());

        var rsts = Single(targets, "rsts");
        Assert.Equal("rsts.test.local", rsts.Host);
        Assert.Equal(url, rsts.Url);
        Assert.Equal(DiagnosticsServerType.Rsts, rsts.ServerType);
        Assert.Equal(expectedPort, rsts.PrimaryPort);
        Assert.Equal([DiagnosticsTestType.Ping, DiagnosticsTestType.RstsAuth], rsts.ApplicableTests);
    }

    [Fact]
    public void Web_interface_target_built_only_when_configured()
    {
        var provider = Create(webInterfaceUrl: "https://web.test.local:4443/ARWebAdmin");

        var web = Single(provider.BuildTargets(DiagnosticsDashboard.ActiveRoles, new DashboardSummary()), "web-interface");

        Assert.Equal("web.test.local", web.Host);
        Assert.Equal(4443, web.PrimaryPort);
        Assert.Equal(DiagnosticsServerType.WebInterface, web.ServerType);
        Assert.Equal([DiagnosticsTestType.Ping, DiagnosticsTestType.WebHttp], web.ApplicableTests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    public void Missing_or_invalid_urls_produce_no_rsts_or_web_targets(string url)
    {
        var targets = Create(rstsUrl: url, webInterfaceUrl: url).BuildTargets(DiagnosticsDashboard.ActiveRoles, new DashboardSummary());

        Assert.Empty(targets);
    }

    [Fact]
    public void Sql_targets_are_deduplicated_per_alias_and_database()
    {
        var summary = new DashboardSummary();
        summary.ConfigDatabases.Items.AddRange(
        [
            new DatabaseInfo { SqlAlias = "sql01", DatabaseName = "ARConfig" },
            new DatabaseInfo { SqlAlias = "SQL01", DatabaseName = "arconfig" },
            new DatabaseInfo { SqlAlias = "sql01", DatabaseName = "ARConfig2" },
            new DatabaseInfo { SqlAlias = "sql02", DatabaseName = "" },
            new DatabaseInfo { SqlAlias = "", DatabaseName = "Orphan" },
        ]);

        var targets = Create().BuildTargets(DiagnosticsDashboard.ActiveRoles, summary);

        Assert.Equal(["sql:sql01:ARConfig", "sql:sql01:ARConfig2", "sql:sql02:"], targets.Select(t => t.Id));
        var first = targets[0];
        Assert.Equal("sql01 (ARConfig)", first.Name);
        Assert.Equal("ARConfig", first.Dn);
        Assert.Equal(1433, first.PrimaryPort);
        Assert.Equal(DiagnosticsServerType.SqlServer, first.ServerType);
        Assert.Equal([DiagnosticsTestType.Ping, DiagnosticsTestType.SqlQuery], first.ApplicableTests);
        Assert.Equal("sql02", targets[2].Name);
    }

    [Fact]
    public void Active_roles_dashboard_combines_all_target_kinds_in_order()
    {
        var summary = new DashboardSummary();
        summary.Servers.Items.Add(new ServerInfo { ServerName = "ars01" });
        summary.ConfigDatabases.Items.Add(new DatabaseInfo { SqlAlias = "sql01", DatabaseName = "ARConfig" });

        var targets = Create("https://rsts/RSTS", "https://web/").BuildTargets(DiagnosticsDashboard.ActiveRoles, summary);

        Assert.Equal(["ar-server:ars01", "rsts", "web-interface", "sql:sql01:ARConfig"], targets.Select(t => t.Id));
    }

    // ------------------------------------------------------------------ Active Directory

    [Fact]
    public void Domain_controllers_get_ldap_target_and_gc_target_only_when_global_catalog()
    {
        var summary = new DashboardSummary();
        summary.DomainControllers.Items.AddRange(
        [
            new DomainControllerInfo { Name = "dc01", Dn = "CN=DC01", IsGlobalCatalog = true },
            new DomainControllerInfo { Name = "dc02", Dn = "CN=DC02", IsGlobalCatalog = false },
            new DomainControllerInfo { Name = "" },
        ]);

        var targets = Create().BuildTargets(DiagnosticsDashboard.ActiveDirectory, summary);

        Assert.Equal(["dc:dc01", "gc:dc01", "dc:dc02"], targets.Select(t => t.Id));

        var dc = targets[0];
        Assert.Equal(DiagnosticsServerType.DomainController, dc.ServerType);
        Assert.Equal(389, dc.PrimaryPort);
        Assert.Equal("CN=DC01", dc.Dn);
        Assert.Equal([DiagnosticsTestType.Ping, DiagnosticsTestType.LdapBind], dc.ApplicableTests);

        var gc = targets[1];
        Assert.Equal(DiagnosticsServerType.GlobalCatalog, gc.ServerType);
        Assert.Equal(3268, gc.PrimaryPort);
        Assert.Equal([DiagnosticsTestType.Ping, DiagnosticsTestType.LdapBind], gc.ApplicableTests);
    }

    [Fact]
    public void Active_directory_dashboard_ignores_active_roles_configuration()
    {
        var summary = new DashboardSummary();
        summary.Servers.Items.Add(new ServerInfo { ServerName = "ars01" });

        Assert.Empty(Create("https://rsts/RSTS", "https://web/").BuildTargets(DiagnosticsDashboard.ActiveDirectory, summary));
    }

    // ------------------------------------------------------------------ Entra ID

    [Fact]
    public void Entra_tenants_get_escaped_openid_discovery_url()
    {
        var summary = new DashboardSummary();
        summary.EntraTotals.Tenants.AddRange(["contoso.onmicrosoft.com", "", "a b/c"]);

        var targets = Create().BuildTargets(DiagnosticsDashboard.EntraId, summary);

        Assert.Equal(2, targets.Count);
        var contoso = targets[0];
        Assert.Equal("entra:contoso.onmicrosoft.com", contoso.Id);
        Assert.Equal("contoso.onmicrosoft.com", contoso.Dn);
        Assert.Equal("login.microsoftonline.com", contoso.Host);
        Assert.Equal(443, contoso.PrimaryPort);
        Assert.Equal(DiagnosticsServerType.EntraId, contoso.ServerType);
        Assert.Equal("https://login.microsoftonline.com/contoso.onmicrosoft.com/v2.0/.well-known/openid-configuration", contoso.Url);
        Assert.Equal([DiagnosticsTestType.EntraOpenId, DiagnosticsTestType.RestGet], contoso.ApplicableTests);

        Assert.Equal("https://login.microsoftonline.com/a%20b%2Fc/v2.0/.well-known/openid-configuration", targets[1].Url);
    }

    // ------------------------------------------------------------------ Exchange

    [Fact]
    public void Exchange_servers_get_owa_healthcheck_target()
    {
        var summary = new DashboardSummary();
        summary.ExchangeKpis["exchangeserverskpi"] = new GovernanceKpiSummary
        {
            Items = [new GovernanceKpiInfo { Name = "ex01.test.local", Dn = "CN=EX01" }, new GovernanceKpiInfo { Name = "" }]
        };

        var ex = Assert.Single(Create().BuildTargets(DiagnosticsDashboard.Exchange, summary));

        Assert.Equal("exchange:ex01.test.local", ex.Id);
        Assert.Equal("https://ex01.test.local/owa/healthcheck.htm", ex.Url);
        Assert.Equal("CN=EX01", ex.Dn);
        Assert.Equal(443, ex.PrimaryPort);
        Assert.Equal(DiagnosticsServerType.ExchangeServer, ex.ServerType);
        Assert.Equal([DiagnosticsTestType.Ping, DiagnosticsTestType.WebHttp], ex.ApplicableTests);
    }

    [Fact]
    public void Exchange_without_server_kpi_produces_no_targets()
    {
        var summary = new DashboardSummary();
        summary.ExchangeKpis["OtherKpi"] = new GovernanceKpiSummary { Items = [new GovernanceKpiInfo { Name = "ex01" }] };

        Assert.Empty(Create().BuildTargets(DiagnosticsDashboard.Exchange, summary));
        Assert.Empty(Create().BuildTargets(DiagnosticsDashboard.Exchange, new DashboardSummary { ExchangeKpis = null! }));
    }

    // ------------------------------------------------------------------ general

    [Theory]
    [InlineData(DiagnosticsDashboard.ActiveRoles)]
    [InlineData(DiagnosticsDashboard.ActiveDirectory)]
    [InlineData(DiagnosticsDashboard.EntraId)]
    [InlineData(DiagnosticsDashboard.Exchange)]
    public void Empty_summary_produces_no_targets(DiagnosticsDashboard dashboard)
        => Assert.Empty(Create().BuildTargets(dashboard, new DashboardSummary()));

    [Fact]
    public void Null_collections_in_summary_are_tolerated()
    {
        var summary = new DashboardSummary { Servers = null!, ConfigDatabases = null!, DomainControllers = null!, EntraTotals = null! };

        Assert.Empty(Create().BuildTargets(DiagnosticsDashboard.ActiveRoles, summary));
        Assert.Empty(Create().BuildTargets(DiagnosticsDashboard.ActiveDirectory, summary));
        Assert.Empty(Create().BuildTargets(DiagnosticsDashboard.EntraId, summary));
    }

    [Fact]
    public void Unknown_dashboard_produces_no_targets()
        => Assert.Empty(Create("https://rsts/RSTS").BuildTargets((DiagnosticsDashboard)999, new DashboardSummary()));

    [Fact]
    public void Configuration_changes_are_picked_up_without_recreating_provider()
    {
        var options = new TestOptionsMonitor<ActiveRolesConfig>(new ActiveRolesConfig());
        var provider = new DiagnosticsTargetProvider(options);
        Assert.Empty(provider.BuildTargets(DiagnosticsDashboard.ActiveRoles, new DashboardSummary()));

        options.CurrentValue = new ActiveRolesConfig { RstsUrl = "https://rsts/RSTS" };

        Assert.Single(provider.BuildTargets(DiagnosticsDashboard.ActiveRoles, new DashboardSummary()));
    }
}
