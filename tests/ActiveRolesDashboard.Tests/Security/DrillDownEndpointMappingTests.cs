using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

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

    [Fact]
    public async Task Children_of_known_object_are_passed_through()
    {
        const string guid = "33333333-0000-0000-0000-000000000001";
        _server.Server.Given(Request.Create().WithPath($"/api/children/{guid}").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""{"items":[{"name":"Child A"},{"name":"Child B"}]}"""));
        using var client = await SignedIn();

        var json = await GetJson(client, $"/api/DrillDown/children/{guid}");

        var names = json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).ToList();
        Assert.Equal(["Child A", "Child B"], names);
    }

    [Fact]
    public async Task Dynamic_groups_count_rules_flag_expensive_groups_and_group_by_service()
    {
        var expensiveRules = string.Concat(Enumerable.Repeat("[rule]", 11));
        _server.WithSearch("objectClass=edsDynamicGroup", [
            Obj(("name", "DG Cheap"), ("edsaDGConditionsList", "[a][b]"), ("edsvaDGControllingService", "ARS01")),
            Obj(("name", "DG Expensive"), ("edsaDGConditionsList", expensiveRules), ("edsvaDGControllingService", "ARS01")),
            Obj(("name", "DG Unassigned"))
        ]);
        using var client = await SignedIn();

        var json = await GetJson(client, "/api/DrillDown/dynamicgroups");

        Assert.Equal(3, json.GetProperty("totalCount").GetInt32());
        var cheap = ItemNamed(json, "name", "DG Cheap");
        Assert.Equal(2, cheap.GetProperty("ruleCount").GetInt32());
        Assert.False(cheap.GetProperty("isExpensive").GetBoolean());
        var expensive = ItemNamed(json, "name", "DG Expensive");
        Assert.Equal(11, expensive.GetProperty("ruleCount").GetInt32());
        Assert.True(expensive.GetProperty("isExpensive").GetBoolean());
        Assert.Equal(0, ItemNamed(json, "name", "DG Unassigned").GetProperty("ruleCount").GetInt32());

        var distribution = json.GetProperty("serviceDistribution");
        Assert.Equal(2, distribution.GetProperty("ARS01").GetInt32());
        Assert.Equal(1, distribution.GetProperty("(unassigned)").GetInt32());
    }

    [Fact]
    public async Task Managed_units_map_dn_and_rule_count()
    {
        _server.WithSearch("objectClass=edsManagedUnit", [
            Obj(("name", "MU Sales"), ("distinguishedName", "CN=MU Sales,CN=Managed Units,CN=Configuration"), ("edsaMUConditionsList", "[x][y][z]")),
            Obj(("name", "MU Empty"))
        ]);
        using var client = await SignedIn();

        var json = await GetJson(client, "/api/DrillDown/managedunits");

        Assert.Equal(2, json.GetProperty("totalCount").GetInt32());
        var sales = ItemNamed(json, "name", "MU Sales");
        Assert.Equal("CN=MU Sales,CN=Managed Units,CN=Configuration", sales.GetProperty("dn").GetString());
        Assert.Equal(3, sales.GetProperty("ruleCount").GetInt32());
        Assert.Equal(0, ItemNamed(json, "name", "MU Empty").GetProperty("ruleCount").GetInt32());
    }

    [Fact]
    public async Task Policies_count_ape_rules_and_require_both_flags_to_be_built_in()
    {
        _server.WithSearch("objectClass=edsPolicyObject", [
            Obj(("name", "Built-in Policy"), ("edsaIsPredefined", "TRUE"), ("edsaSystemObject", "TRUE"),
                ("edsaAPEListXML", "<APEList><APE/><APE/></APEList>")),
            Obj(("name", "Predefined Only"), ("edsaIsPredefined", "TRUE"), ("edsaSystemObject", "FALSE")),
            Obj(("name", "Malformed XML"), ("edsaAPEListXML", "<APEList><APE>"))
        ]);
        using var client = await SignedIn();

        var json = await GetJson(client, "/api/DrillDown/policies");

        Assert.Equal(3, json.GetProperty("totalCount").GetInt32());
        var builtIn = ItemNamed(json, "name", "Built-in Policy");
        Assert.True(builtIn.GetProperty("isBuiltIn").GetBoolean());
        Assert.Equal(2, builtIn.GetProperty("ruleCount").GetInt32());
        Assert.False(ItemNamed(json, "name", "Predefined Only").GetProperty("isBuiltIn").GetBoolean());
        Assert.Equal(0, ItemNamed(json, "name", "Malformed XML").GetProperty("ruleCount").GetInt32());
    }

    [Fact]
    public async Task Script_modules_map_type_system_flag_and_language_breakdown()
    {
        _server.WithSearch("objectClass=edsScriptModule", [
            Obj(("name", "Policy Script"), ("edsaScriptLanguage", "PowerShell"), ("edsaScriptType", "0"),
                ("edsaSystemObject", "TRUE"), ("edsaIsPredefined", "TRUE")),
            Obj(("name", "Task Script"), ("edsaScriptLanguage", "PowerShell"), ("edsaScriptType", "1")),
            Obj(("name", "Library Script"), ("edsaScriptLanguage", "VBScript"), ("edsaScriptType", "2")),
            Obj(("name", "Odd Script"), ("edsaScriptType", "7"))
        ]);
        using var client = await SignedIn();

        var json = await GetJson(client, "/api/DrillDown/scriptmodules");

        Assert.Equal(4, json.GetProperty("totalCount").GetInt32());
        var policy = ItemNamed(json, "name", "Policy Script");
        Assert.Equal("PolicyScript", policy.GetProperty("type").GetString());
        Assert.True(policy.GetProperty("isSystem").GetBoolean());
        Assert.Equal("ScheduledTaskScript", ItemNamed(json, "name", "Task Script").GetProperty("type").GetString());
        Assert.False(ItemNamed(json, "name", "Task Script").GetProperty("isSystem").GetBoolean());
        Assert.Equal("LibraryScript", ItemNamed(json, "name", "Library Script").GetProperty("type").GetString());
        Assert.Equal("7", ItemNamed(json, "name", "Odd Script").GetProperty("type").GetString());

        var breakdown = json.GetProperty("languageBreakdown");
        Assert.Equal(2, breakdown.GetProperty("PowerShell").GetInt32());
        Assert.Equal(1, breakdown.GetProperty("VBScript").GetInt32());
        Assert.Equal(1, breakdown.GetProperty("Unknown").GetInt32());
    }

    // ---------------------------------------------------------------- analysis endpoints

    private static List<string?> Names(JsonElement summary) =>
        summary.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).OrderBy(n => n).ToList();

    [Fact]
    public async Task Policy_objects_without_rules_exclude_policies_with_ape_entries()
    {
        _server.WithSearch("objectClass=edsPolicyObject", [
            Obj(("name", "With Rules"), ("edsaAPEListXML", "<APEList><APE/></APEList>")),
            Obj(("name", "Empty List"), ("edsaAPEListXML", "<APEList/>")),
            Obj(("name", "No Attribute"))
        ]);
        using var client = await SignedIn();

        var json = await GetJson(client, "/api/DrillDown/policyobjectsnorules");

        Assert.Equal(2, json.GetProperty("totalCount").GetInt32());
        Assert.Equal(["Empty List", "No Attribute"], Names(json));
    }

    [Fact]
    public async Task Unlinked_access_templates_ignore_braces_and_case_when_matching_links()
    {
        _server.WithSearch("edsaIsPredefined=FALSE", [
            Obj(("name", "Linked"), ("objectGUID", "AAAAAAAA-0000-0000-0000-000000000001")),
            Obj(("name", "Unlinked"), ("objectGUID", "aaaaaaaa-0000-0000-0000-000000000002"))
        ], priority: 30);
        _server.WithSearch("objectClass=edsACE", [
            Obj(("edsaAccessTemplateGUID", "{aaaaaaaa-0000-0000-0000-000000000001}"))
        ]);
        using var client = await SignedIn();

        var json = await GetJson(client, "/api/DrillDown/unlinkedaccesstemplates");

        Assert.Equal(1, json.GetProperty("totalCount").GetInt32());
        Assert.Equal(["Unlinked"], Names(json));
    }

    [Fact]
    public async Task Deny_access_templates_detect_deny_and_object_deny_aces()
    {
        _server.WithSearch("objectClass=edsAccessTemplate", [
            Obj(("name", "Allow Only"), ("edsaATEList", new JsonArray("[A;;RP;;;][OA;;WP;;;]"))),
            Obj(("name", "Deny"), ("edsaATEList", new JsonArray("[A;;RP;;;]", "[D;;WP;;;]"))),
            Obj(("name", "Object Deny"), ("edsaATEList", new JsonArray("[od;;RP;;;]"))),
            Obj(("name", "No ACEs"))
        ]);
        using var client = await SignedIn();

        var json = await GetJson(client, "/api/DrillDown/denyaccesstemplates");

        Assert.Equal(2, json.GetProperty("totalCount").GetInt32());
        Assert.Equal(["Deny", "Object Deny"], Names(json));
    }

    [Fact]
    public async Task Unlinked_policy_objects_exclude_policies_referenced_by_links()
    {
        _server.WithSearch("objectClass=edsPolicyObject)(!(name=Built-in", [
            Obj(("name", "Linked"), ("objectGuid", "bbbbbbbb-0000-0000-0000-000000000001")),
            Obj(("name", "Unlinked"), ("objectGuid", "bbbbbbbb-0000-0000-0000-000000000002"))
        ], priority: 30);
        _server.WithSearch("objectClass=edsPolicyObjectLink", [
            Obj(("edsaAPOGUID", "{BBBBBBBB-0000-0000-0000-000000000001}"))
        ]);
        using var client = await SignedIn();

        var json = await GetJson(client, "/api/DrillDown/unlinkedpolicyobjects");

        Assert.Equal(1, json.GetProperty("totalCount").GetInt32());
        Assert.Equal(["Unlinked"], Names(json));
    }

    [Fact]
    public async Task Orphan_access_template_links_list_missing_reference_attributes()
    {
        _server.WithSearch("objectClass=edsACE", [
            Obj(("name", "Complete"), ("edsaAccessTemplateGUID", "g1"), ("edsaSecObjectGUID", "g2"), ("edsaTrusteeSID", "S-1-5-21-1")),
            Obj(("name", "No Trustee"), ("edsaAccessTemplateGUID", "g1"), ("edsaSecObjectGUID", "g2")),
            Obj(("name", "Bare"))
        ]);
        using var client = await SignedIn();

        var json = await GetJson(client, "/api/DrillDown/orphanaccesstemplatelinks");

        Assert.Equal(2, json.GetProperty("totalCount").GetInt32());
        Assert.Equal("edsaTrusteeSID", ItemNamed(json, "name", "No Trustee").GetProperty("missingAttributes").GetString());
        Assert.Equal("edsaAccessTemplateGUID, edsaSecObjectGUID, edsaTrusteeSID",
            ItemNamed(json, "name", "Bare").GetProperty("missingAttributes").GetString());
    }

    [Fact]
    public async Task Orphan_policy_object_links_list_missing_reference_attributes()
    {
        _server.WithSearch("objectClass=edsPolicyObjectLink", [
            Obj(("name", "Complete"), ("edsaAPOGUID", "g1"), ("edsaSecObjectGUID", "g2")),
            Obj(("name", "No Target"), ("edsaAPOGUID", "g1"))
        ]);
        using var client = await SignedIn();

        var json = await GetJson(client, "/api/DrillDown/orphanpolicyobjectlinks");

        Assert.Equal(1, json.GetProperty("totalCount").GetInt32());
        Assert.Equal("edsaSecObjectGUID", ItemNamed(json, "name", "No Target").GetProperty("missingAttributes").GetString());
    }

    private const string ExistingGuid = "cccccccc-0000-0000-0000-000000000001";
    private const string MissingGuid = "cccccccc-0000-0000-0000-000000000099";

    [Theory]
    [InlineData("dynamicgroupsbrokenrules", "objectClass=edsDynamicGroup", "edsaDGConditionsList")]
    [InlineData("managedunitsbrokenrules", "objectClass=edsManagedUnit", "edsaMUConditionsList")]
    public async Task Broken_rules_flag_only_objects_referencing_unresolvable_guids(string route, string filter, string conditionsAttribute)
    {
        _server.WithObject(ExistingGuid, Obj(("name", "Resolves")));
        _server.WithSearch(filter, [
            Obj(("name", "Healthy"), (conditionsAttribute, $"[memberOf={{{ExistingGuid}}}]")),
            Obj(("name", "Broken"), (conditionsAttribute, $"[memberOf={ExistingGuid}][memberOf={{{MissingGuid}}}]")),
            Obj(("name", "No GUIDs"), (conditionsAttribute, "[department=IT]")),
            Obj(("name", "No Conditions"))
        ]);
        using var client = await SignedIn();

        var json = await GetJson(client, $"/api/DrillDown/{route}");

        Assert.Equal(1, json.GetProperty("totalCount").GetInt32());
        Assert.Equal(["Broken"], Names(json));
    }

    public void Dispose()
    {
        _app?.Dispose();
        _server.Dispose();
    }
}
