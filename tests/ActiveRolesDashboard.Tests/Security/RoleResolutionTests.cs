using System.Text.Json;
using System.Text.Json.Nodes;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActiveRolesDashboard.Tests.Security;

/// <summary>
/// Role and directory-facts resolution: role-group precedence, direct vs nested membership, DN
/// lookup fallbacks, fail-closed behavior on directory errors, the role -> permission matrix, and
/// the epoch-based facts cache including fresh re-evaluation at login.
/// </summary>
[Trait("Stage", "Security")]
public class RoleResolutionTests : IDisposable
{
    private const string UserDn = "CN=Admin,OU=Users,DC=test,DC=local";
    private const string Username = FakeActiveRolesServer.ValidUser;

    private readonly FakeActiveRolesServer _server = new FakeActiveRolesServer().WithHealthyDefaults();
    private readonly RolePermissionProtector _protector = new(new EphemeralDataProtectionProvider());
    private readonly ActiveRolesConfig _config;
    private readonly PerUserSummaryCache _cache = new(new MemoryCache(new MemoryCacheOptions()));

    public RoleResolutionTests()
    {
        _config = new ActiveRolesConfig { ApiBaseUrl = _server.ApiBaseUrl, RstsUrl = _server.RstsUrl };
        _config.RoleGroups.ActiveRolesAdmins = "AR Admins";
        _config.RoleGroups.DashboardAdmins = "Dash Admins";
        _config.RoleGroups.Auditors = "Auditors";
        _config.RoleGroups.PowerUsers = "Power Users";
    }

    private RoleService CreateRoleService()
    {
        var options = new TestOptionsMonitor<ActiveRolesConfig>(_config);
        var factory = new TestHttpClientFactory(TimeSpan.FromSeconds(5));
        var tokens = new ServiceAccountTokenProvider(
            new RstsAuthService(options, factory),
            new ServiceAccountSecretProtector(new EphemeralDataProtectionProvider()),
            options,
            NullLogger<ServiceAccountTokenProvider>.Instance);
        var ar = new ActiveRolesService(factory, options, NullLogger<ActiveRolesService>.Instance, tokens);
        return new RoleService(ar, options, _protector, tokens, NullLogger<RoleService>.Instance);
    }

    private DirectoryFactsResolver CreateResolver() =>
        new(CreateRoleService(), _cache, NullLogger<DirectoryFactsResolver>.Instance);

    private RoleResolutionTests WithUser(string filterContains = "samAccountName=admin", string dn = UserDn)
    {
        _server.WithSearch(filterContains, [new JsonObject { ["sAMAccountName"] = "admin", ["distinguishedName"] = dn }], priority: 20);
        return this;
    }

    private RoleResolutionTests WithGroup(string name, string? direct = null, string? indirect = null)
    {
        var group = new JsonObject { ["distinguishedName"] = $"CN={name},OU=Groups,DC=test,DC=local" };
        if (direct != null) group["edsaMember"] = new JsonArray(direct);
        if (indirect != null) group["edsaMemberIndirect"] = new JsonArray(indirect);
        _server.WithSearch($"name={name})", [group], priority: 20);
        return this;
    }

    private Task<(bool IsActiveRolesAdmin, DashboardRole Role)> Resolve() =>
        CreateRoleService().ResolveLoginFactsAsync(FakeActiveRolesServer.ValidToken, Username);

    // ---------------------------------------------------------------- role evaluation

    [Fact]
    public async Task Unknown_user_falls_back_to_least_privileged_role()
    {
        WithGroup("Auditors", direct: UserDn);

        Assert.Equal((false, DashboardRole.User), await Resolve());
    }

    [Fact]
    public async Task User_in_no_role_group_gets_User_role()
    {
        WithUser().WithGroup("Auditors", direct: "CN=Someone Else,DC=test,DC=local");

        Assert.Equal((false, DashboardRole.User), await Resolve());
    }

    [Theory]
    [InlineData("Dash Admins", DashboardRole.DashboardAdministrator)]
    [InlineData("Auditors", DashboardRole.Auditor)]
    [InlineData("Power Users", DashboardRole.PowerUser)]
    public async Task Direct_member_of_role_group_gets_that_role(string group, DashboardRole expected)
    {
        WithUser().WithGroup(group, direct: UserDn);

        Assert.Equal((false, expected), await Resolve());
    }

    [Fact]
    public async Task Nested_member_of_role_group_gets_that_role()
    {
        WithUser().WithGroup("Auditors", indirect: UserDn);

        Assert.Equal((false, DashboardRole.Auditor), await Resolve());
    }

