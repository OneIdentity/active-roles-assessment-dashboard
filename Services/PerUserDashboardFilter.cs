using System.Text.Json;
using ActiveRolesDashboard.Models;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// Projects the shared, service-account-collected superset <see cref="DashboardSummary"/> down to
/// what a single viewer is permitted to see. Every item list is filtered through the one visibility
/// gate (<see cref="PermissionScope"/>) and every count is recomputed from the visible set, so
/// Overview totals and all derived KPI/category values stay consistent with the viewer's delegated
/// read access. AR admins bypass this entirely (the caller uses the unfiltered superset).
///
/// The superset is never mutated: each filtered summary/list is a new instance. Scalar
/// security-health signals are scoped by domain visibility (the set of domains the viewer can see).
/// </summary>
public sealed class PerUserDashboardFilter
{
    /// <summary>
    /// Returns a new <see cref="DashboardSummary"/> containing only the objects visible to
    /// <paramref name="user"/> under <paramref name="model"/>, with all counts recomputed.
    /// </summary>
    public DashboardSummary Filter(DashboardSummary superset, UserSidSet user, ArPermissionModel model)
    {
        ArgumentNullException.ThrowIfNull(superset);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(model);

        var s = new DashboardSummary();

        // --- Overview raw sources (JsonElement) ------------------------------
        s.ADUserAccounts = FilterRaw(superset.ADUserAccounts, user, model,
            items => new ADUserAccountsSummary { Items = items, TotalCount = items.Count });
        s.ADGroups = FilterRaw(superset.ADGroups, user, model,
            items => new ADGroupsSummary { Items = items, TotalCount = items.Count });
        s.Computers = FilterRaw(superset.Computers, user, model,
            items => new ComputersSummary { Items = items, TotalCount = items.Count });

        // Entra objects are not AR-delegated (they come from Azure configuration the service
        // account reads, not per-object AT Links). A non-admin viewer reaching this filter has no
        // Entra delegation, so Entra must NOT be shown to them; expose an empty Entra summary so
        // EntraVisible resolves false and the Entra tile/toast/badge stay hidden. Admins bypass this
        // filter entirely and keep the full superset.
        s.EntraTotals = new EntraTotalsSummary();

        // --- Typed user-account detail drilldowns ----------------------------
        s.NeverLoggedIn = FilterDetail(superset.NeverLoggedIn, user, model);
        s.ExpiredUsers = FilterDetail(superset.ExpiredUsers, user, model);
        s.PasswordNeverExpires = FilterDetail(superset.PasswordNeverExpires, user, model);
        s.AdminCount = FilterDetail(superset.AdminCount, user, model);
        s.EnabledUsers = FilterDetail(superset.EnabledUsers, user, model);
        s.DisabledUsers = FilterDetail(superset.DisabledUsers, user, model);
        s.MustChangePassword = FilterDetail(superset.MustChangePassword, user, model);
        s.PasswordNotRequired = FilterDetail(superset.PasswordNotRequired, user, model);
        s.SmartCardRequired = FilterDetail(superset.SmartCardRequired, user, model);
        s.CannotChangePassword = FilterDetail(superset.CannotChangePassword, user, model);
        s.NoKerberosPreauth = FilterDetail(superset.NoKerberosPreauth, user, model);
        s.UserReversibleEncryption = FilterDetail(superset.UserReversibleEncryption, user, model);
        s.SensitiveCannotDelegate = FilterDetail(superset.SensitiveCannotDelegate, user, model);
        s.TrustedForDelegation = FilterDetail(superset.TrustedForDelegation, user, model);
        s.UseDesEncryption = FilterDetail(superset.UseDesEncryption, user, model);
        s.DeprovisionedUsers = FilterDetail(superset.DeprovisionedUsers, user, model);
        s.SpnUserAccounts = FilterDetail(superset.SpnUserAccounts, user, model);
        s.StaleUsers = FilterDetail(superset.StaleUsers, user, model);

        // Expiring users (own summary type).
        s.ExpiringUsers = FilterExpiring(superset.ExpiringUsers, user, model);

        // --- Governance KPI drilldowns ---------------------------------------
        s.NoManagerUser = FilterGovernance(superset.NoManagerUser, user, model);
        s.NoManagerServiceAccount = FilterGovernance(superset.NoManagerServiceAccount, user, model);
        s.ServiceAccounts = FilterGovernance(superset.ServiceAccounts, user, model);
        s.GmsaServiceAccounts = FilterGovernance(superset.GmsaServiceAccounts, user, model);
        s.SmsaServiceAccounts = FilterGovernance(superset.SmsaServiceAccounts, user, model);
        s.UserAccountLockedOut = FilterGovernance(superset.UserAccountLockedOut, user, model);
        s.ReversibleEncryption = FilterGovernance(superset.ReversibleEncryption, user, model);
        s.EmptyGroups = FilterGovernance(superset.EmptyGroups, user, model);
        s.CircularGroupNesting = FilterGovernance(superset.CircularGroupNesting, user, model);
        s.Sites = FilterGovernance(superset.Sites, user, model);
        s.SiteLinks = FilterGovernance(superset.SiteLinks, user, model);
        s.Subnets = FilterGovernance(superset.Subnets, user, model);
        s.OUs = FilterGovernance(superset.OUs, user, model);

        // No-group-owner (own summary type).
        s.NoGroupOwner = FilterNoGroupOwner(superset.NoGroupOwner, user, model);

        // --- Privileged group memberships ------------------------------------
        s.AccountOperators = FilterPrivileged(superset.AccountOperators, user, model);
        s.Administrators = FilterPrivileged(superset.Administrators, user, model);
        s.BackupOperators = FilterPrivileged(superset.BackupOperators, user, model);
        s.DomainAdmins = FilterPrivileged(superset.DomainAdmins, user, model);
        s.ServerOperators = FilterPrivileged(superset.ServerOperators, user, model);
        s.EnterpriseAdmins = FilterPrivileged(superset.EnterpriseAdmins, user, model);
        s.SchemaAdmins = FilterPrivileged(superset.SchemaAdmins, user, model);
        s.ActiveRolesAdmins = FilterPrivileged(superset.ActiveRolesAdmins, user, model);

        // --- Group detail breakdowns -----------------------------------------
        s.DistributionGroups = FilterGroupDetail(superset.DistributionGroups, user, model);
        s.DomainLocalGroups = FilterGroupDetail(superset.DomainLocalGroups, user, model);
        s.GlobalGroups = FilterGroupDetail(superset.GlobalGroups, user, model);
        s.MailEnabledSecurityGroups = FilterGroupDetail(superset.MailEnabledSecurityGroups, user, model);
        s.SecurityGroups = FilterGroupDetail(superset.SecurityGroups, user, model);
        s.UniversalGroups = FilterGroupDetail(superset.UniversalGroups, user, model);

        // --- Computer breakdowns ---------------------------------------------
        s.ComputerClients = FilterComputer(superset.ComputerClients, user, model);
        s.ComputerServers = FilterComputer(superset.ComputerServers, user, model);
        s.UnconstrainedComputers = FilterComputer(superset.UnconstrainedComputers, user, model);
        s.WinServer2008R2 = FilterComputer(superset.WinServer2008R2, user, model);
        s.WinServer2012R2 = FilterComputer(superset.WinServer2012R2, user, model);
        s.WinServer2016 = FilterComputer(superset.WinServer2016, user, model);
        s.WinServer2019 = FilterComputer(superset.WinServer2019, user, model);
        s.WinServer2022 = FilterComputer(superset.WinServer2022, user, model);
        s.WinServer2025 = FilterComputer(superset.WinServer2025, user, model);
        s.ServerOther = FilterComputer(superset.ServerOther, user, model);
        s.Win7 = FilterComputer(superset.Win7, user, model);
        s.Win81 = FilterComputer(superset.Win81, user, model);
        s.Win10_22H2 = FilterComputer(superset.Win10_22H2, user, model);
        s.Win11_22H2 = FilterComputer(superset.Win11_22H2, user, model);
        s.Win11_23H2 = FilterComputer(superset.Win11_23H2, user, model);
        s.Win11Enterprise = FilterComputer(superset.Win11Enterprise, user, model);
        s.Win11Pro = FilterComputer(superset.Win11Pro, user, model);
        s.ClientsOther = FilterComputer(superset.ClientsOther, user, model);
        s.StaleComputers = FilterComputer(superset.StaleComputers, user, model);
        s.DomainControllers = FilterDomainControllers(superset.DomainControllers, user, model);

        // --- Domain / server infra objects -----------------------------------
        s.Domains = FilterDomains(superset.Domains, user, model);
        s.Servers = FilterServers(superset.Servers, user, model);

        // --- Scalar security-health signals: scope by visible domains --------
        var visibleDomains = s.Domains.Items
            .Select(d => d.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        s.KrbtgtPasswordAge = ScopeSecurityHealth(superset.KrbtgtPasswordAge, visibleDomains);
        s.WeakPasswordLength = ScopeSecurityHealth(superset.WeakPasswordLength, visibleDomains);
        s.PasswordComplexityDisabled = ScopeSecurityHealth(superset.PasswordComplexityDisabled, visibleDomains);
        s.NoAccountLockout = ScopeSecurityHealth(superset.NoAccountLockout, visibleDomains);
        s.PasswordMaxAgeDays = ScopeSecurityHealth(superset.PasswordMaxAgeDays, visibleDomains);

        return s;
    }

    // ----- family-specific filters (new instance, recomputed count) ----------

    private static TSummary FilterRaw<TSummary>(
        TSummary? source, UserSidSet user, ArPermissionModel model,
        Func<List<JsonElement>, TSummary> build)
        where TSummary : class, new()
    {
        if (source == null) return new TSummary();
        // Preserve error summaries as-is (nothing to filter, count already 0/N-A).
        var error = GetError(source);
        if (error != null) return source;

        var raw = GetRawItems(source);
        var visible = PermissionScope.VisibleItems(raw, user, model);
        return build(visible);
    }

    private static ADUserAccountDetailSummary FilterDetail(ADUserAccountDetailSummary? src, UserSidSet u, ArPermissionModel m)
    {
        if (src == null) return new ADUserAccountDetailSummary();
        if (src.Error != null) return src;
        var items = PermissionScope.VisibleItems(src.Items, u, m);
        return new ADUserAccountDetailSummary { Items = items, TotalCount = items.Count };
    }

    private static ExpiringUsersSummary FilterExpiring(ExpiringUsersSummary? src, UserSidSet u, ArPermissionModel m)
    {
        if (src == null) return new ExpiringUsersSummary();
        if (src.Error != null) return src;
        var items = PermissionScope.VisibleItems(src.Items, u, m);
        return new ExpiringUsersSummary { Items = items, TotalCount = items.Count };
    }

    private static GovernanceKpiSummary FilterGovernance(GovernanceKpiSummary? src, UserSidSet u, ArPermissionModel m)
    {
        if (src == null) return new GovernanceKpiSummary();
        if (src.Error != null) return src;
        var items = PermissionScope.VisibleItems(src.Items, u, m);
        return new GovernanceKpiSummary { Items = items, TotalCount = items.Count };
    }

    private static NoGroupOwnerSummary FilterNoGroupOwner(NoGroupOwnerSummary? src, UserSidSet u, ArPermissionModel m)
    {
        if (src == null) return new NoGroupOwnerSummary();
        if (src.Error != null) return src;
        var items = PermissionScope.VisibleItems(src.Items, u, m);
        return new NoGroupOwnerSummary { Items = items, TotalCount = items.Count };
    }

    private static PrivilegedGroupSummary FilterPrivileged(PrivilegedGroupSummary? src, UserSidSet u, ArPermissionModel m)
    {
        if (src == null) return new PrivilegedGroupSummary();
        if (src.Error != null) return src;
        var items = PermissionScope.VisibleItems(src.Items, u, m);
        return new PrivilegedGroupSummary
        {
            Items = items,
            TotalCount = items.Count,
            GroupDn = src.GroupDn,
            GroupName = src.GroupName
        };
    }

    private static ADGroupDetailSummary FilterGroupDetail(ADGroupDetailSummary? src, UserSidSet u, ArPermissionModel m)
    {
        if (src == null) return new ADGroupDetailSummary();
        if (src.Error != null) return src;
        var items = PermissionScope.VisibleItems(src.Items, u, m);
        return new ADGroupDetailSummary { Items = items, TotalCount = items.Count };
    }

    private static ComputerBreakdownSummary FilterComputer(ComputerBreakdownSummary? src, UserSidSet u, ArPermissionModel m)
    {
        if (src == null) return new ComputerBreakdownSummary();
        if (src.Error != null) return src;
        var items = PermissionScope.VisibleItems(src.Items, u, m);
        return new ComputerBreakdownSummary { Items = items, TotalCount = items.Count };
    }

    private static DomainControllersSummary FilterDomainControllers(DomainControllersSummary? src, UserSidSet u, ArPermissionModel m)
    {
        if (src == null) return new DomainControllersSummary();
        if (src.Error != null) return src;
        var items = PermissionScope.VisibleItems(src.Items, u, m);
        return new DomainControllersSummary { Items = items, TotalCount = items.Count };
    }

    private static DomainSummary FilterDomains(DomainSummary? src, UserSidSet u, ArPermissionModel m)
    {
        if (src == null) return new DomainSummary();
        if (src.Error != null) return src;
        var items = PermissionScope.VisibleItems(src.Items, u, m);
        return new DomainSummary { Items = items, TotalCount = items.Count };
    }

    private static ServerSummary FilterServers(ServerSummary? src, UserSidSet u, ArPermissionModel m)
    {
        if (src == null) return new ServerSummary();
        if (src.Error != null) return src;
        var items = PermissionScope.VisibleItems(src.Items, u, m);
        return new ServerSummary { Items = items, TotalCount = items.Count };
    }

    /// <summary>
    /// Keeps a scalar security-health signal only when its measured domain is visible to the viewer.
    /// Signals with no domain context (or measured against a hidden domain) are suppressed for
    /// non-admins by returning an empty (no-value) summary.
    /// </summary>
    private static SecurityHealthSummary ScopeSecurityHealth(SecurityHealthSummary? src, ISet<string> visibleDomains)
    {
        if (src == null) return new SecurityHealthSummary();
        if (src.Error != null) return src;
        if (!string.IsNullOrEmpty(src.Domain) && visibleDomains.Contains(src.Domain))
            return src;
        return new SecurityHealthSummary { Error = "Not visible", Domain = src.Domain };
    }

    // ----- reflection-free accessors for the raw JsonElement summaries -------

    private static string? GetError(object summary) => summary switch
    {
        ADUserAccountsSummary a => a.Error,
        ADGroupsSummary g => g.Error,
        ComputersSummary c => c.Error,
        _ => null
    };

    private static List<JsonElement> GetRawItems(object summary) => summary switch
    {
        ADUserAccountsSummary a => a.Items,
        ADGroupsSummary g => g.Items,
        ComputersSummary c => c.Items,
        _ => new List<JsonElement>()
    };
}
