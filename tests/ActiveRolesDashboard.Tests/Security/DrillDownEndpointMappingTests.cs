using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ActiveRolesDashboard.Tests.Security;

/// <summary>
/// Signed-in mapping coverage for the typed DrillDown endpoints not exercised by
/// <see cref="DrillDownGroupTreeAndViewTests"/>: each test feeds raw Active Roles search
/// results through the real API and asserts the serialized summary fields.
/// </summary>
[Trait("Stage", "Security")]
public class DrillDownEndpointMappingTests : IDisposable
{
    private readonly FakeActiveRolesServer _server = new FakeActiveRolesServer().WithHealthyDefaults();
    private DashboardAppFactory? _app;

    private async Task<HttpClient> SignedIn()
    {
        _app = new DashboardAppFactory(activeRoles: _server);
        var client = _app.CreateNonRedirectingClient();
        var login = new HttpRequestMessage(HttpMethod.Post, "/Login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Username"] = FakeActiveRolesServer.ValidUser,
                ["Password"] = FakeActiveRolesServer.ValidPassword
            })
        };
        login.Headers.Accept.ParseAdd("application/json");
        var response = await client.SendAsync(login);
        Assert.Contains("/app/", await response.Content.ReadAsStringAsync());

        _app.Services.GetRequiredService<PerUserSummaryCache>()
            .SetSummary(FakeActiveRolesServer.ValidUser, JsonSerializer.Serialize(new DashboardSummary()));
        return client;
    }

    private static async Task<JsonElement> GetJson(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static JsonObject Obj(params (string Key, JsonNode? Value)[] props)
    {
        var o = new JsonObject();
        foreach (var (k, v) in props) o[k] = v;
        return o;
    }

    private static JsonElement ItemNamed(JsonElement summary, string property, string value) =>
        summary.GetProperty("items").EnumerateArray().Single(i => i.GetProperty(property).GetString() == value);

    [Fact]
    public async Task Servers_map_computer_name_version_and_verbose_logging()
    {
        _server.WithSearch("objectClass=edsARService", [
            Obj(("edsaEdmServiceComputerName", "ARS01"), ("edsvaPublicProductVersion", "8.2.1"), ("edsvaDiagnosticLogTurnedOn", "TRUE")),
            Obj(("edsaEdmServiceComputerName", "ARS02"), ("edsvaPublicProductVersion", "8.2.0"), ("edsvaDiagnosticLogTurnedOn", "FALSE"))
        ]);
        using var client = await SignedIn();

        var json = await GetJson(client, "/api/DrillDown/servers");

        Assert.Equal(2, json.GetProperty("totalCount").GetInt32());
        var ars01 = ItemNamed(json, "serverName", "ARS01");
        Assert.Equal("8.2.1", ars01.GetProperty("version").GetString());
        Assert.True(ars01.GetProperty("verboseLogging").GetBoolean());
        Assert.False(ItemNamed(json, "serverName", "ARS02").GetProperty("verboseLogging").GetBoolean());
    }

    [Fact]
    public async Task Domains_map_dns_name_and_override_flag()
    {
        _server.WithSearch("objectClass=edsDomainCacheConfig", [
            Obj(("name", "test"), ("edsvaDomainDNS", "DC=test,DC=local"), ("edsaSavedDnsName", "test.local"), ("edsaUseOverrideAccount", "True"))
        ]);
        using var client = await SignedIn();

        var json = await GetJson(client, "/api/DrillDown/domains");

        var domain = ItemNamed(json, "name", "test");
        Assert.Equal("test.local", domain.GetProperty("dnsName").GetString());
        Assert.Equal("DC=test,DC=local", domain.GetProperty("dn").GetString());
        Assert.True(domain.GetProperty("useOverride").GetBoolean());
    }

    [Fact]
    public async Task Workflows_distinguish_automation_workflows_and_enabled_state()
    {
        _server.WithSearch("objectClass=edsWorkflowDefinition", [
            Obj(("name", "Approval"), ("distinguishedName", "CN=Approval,CN=Workflow,CN=Policies,CN=Configuration"),
                ("objectClass", "edsWorkflowDefinition"), ("edsaWorkflowIsDisabled", "false")),
            Obj(("name", "Nightly"), ("distinguishedName", "CN=Nightly,CN=Workflow,CN=Policies,CN=Configuration"),
                ("objectClass", "edsAutomationWorkflowDefinition"), ("edsaWorkflowIsDisabled", "true"))
        ]);
        using var client = await SignedIn();

        var json = await GetJson(client, "/api/DrillDown/workflows");

        var approval = ItemNamed(json, "name", "Approval");
        Assert.True(approval.GetProperty("isEnabled").GetBoolean());
        Assert.False(approval.GetProperty("isAutomationWorkflow").GetBoolean());
        var nightly = ItemNamed(json, "name", "Nightly");
        Assert.False(nightly.GetProperty("isEnabled").GetBoolean());
        Assert.True(nightly.GetProperty("isAutomationWorkflow").GetBoolean());
    }

    [Fact]
    public async Task Scheduled_tasks_treat_disable_schedule_flag_case_insensitively()
    {
        _server.WithSearch("objectClass=edsScheduledTask", [
            Obj(("name", "Active Task"), ("distinguishedName", "CN=Active Task,CN=Scheduled Tasks")),
            Obj(("name", "Paused Task"), ("distinguishedName", "CN=Paused Task,CN=Scheduled Tasks"), ("edsaDisableSchedule", "TRUE"))
        ]);
        using var client = await SignedIn();

        var json = await GetJson(client, "/api/DrillDown/scheduledtasks");

        Assert.True(ItemNamed(json, "name", "Active Task").GetProperty("isEnabled").GetBoolean());
        Assert.False(ItemNamed(json, "name", "Paused Task").GetProperty("isEnabled").GetBoolean());
    }

    [Fact]
    public async Task Virtual_attributes_require_both_predefined_and_system_flags_to_be_built_in()
    {
        _server.WithSearch("objectClass=edsVirtualAttribute", [
            Obj(("name", "Builtin VA"), ("lDAPDisplayName", "edsvaBuiltin"), ("isSingleValued", "true"),
                ("edsaIsPredefined", "TRUE"), ("edsaSystemObject", "TRUE")),
            Obj(("name", "Custom VA"), ("lDAPDisplayName", "edsvaCustom"), ("isSingleValued", "false"),
                ("edsaIsPredefined", "TRUE"), ("edsaSystemObject", "FALSE"))
        ]);
        using var client = await SignedIn();

        var json = await GetJson(client, "/api/DrillDown/virtualattrs");

        var builtin = ItemNamed(json, "name", "Builtin VA");
        Assert.True(builtin.GetProperty("isBuiltIn").GetBoolean());
        Assert.False(builtin.GetProperty("isMultivalued").GetBoolean());
        var custom = ItemNamed(json, "name", "Custom VA");
        Assert.False(custom.GetProperty("isBuiltIn").GetBoolean());
        Assert.True(custom.GetProperty("isMultivalued").GetBoolean());
        Assert.Equal("edsvaCustom", custom.GetProperty("ldapDisplayName").GetString());
    }

    [Theory]
    [InlineData("dynamicgroups", "objectClass=edsDynamicGroup")]
    [InlineData("managedunits", "objectClass=edsManagedUnit")]
    [InlineData("policies", "objectClass=edsPolicyObject")]
    [InlineData("scriptmodules", "objectClass=edsScriptModule")]
    [InlineData("workflows", "objectClass=edsWorkflowDefinition")]
    [InlineData("servers", "objectClass=edsARService")]
    public async Task Backend_failure_is_reported_as_error_with_no_items(string route, string filter)
    {
        _server.WithSearchError(filter, 500);
        using var client = await SignedIn();

        var json = await GetJson(client, $"/api/DrillDown/{route}");

        Assert.StartsWith("No data", json.GetProperty("error").GetString());
        Assert.Equal(0, json.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Children_of_unknown_object_reports_no_data()
    {
        using var client = await SignedIn();

        var json = await GetJson(client, "/api/DrillDown/children/33333333-0000-0000-0000-000000000099");

        Assert.Equal("No data", json.GetProperty("error").GetString());
    }

    public void Dispose()
    {
        _app?.Dispose();
        _server.Dispose();
    }
}