    [Fact]
    public async Task Active_Roles_admin_is_dashboard_administrator_and_flagged_admin()
    {
        WithUser().WithGroup("AR Admins", indirect: UserDn).WithGroup("Power Users", direct: UserDn);

        Assert.Equal((true, DashboardRole.DashboardAdministrator), await Resolve());
    }

    [Fact]
    public async Task Highest_privilege_role_wins_when_in_several_groups()
    {
        WithUser().WithGroup("Power Users", direct: UserDn).WithGroup("Auditors", direct: UserDn);

        Assert.Equal((false, DashboardRole.Auditor), await Resolve());
    }

    [Fact]
    public async Task Member_dn_matching_is_case_insensitive()
    {
        WithUser().WithGroup("Power Users", direct: UserDn.ToUpperInvariant());

        Assert.Equal((false, DashboardRole.PowerUser), await Resolve());
    }

    [Fact]
    public async Task Similar_but_different_dn_does_not_grant_role()
    {
        WithUser().WithGroup("Auditors", direct: "CN=Admin2,OU=Users,DC=test,DC=local");

        Assert.Equal((false, DashboardRole.User), await Resolve());
    }

    [Fact]
    public async Task Case_insensitive_anr_fallback_resolves_user()
    {
        WithUser(filterContains: "anr=admin").WithGroup("Auditors", direct: UserDn);

        Assert.Equal((false, DashboardRole.Auditor), await Resolve());
    }

    [Fact]
    public async Task Anr_hit_for_a_different_account_is_rejected()
    {
        _server.WithSearch("anr=admin", [new JsonObject { ["sAMAccountName"] = "administrator", ["distinguishedName"] = UserDn }], priority: 20);
        WithGroup("Auditors", direct: UserDn);

        Assert.Equal((false, DashboardRole.User), await Resolve());
    }

    [Fact]
    public async Task Role_group_lookup_failure_fails_closed()
    {
        WithUser();
        _server.WithSearchError("name=AR Admins)", 500);
        _server.WithSearchError("name=Dash Admins)", 500);
        _server.WithSearchRawBody("name=Auditors)", "{not json");

        Assert.Equal((false, DashboardRole.User), await Resolve());
    }

    [Fact]
    public async Task Unconfigured_role_groups_grant_nothing()
    {
        _config.RoleGroups.ActiveRolesAdmins = "";
        _config.RoleGroups.DashboardAdmins = " ";
        WithUser();
        // A wildcard group search would otherwise return membership for any filter.
        _server.WithSearch("objectClass=group", [new JsonObject { ["edsaMember"] = new JsonArray(UserDn) }], priority: 30);
        _server.WithSearch("name=Auditors)", [], priority: 20);
        _server.WithSearch("name=Power Users)", [], priority: 20);

        Assert.Equal((false, DashboardRole.User), await Resolve());
    }

    [Fact]
    public async Task Role_group_name_is_ldap_escaped()
    {
        _config.RoleGroups.Auditors = "Audit*)(name=x";
        WithUser();

        await Resolve();

        var filters = _server.Requests.Select(r => Uri.UnescapeDataString(r.RequestMessage.Url)).ToList();
        Assert.DoesNotContain(filters, f => f.Contains("Audit*)(name=x"));
        Assert.Contains(filters, f => f.Contains(@"Audit\2a\29\28name=x"));
    }

    // ---------------------------------------------------------------- permission matrix

    [Fact]
    public void Default_matrix_matches_registry()
    {
        var matrix = CreateRoleService().GetMatrix();

        foreach (var role in RolePermissionRegistry.AllRoles)
            Assert.Equal(RolePermissionRegistry.GetDefaultPermissions(role).OrderBy(p => p), matrix[role].OrderBy(p => p));
    }

    [Fact]
    public void Dashboard_administrator_has_everything_except_delegated_visibility()
    {
        var perms = CreateRoleService().GetPermissions(DashboardRole.DashboardAdministrator);

        Assert.DoesNotContain(DashboardPermission.UseDelegatedPermissionsForVisibility, perms);
        Assert.Equal(RolePermissionRegistry.AllPermissions.Count - 1, perms.Count);
    }

    [Theory]
    [InlineData(DashboardRole.PowerUser)]
    [InlineData(DashboardRole.User)]
    public void Non_privileged_roles_default_to_delegated_visibility_and_no_admin_operations(DashboardRole role)
    {
        var perms = CreateRoleService().GetPermissions(role);

        Assert.Contains(DashboardPermission.UseDelegatedPermissionsForVisibility, perms);
        Assert.DoesNotContain(DashboardPermission.ManageSystemSettings, perms);
        Assert.DoesNotContain(DashboardPermission.RebuildCache, perms);
        Assert.DoesNotContain(DashboardPermission.DeleteAssessments, perms);
    }

    [Fact]
    public void Persisted_matrix_overrides_defaults_and_ignores_unknown_entries()
    {
        _config.Roles.ProtectedMatrix = _protector.Protect(JsonSerializer.Serialize(new Dictionary<string, List<string>>
        {
            ["User"] = ["ExportDashboardData", "NotAPermission"],
            ["NotARole"] = ["ManageSystemSettings"],
            ["DashboardAdministrator"] = []
        }));

        var svc = CreateRoleService();

        Assert.Equal([DashboardPermission.ExportDashboardData], svc.GetPermissions(DashboardRole.User));
        Assert.Equal(RolePermissionRegistry.DashboardAdministratorPermissions.Count, svc.GetPermissions(DashboardRole.DashboardAdministrator).Count);
        Assert.Equal(RolePermissionRegistry.GetDefaultPermissions(DashboardRole.Auditor).Count, svc.GetPermissions(DashboardRole.Auditor).Count);
    }

    [Theory]
    [InlineData("not-a-protected-payload")]
    [InlineData("CfDJ8AAAAAAAAAAAAAAAAAAAAAA")]
    public void Tampered_matrix_falls_back_to_defaults(string payload)
    {
        _config.Roles.ProtectedMatrix = payload;

        var perms = CreateRoleService().GetPermissions(DashboardRole.User);

        Assert.Equal(RolePermissionRegistry.GetDefaultPermissions(DashboardRole.User).OrderBy(p => p), perms.OrderBy(p => p));
    }

    [Fact]
    public void Matrix_from_a_different_protector_is_rejected()
    {
        var foreign = new RolePermissionProtector(new EphemeralDataProtectionProvider());
        _config.Roles.ProtectedMatrix = foreign.Protect(JsonSerializer.Serialize(new Dictionary<string, List<string>>
        {
            ["User"] = ["ManageSystemSettings"]
        }));

        Assert.DoesNotContain(DashboardPermission.ManageSystemSettings, CreateRoleService().GetPermissions(DashboardRole.User));
    }

    [Fact]
    public void ProtectMatrix_round_trips_and_never_stores_administrator_row()
    {
        var svc = CreateRoleService();
        var edited = svc.GetMatrix().ToDictionary(k => k.Key, v => v.Value);
        edited[DashboardRole.PowerUser] = new HashSet<DashboardPermission> { DashboardPermission.ViewSnapshots };
        edited[DashboardRole.DashboardAdministrator] = new HashSet<DashboardPermission>();

        var payload = svc.ProtectMatrix(edited);
        Assert.DoesNotContain("DashboardAdministrator", _protector.Unprotect(payload));

        _config.Roles.ProtectedMatrix = payload;
        var reloaded = CreateRoleService();
        Assert.Equal([DashboardPermission.ViewSnapshots], reloaded.GetPermissions(DashboardRole.PowerUser));
        Assert.NotEmpty(reloaded.GetPermissions(DashboardRole.DashboardAdministrator));
    }

    // ---------------------------------------------------------------- assessment update permission

    [Theory]
    [InlineData(DashboardRole.DashboardAdministrator, true)]
    [InlineData(DashboardRole.Auditor, true)]
    [InlineData(DashboardRole.PowerUser, false)]
    [InlineData(DashboardRole.User, false)]
    public void ManageAssessmentUpdates_default_grants(DashboardRole role, bool expected)
    {
        Assert.Equal(expected, CreateRoleService().GetPermissions(role).Contains(DashboardPermission.ManageAssessmentUpdates));
    }

    [Fact]
    public void Legacy_persisted_matrix_receives_new_auditor_default()
    {
        _config.Roles.ProtectedMatrix = _protector.Protect(JsonSerializer.Serialize(new Dictionary<string, List<string>>
        {
            ["Auditor"] = ["ViewAssessments"]
        }));

        var perms = CreateRoleService().GetPermissions(DashboardRole.Auditor);

        Assert.Contains(DashboardPermission.ViewAssessments, perms);
        Assert.Contains(DashboardPermission.ManageAssessmentUpdates, perms);
    }

    [Fact]
    public void Revoked_update_permission_stays_revoked_after_save()
    {
        var svc = CreateRoleService();
        var edited = svc.GetMatrix().ToDictionary(k => k.Key, v => v.Value);
        edited[DashboardRole.Auditor] = edited[DashboardRole.Auditor].Where(p => p != DashboardPermission.ManageAssessmentUpdates).ToHashSet();

        _config.Roles.ProtectedMatrix = svc.ProtectMatrix(edited);

        Assert.DoesNotContain(DashboardPermission.ManageAssessmentUpdates, CreateRoleService().GetPermissions(DashboardRole.Auditor));
    }

    // ---------------------------------------------------------------- directory facts cache

    private int RoleGroupQueries() => _server.Requests.Count(r =>
        (Uri.UnescapeDataString(r.RequestMessage.Url)).Contains("objectClass=group"));

    [Fact]
    public async Task Facts_are_cached_until_the_epoch_advances()
    {
        WithUser().WithGroup("Auditors", direct: UserDn);
        var resolver = CreateResolver();

        var first = await resolver.ResolveAsync(FakeActiveRolesServer.ValidToken, Username);
        var queries = RoleGroupQueries();
        var second = await resolver.ResolveAsync(FakeActiveRolesServer.ValidToken, Username);

        Assert.Equal(DashboardRole.Auditor, first.Role);
        Assert.Equal(first, second);
        Assert.Equal(queries, RoleGroupQueries());

        _cache.InvalidateDirectoryFacts();
        var third = await resolver.ResolveAsync(FakeActiveRolesServer.ValidToken, Username);

        Assert.True(RoleGroupQueries() > queries);
        Assert.Equal(first.Epoch + 1, third.Epoch);
    }

    [Fact]
    public async Task Fresh_resolution_detects_role_change()
    {
        WithUser().WithGroup("Auditors", direct: UserDn);
        var resolver = CreateResolver();
        await resolver.ResolveAsync(FakeActiveRolesServer.ValidToken, Username);

        // Removed from Auditors between logins.
        _server.WithSearch("name=Auditors)", [new JsonObject { ["edsaMember"] = new JsonArray() }], priority: 5);
        var fresh = await resolver.ResolveFreshAsync(FakeActiveRolesServer.ValidToken, Username);

        Assert.True(fresh.Changed);
        Assert.Equal(DashboardRole.Auditor, fresh.PreviousRole);
        Assert.Equal(DashboardRole.User, fresh.Facts.Role);
        Assert.Equal(nameof(DashboardRole.User), _cache.GetRole(Username));
    }

    [Fact]
    public async Task Fresh_resolution_reports_unchanged_when_role_is_stable()
    {
        WithUser().WithGroup("Power Users", direct: UserDn);
        var resolver = CreateResolver();
        await resolver.ResolveAsync(FakeActiveRolesServer.ValidToken, Username);

        var fresh = await resolver.ResolveFreshAsync(FakeActiveRolesServer.ValidToken, Username);

        Assert.False(fresh.Changed);
        Assert.Equal(DashboardRole.PowerUser, fresh.Facts.Role);
    }

    [Fact]
    public async Task First_ever_fresh_resolution_has_no_previous_role()
    {
        WithUser().WithGroup("Auditors", direct: UserDn);

        var fresh = await CreateResolver().ResolveFreshAsync(FakeActiveRolesServer.ValidToken, Username);

        Assert.Null(fresh.PreviousRole);
        Assert.True(fresh.Changed);
        Assert.Equal(DashboardRole.Auditor, fresh.Facts.Role);
    }

    [Fact]
    public async Task Cache_entries_are_isolated_per_user()
    {
        _cache.SetRole(@"TEST\other", nameof(DashboardRole.DashboardAdministrator));
        _cache.SetAdmin(@"TEST\other", true);
        WithUser();

        var facts = await CreateResolver().ResolveAsync(FakeActiveRolesServer.ValidToken, Username);

        Assert.Equal(DashboardRole.User, facts.Role);
        Assert.False(facts.IsActiveRolesAdmin);
    }

    [Fact]
    public async Task Corrupt_cached_role_is_re_evaluated()
    {
        _cache.SetAdmin(Username, false);
        _cache.SetRole(Username, "SuperUser");
        WithUser().WithGroup("Power Users", direct: UserDn);

        var facts = await CreateResolver().ResolveAsync(FakeActiveRolesServer.ValidToken, Username);

        Assert.Equal(DashboardRole.PowerUser, facts.Role);
    }

    private void FailUserLookup() =>
        _server.WithSearchError("(objectClass=user)(samAccountName=admin)", 503, priority: 1);

    [Fact]
    public async Task Fresh_resolution_always_queries_the_directory()
    {
        WithUser().WithGroup("Auditors", direct: UserDn);
        var resolver = CreateResolver();
        await resolver.ResolveAsync(FakeActiveRolesServer.ValidToken, Username);
        var queries = RoleGroupQueries();

        await resolver.ResolveFreshAsync(FakeActiveRolesServer.ValidToken, Username);

        Assert.True(RoleGroupQueries() > queries);
    }

    [Fact]
    public async Task Fresh_resolution_detects_admin_flag_change_with_same_role()
    {
        WithUser().WithGroup("Dash Admins", direct: UserDn);
        var resolver = CreateResolver();
        var before = await resolver.ResolveAsync(FakeActiveRolesServer.ValidToken, Username);

        // Added to the Active Roles admins group between logins; role stays DashboardAdministrator.
        WithGroup("AR Admins", direct: UserDn);
        var fresh = await resolver.ResolveFreshAsync(FakeActiveRolesServer.ValidToken, Username);

        Assert.False(before.IsActiveRolesAdmin);
        Assert.Equal(DashboardRole.DashboardAdministrator, fresh.PreviousRole);
        Assert.Equal(DashboardRole.DashboardAdministrator, fresh.Facts.Role);
        Assert.True(fresh.Facts.IsActiveRolesAdmin);
        Assert.True(fresh.Changed);
        Assert.True(_cache.GetAdmin(Username));
    }

    [Fact]
    public async Task Fresh_resolution_retains_last_known_facts_when_directory_fails()
    {
        WithUser().WithGroup("Auditors", direct: UserDn);
        var resolver = CreateResolver();
        var known = await resolver.ResolveAsync(FakeActiveRolesServer.ValidToken, Username);

        FailUserLookup();
        var fresh = await resolver.ResolveFreshAsync(FakeActiveRolesServer.ValidToken, Username);

        Assert.False(fresh.Changed);
        Assert.Equal(DashboardRole.Auditor, fresh.PreviousRole);
        Assert.Equal(known, fresh.Facts);
        Assert.Equal(nameof(DashboardRole.Auditor), _cache.GetRole(Username));
        Assert.False(_cache.GetAdmin(Username));
    }

    [Fact]
    public async Task Fresh_resolution_does_not_downgrade_an_admin_during_an_outage()
    {
        _cache.SetAdmin(Username, true);
        _cache.SetRole(Username, nameof(DashboardRole.DashboardAdministrator));

        FailUserLookup();
        var fresh = await CreateResolver().ResolveFreshAsync(FakeActiveRolesServer.ValidToken, Username);

        Assert.True(fresh.Facts.IsActiveRolesAdmin);
        Assert.Equal(DashboardRole.DashboardAdministrator, fresh.Facts.Role);
        Assert.False(fresh.Changed);
    }

    [Fact]
    public async Task Fresh_resolution_failure_without_cached_facts_propagates()
    {
        FailUserLookup();

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            CreateResolver().ResolveFreshAsync(FakeActiveRolesServer.ValidToken, Username));

        Assert.Null(_cache.GetRole(Username));
        Assert.Null(_cache.GetAdmin(Username));
    }

    [Fact]
    public async Task Fresh_resolution_after_epoch_advance_treats_old_facts_as_absent()
    {
        WithUser().WithGroup("Auditors", direct: UserDn);
        var resolver = CreateResolver();
        await resolver.ResolveAsync(FakeActiveRolesServer.ValidToken, Username);
        _cache.InvalidateDirectoryFacts();

        var fresh = await resolver.ResolveFreshAsync(FakeActiveRolesServer.ValidToken, Username);

        Assert.Null(fresh.PreviousRole);
        Assert.True(fresh.Changed);
        Assert.Equal(_cache.DirectoryFactsEpoch, fresh.Facts.Epoch);
    }

    [Fact]
    public async Task Stale_facts_are_not_retained_when_directory_fails_after_epoch_advance()
    {
        _cache.SetAdmin(Username, true);
        _cache.SetRole(Username, nameof(DashboardRole.DashboardAdministrator));
        _cache.InvalidateDirectoryFacts();

        FailUserLookup();

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            CreateResolver().ResolveFreshAsync(FakeActiveRolesServer.ValidToken, Username));
    }

    [Fact]
    public async Task Cached_resolution_failure_propagates_and_caches_nothing()
    {
        FailUserLookup();

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            CreateResolver().ResolveAsync(FakeActiveRolesServer.ValidToken, Username));

        Assert.Null(_cache.GetRole(Username));
    }

    [Fact]
    public async Task Resolved_facts_are_stored_per_user()
    {
        WithUser().WithGroup("Power Users", direct: UserDn);

        await CreateResolver().ResolveAsync(FakeActiveRolesServer.ValidToken, Username);

        Assert.Equal(nameof(DashboardRole.PowerUser), _cache.GetRole(Username));
        Assert.Null(_cache.GetRole(@"TEST\someone-else"));
    }

    public void Dispose() => _server.Dispose();
}
