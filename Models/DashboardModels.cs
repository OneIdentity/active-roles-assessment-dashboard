using System.Text.Json;
using System.Text.Json.Serialization;
using ActiveRolesDashboard.Models.Reporting;
using ActiveRolesDashboard.Services;

namespace ActiveRolesDashboard.Models;

public enum CategorySortOrder
{
    Custom,
    AtoZ,
    ZtoA,
    CustomThenAtoZ
}

public class DashboardInfo
{
    public string Key { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Image { get; init; } = string.Empty;
    public bool RequiresAdmin { get; init; }
    public CategorySortOrder CategoryOrder { get; init; } = CategorySortOrder.Custom;

    public static readonly DashboardInfo ActiveRoles = new()
    {
        Key = "ActiveRoles",
        Title = "Active Roles",
        Subtitle = "Active Roles configuration KPIs",
        Url = "/ActiveRoles",
        Image = "images/Active Roles.png",
        RequiresAdmin = true
    };

    public static readonly DashboardInfo ActiveDirectory = new()
    {
        Key = "ActiveDirectory",
        Title = "Active Directory",
        Subtitle = "Active Directory KPIs",
        Url = "/ActiveDirectory",
        Image = "images/Active Directory.png",
        CategoryOrder = CategorySortOrder.AtoZ
    };

    public static readonly DashboardInfo EntraId = new()
    {
        Key = "EntraId",
        Title = "Entra ID",
        Subtitle = "Entra ID KPIs",
        Url = "/EntraId",
        Image = "images/Entra ID.png",
        CategoryOrder = CategorySortOrder.AtoZ
    };

    public static readonly DashboardInfo Licensing = new()
    {
        Key = "Licensing",
        Title = "Licensing",
        Subtitle = "Licensing and compliance KPIs",
        Url = "/Licensing",
        Image = "images/Licensing.png"
    };

    public static readonly IReadOnlyList<DashboardInfo> All = [ActiveRoles, ActiveDirectory, EntraId, Licensing];
}

public class CategoryInfo
{
    private readonly string _displayName = string.Empty;

    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Localized category name resolved at read-time (key <c>Cat_{Key}</c>) via
    /// <see cref="KpiLocalizer"/>, falling back to the literal set at initialization.
    /// </summary>
    public string DisplayName
    {
        get => KpiLocalizer.Localize($"Cat_{Key}", _displayName);
        init => _displayName = value;
    }

    public string DashboardKey { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public CategorySortOrder KpiSortOrder { get; init; } = CategorySortOrder.Custom;

    /// <summary>When true, this is a "Governance and Risk" category that aggregates KPIs flagged <see cref="KpiInfo.IsRiskKpi"/> from other categories on the same dashboard.</summary>
    public bool IsRiskCategory { get; init; }

    /// <summary>When true, this is an "Overview" category that floats to the very top of its dashboard (above Governance and Risk) and is default-expanded.</summary>
    public bool IsOverviewCategory { get; init; }

    public static readonly CategoryInfo Overview = new()
    {
        Key = "Overview",
        DisplayName = "Overview",
        DashboardKey = "Main",
        SortOrder = 0,
        IsOverviewCategory = true
    };

    public static readonly CategoryInfo ADOverview = new()
    {
        Key = "ADOverview",
        DisplayName = "Overview",
        DashboardKey = "ActiveDirectory",
        SortOrder = -1,
        IsOverviewCategory = true
    };

    public static readonly CategoryInfo EntraOverview = new()
    {
        Key = "EntraOverview",
        DisplayName = "Overview",
        DashboardKey = "EntraId",
        SortOrder = -1,
        IsOverviewCategory = true
    };

    public static readonly CategoryInfo ARConfiguration = new()
    {
        Key = "ARConfiguration",
        DisplayName = "Active Roles Configuration",
        DashboardKey = "ActiveRoles",
        SortOrder = 0,
        KpiSortOrder = CategorySortOrder.CustomThenAtoZ
    };

    public static readonly CategoryInfo ADGovernance = new()
    {
        Key = "ADGovernance",
        DisplayName = "Governance and Risk",
        DashboardKey = "ActiveDirectory",
        SortOrder = 0,
        KpiSortOrder = CategorySortOrder.AtoZ,
        IsRiskCategory = true
    };

    public static readonly CategoryInfo PrivilegedGroups = new()
    {
        Key = "PrivilegedGroups",
        DisplayName = "Privileged Groups",
        DashboardKey = "ActiveDirectory",
        SortOrder = 1,
        KpiSortOrder = CategorySortOrder.AtoZ
    };

    public static readonly CategoryInfo ADUserAccountsCategory = new()
    {
        Key = "ADUserAccountsCategory",
        DisplayName = "User Accounts",
        DashboardKey = "ActiveDirectory",
        SortOrder = 2,
        KpiSortOrder = CategorySortOrder.AtoZ
    };

    public static readonly CategoryInfo ADGroupsCategory = new()
    {
        Key = "ADGroupsCategory",
        DisplayName = "Groups",
        DashboardKey = "ActiveDirectory",
        SortOrder = 3,
        KpiSortOrder = CategorySortOrder.AtoZ
    };

    public static readonly CategoryInfo PrivilegedUsers = new()
    {
        Key = "PrivilegedUsers",
        DisplayName = "Privileged Users",
        DashboardKey = "ActiveDirectory",
        SortOrder = 4,
        KpiSortOrder = CategorySortOrder.AtoZ
    };

    public static readonly CategoryInfo Infrastructure = new()
    {
        Key = "Infrastructure",
        DisplayName = "Infrastructure",
        DashboardKey = "ActiveDirectory",
        SortOrder = 5,
        KpiSortOrder = CategorySortOrder.AtoZ
    };

    public static readonly CategoryInfo ComputersCategory = new()
    {
        Key = "ComputersCategory",
        DisplayName = "Computers",
        DashboardKey = "ActiveDirectory",
        SortOrder = 6,
        KpiSortOrder = CategorySortOrder.AtoZ
    };

    public static readonly CategoryInfo NHIs = new()
    {
        Key = "NHIs",
        DisplayName = "NHIs",
        DashboardKey = "ActiveDirectory",
        SortOrder = 7,
        KpiSortOrder = CategorySortOrder.AtoZ
    };

    public static readonly CategoryInfo EntraIDGovernance = new()
    {
        Key = "EntraIDGovernance",
        DisplayName = "Governance and Risk",
        DashboardKey = "EntraId",
        SortOrder = 0,
        KpiSortOrder = CategorySortOrder.AtoZ,
        IsRiskCategory = true
    };

    public static readonly CategoryInfo EntraUserAccounts = new()
    {
        Key = "EntraUserAccounts",
        DisplayName = "User Accounts",
        DashboardKey = "EntraId",
        SortOrder = 1,
        KpiSortOrder = CategorySortOrder.AtoZ
    };

    public static readonly CategoryInfo EntraGroups = new()
    {
        Key = "EntraGroups",
        DisplayName = "Groups",
        DashboardKey = "EntraId",
        SortOrder = 2,
        KpiSortOrder = CategorySortOrder.AtoZ
    };

    public static readonly CategoryInfo Licensing = new()
    {
        Key = "Licensing",
        DisplayName = "Licensing",
        DashboardKey = "Licensing",
        SortOrder = 0
    };

    public static readonly IReadOnlyList<CategoryInfo> All = [Overview, ADOverview, EntraOverview, ARConfiguration, ADGovernance, PrivilegedGroups, ADUserAccountsCategory, ADGroupsCategory, PrivilegedUsers, Infrastructure, ComputersCategory, NHIs, EntraIDGovernance, EntraUserAccounts, EntraGroups, Licensing];

    private static readonly Dictionary<string, CategoryInfo> ByKey = All.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolves a category by its key, or null when unknown.</summary>
    public static CategoryInfo? FromKey(string key) => ByKey.TryGetValue(key, out var category) ? category : null;

    public static IReadOnlyList<CategoryInfo> ForDashboard(DashboardInfo dashboard)
    {
        var categories = All.Where(c => c.DashboardKey == dashboard.Key);
        var ordered = dashboard.CategoryOrder switch
        {
            CategorySortOrder.AtoZ => categories.OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase).ToList(),
            CategorySortOrder.ZtoA => categories.OrderByDescending(c => c.DisplayName, StringComparer.OrdinalIgnoreCase).ToList(),
            _ => categories.OrderBy(c => c.SortOrder).ToList()
        };

        // Float the "Overview" category to the very top, then "Governance and Risk",
        // regardless of the chosen ordering, so Overview leads and Governance follows as
        // the two default-expanded categories.
        return ordered
            .OrderByDescending(c => c.IsOverviewCategory)
            .ThenByDescending(c => c.IsRiskCategory)
            .ToList();
    }

    /// <summary>
    /// Returns the categories that can be exported from a given dashboard. The main
    /// dashboard ("Main") is an aggregate hub, so its export spans its own Overview plus
    /// every child dashboard (Active Roles, Active Directory, Entra ID, Licensing) in
    /// canonical order. Any other dashboard exports only its own categories.
    /// </summary>
    public static IReadOnlyList<CategoryInfo> ForExport(string dashboardKey)
    {
        static IEnumerable<CategoryInfo> OrderedFor(string key) =>
            All.Where(c => c.DashboardKey == key)
                .OrderByDescending(c => c.IsOverviewCategory)
                .ThenByDescending(c => c.IsRiskCategory)
                .ThenBy(c => c.SortOrder);

        if (dashboardKey == "Main")
        {
            // "Main" first (the aggregate Overview), then each child dashboard in the
            // canonical DashboardInfo.All order.
            var keys = new List<string> { "Main" };
            keys.AddRange(DashboardInfo.All.Select(d => d.Key));

            return keys
                .Distinct()
                .SelectMany(OrderedFor)
                .ToList();
        }

        return OrderedFor(dashboardKey).ToList();
    }
}

public enum ChartType
{
    Doughnut,
    Pie,
    Bar
}

/// <summary>
/// A single data point on a chart, sourced from an existing KPI's result.
/// </summary>
public class ChartSeriesItem
{
    /// <summary>The KPI key whose count provides this data point's value.</summary>
    public string KpiKey { get; init; } = string.Empty;

    /// <summary>Optional label override. Falls back to the KPI's Label when empty.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Optional CSS color name override. Falls back to the KPI's CssColor when empty.</summary>
    public string CssColor { get; init; } = string.Empty;
}

/// <summary>
/// Describes a chart rendered within a category, beneath the category's KPIs.
/// Charts derive their data from existing KPI results (no additional queries).
/// Not all categories have charts.
/// </summary>
public class ChartInfo
{
    private readonly string _title = string.Empty;

    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Localized chart title resolved at read-time (key <c>Chart_{Key}</c>) via
    /// <see cref="KpiLocalizer"/>, falling back to the literal set at initialization.
    /// </summary>
    public string Title
    {
        get => KpiLocalizer.Localize($"Chart_{Key}", _title);
        init => _title = value;
    }

    public string CategoryKey { get; init; } = string.Empty;
    public ChartType Type { get; init; } = ChartType.Doughnut;
    public int SortOrder { get; init; }

    /// <summary>The data points for this chart, each referencing a KPI by key.</summary>
    public IReadOnlyList<ChartSeriesItem> Series { get; init; } = [];

    /// <summary>
    /// When set, the chart is built from an Overview KPI's Active Directory vs Entra ID
    /// source split (via DashboardSummary.GetSourceSplit) instead of the Series list.
    /// </summary>
    public string SourceSplitKpiKey { get; init; } = string.Empty;

    /// <summary>
    /// Pixels each slice is pushed outward from the center, producing an "exploded" pie.
    /// 0 = no explosion.
    /// </summary>
    public int SliceOffset { get; init; }

    /// <summary>
    /// When true, this chart is locked to its declared Type (no donut/column toggle).
    /// Used for charts whose series are overlapping subsets, where a share-of-whole
    /// donut would be misleading.
    /// </summary>
    public bool DisableTypeToggle { get; init; }

    // CSS color names used for source-split slices, in order. The first two keep the
    // established AD (blue) / Entra (teal) mapping; the remainder give distinct colours
    // when a chart is split across multiple tenants.
    public static readonly IReadOnlyList<string> SourceSplitColors = ["blue", "teal", "purple", "amber", "pink", "slate", "indigo", "orange"];

    // Chart definitions.
    // Example: Computers category operating-system breakdown, derived from existing computer KPIs.
    public static readonly ChartInfo ComputerOsBreakdown = new()
    {
        Key = "ComputerOsBreakdown",
        Title = "Computers by Operating System",
        CategoryKey = "ComputersCategory",
        Type = ChartType.Doughnut,
        SortOrder = 0,
        Series =
        [
            new() { KpiKey = "WinServer2008R2" },
            new() { KpiKey = "WinServer2012R2" },
            new() { KpiKey = "WinServer2016" },
            new() { KpiKey = "WinServer2019" },
            new() { KpiKey = "WinServer2022" },
            new() { KpiKey = "WinServer2025" },
            new() { KpiKey = "ServerOther" },
            new() { KpiKey = "Win7" },
            new() { KpiKey = "Win81" },
            new() { KpiKey = "Win10_22H2" },
            new() { KpiKey = "Win11_22H2" },
            new() { KpiKey = "Win11_23H2" },
            new() { KpiKey = "Win11Enterprise" },
            new() { KpiKey = "Win11Pro" },
            new() { KpiKey = "ClientsOther" }
        ]
    };

    // Groups category: breakdown of AD groups by group type.
    public static readonly ChartInfo GroupTypeBreakdown = new()
    {
        Key = "GroupTypeBreakdown",
        Title = "Groups by Type",
        CategoryKey = "ADGroupsCategory",
        Type = ChartType.Doughnut,
        SortOrder = 0,
        SliceOffset = 0,
        Series =
        [
            new() { KpiKey = "DistributionGroups", CssColor = "blue" },
            new() { KpiKey = "DomainLocalGroups", CssColor = "teal" },
            new() { KpiKey = "GlobalGroups", CssColor = "green" },
            new() { KpiKey = "MailEnabledSecurityGroups", CssColor = "amber" },
            new() { KpiKey = "SecurityGroups", CssColor = "purple" },
            new() { KpiKey = "UniversalGroups", CssColor = "slate" }
        ]
    };

    // AD User Accounts category: enabled vs disabled is a true partition of the total,
    // so it renders as a donut (with the standard donut/column toggle).
    public static readonly ChartInfo UserAccountStateBreakdown = new()
    {
        Key = "UserAccountStateBreakdown",
        Title = "Users by Account State",
        CategoryKey = "ADUserAccountsCategory",
        Type = ChartType.Doughnut,
        SortOrder = 0,
        SliceOffset = 0,
        Series =
        [
            new() { KpiKey = "EnabledUsers", CssColor = "green" },
            new() { KpiKey = "DisabledUsers", CssColor = "red" }
        ]
    };

    // AD User Accounts category: Must Change vs Cannot Change password are mutually
    // exclusive per account, so this is a donut (with the standard donut/column toggle).
    public static readonly ChartInfo UserPasswordControlBreakdown = new()
    {
        Key = "UserPasswordControlBreakdown",
        Title = "Password Change Control",
        CategoryKey = "ADUserAccountsCategory",
        Type = ChartType.Doughnut,
        SortOrder = 1,
        SliceOffset = 0,
        Series =
        [
            new() { KpiKey = "MustChangePassword", CssColor = "blue" },
            new() { KpiKey = "CannotChangePassword", CssColor = "slate" }
        ]
    };

    // AD User Accounts category: Trusted for Delegation vs Sensitive (cannot be delegated)
    // are mutually exclusive per account, so this is a donut (with the donut/column toggle).
    public static readonly ChartInfo UserDelegationBreakdown = new()
    {
        Key = "UserDelegationBreakdown",
        Title = "Delegation Control",
        CategoryKey = "ADUserAccountsCategory",
        Type = ChartType.Doughnut,
        SortOrder = 2,
        SliceOffset = 0,
        Series =
        [
            new() { KpiKey = "TrustedForDelegation", CssColor = "green" },
            new() { KpiKey = "SensitiveCannotDelegate", CssColor = "slate" }
        ]
    };

    // AD User Accounts category: remaining account options are overlapping subsets
    // (not a partition), so this is a column-only chart with the toggle disabled.
    public static readonly ChartInfo UserAccountOptions = new()
    {
        Key = "UserAccountOptions",
        Title = "Account Options",
        CategoryKey = "ADUserAccountsCategory",
        Type = ChartType.Bar,
        SortOrder = 3,
        DisableTypeToggle = true,
        Series =
        [
            new() { KpiKey = "ExpiringUsers", CssColor = "orange" },
            new() { KpiKey = "PasswordNeverExpires", CssColor = "purple" },
            new() { KpiKey = "PasswordNotRequired", CssColor = "amber" },
            new() { KpiKey = "SmartCardRequired", CssColor = "teal" },
            new() { KpiKey = "UserReversibleEncryption", CssColor = "red" },
            new() { KpiKey = "UseDesEncryption", CssColor = "orange" },
            new() { KpiKey = "NoKerberosPreauth", CssColor = "pink" }
        ]
    };

    // Overview: exploded pie charts splitting each total by data source (AD vs Entra ID).
    public static readonly ChartInfo OverviewUsersBySource = new()
    {
        Key = "OverviewUsersBySource",
        Title = "Users by Source",
        CategoryKey = "Overview",
        Type = ChartType.Pie,
        SortOrder = 0,
        SliceOffset = 16,
        SourceSplitKpiKey = "ADUserAccounts"
    };

    public static readonly ChartInfo OverviewGroupsBySource = new()
    {
        Key = "OverviewGroupsBySource",
        Title = "Groups by Source",
        CategoryKey = "Overview",
        Type = ChartType.Pie,
        SortOrder = 1,
        SliceOffset = 16,
        SourceSplitKpiKey = "ADGroups"
    };

    public static readonly ChartInfo OverviewComputersBySource = new()
    {
        Key = "OverviewComputersBySource",
        Title = "Computers / Devices by Source",
        CategoryKey = "Overview",
        Type = ChartType.Pie,
        SortOrder = 2,
        SliceOffset = 16,
        SourceSplitKpiKey = "Computers"
    };

    // Single-source Overview charts for the Active Directory dashboard (AD-only slice).
    public static readonly ChartInfo AdOverviewUsersChart = new()
    {
        Key = "AdOverviewUsersChart",
        Title = "Users by Source",
        CategoryKey = "ADOverview",
        Type = ChartType.Pie,
        SortOrder = 0,
        SliceOffset = 16,
        SourceSplitKpiKey = "AdOverviewUsers"
    };

    public static readonly ChartInfo AdOverviewGroupsChart = new()
    {
        Key = "AdOverviewGroupsChart",
        Title = "Groups by Source",
        CategoryKey = "ADOverview",
        Type = ChartType.Pie,
        SortOrder = 1,
        SliceOffset = 16,
        SourceSplitKpiKey = "AdOverviewGroups"
    };

    public static readonly ChartInfo AdOverviewComputersChart = new()
    {
        Key = "AdOverviewComputersChart",
        Title = "Computers / Devices by Source",
        CategoryKey = "ADOverview",
        Type = ChartType.Pie,
        SortOrder = 2,
        SliceOffset = 16,
        SourceSplitKpiKey = "AdOverviewComputers"
    };

    // Single-source Overview charts for the Entra ID dashboard (Entra-only slice).
    public static readonly ChartInfo EntraOverviewUsersChart = new()
    {
        Key = "EntraOverviewUsersChart",
        Title = "Users by Source",
        CategoryKey = "EntraOverview",
        Type = ChartType.Pie,
        SortOrder = 0,
        SliceOffset = 16,
        SourceSplitKpiKey = "EntraOverviewUsers"
    };

    public static readonly ChartInfo EntraOverviewGroupsChart = new()
    {
        Key = "EntraOverviewGroupsChart",
        Title = "Groups by Source",
        CategoryKey = "EntraOverview",
        Type = ChartType.Pie,
        SortOrder = 1,
        SliceOffset = 16,
        SourceSplitKpiKey = "EntraOverviewGroups"
    };

    // Entra User Accounts category: enabled vs disabled is a true partition of the total,
    // so it renders as a donut (with the standard donut/column toggle), mirroring AD.
    public static readonly ChartInfo EntraUserAccountStateBreakdown = new()
    {
        Key = "EntraUserAccountStateBreakdown",
        Title = "Users by Account State",
        CategoryKey = "EntraUserAccounts",
        Type = ChartType.Doughnut,
        SortOrder = 0,
        SliceOffset = 0,
        Series =
        [
            new() { KpiKey = "EntraEnabledUsers", CssColor = "green" },
            new() { KpiKey = "EntraDisabledUsers", CssColor = "red" }
        ]
    };

    // Entra User Accounts category: internal vs external is a true partition of the user
    // total (by #EXT# origin), so it renders as a donut with the standard donut/column toggle.
    public static readonly ChartInfo EntraUserOriginBreakdown = new()
    {
        Key = "EntraUserOriginBreakdown",
        Title = "Users by Origin",
        CategoryKey = "EntraUserAccounts",
        Type = ChartType.Doughnut,
        SortOrder = 1,
        SliceOffset = 0,
        Series =
        [
            new() { KpiKey = "EntraInternalUsers", CssColor = "teal" },
            new() { KpiKey = "EntraExternalUsers", CssColor = "pink" }
        ]
    };

    // Entra Groups category: a breakdown of the group population by group type, rendered as a
    // donut (with the standard donut/column toggle), mirroring the AD Groups by Type chart.
    public static readonly ChartInfo EntraGroupTypeBreakdown = new()
    {
        Key = "EntraGroupTypeBreakdown",
        Title = "Groups by Type",
        CategoryKey = "EntraGroups",
        Type = ChartType.Doughnut,
        SortOrder = 0,
        SliceOffset = 0,
        Series =
        [
            new() { KpiKey = "EntraDistributionGroups", CssColor = "blue" },
            new() { KpiKey = "EntraDynamicDistributionGroups", CssColor = "teal" },
            new() { KpiKey = "EntraMicrosoft365Groups", CssColor = "purple" },
            new() { KpiKey = "EntraSecurityGroups", CssColor = "green" }
        ]
    };

    public static readonly IReadOnlyList<ChartInfo> All =
    [
        ComputerOsBreakdown,
        GroupTypeBreakdown,
        UserAccountStateBreakdown, UserPasswordControlBreakdown, UserDelegationBreakdown, UserAccountOptions,
        OverviewUsersBySource, OverviewGroupsBySource, OverviewComputersBySource,
        AdOverviewUsersChart, AdOverviewGroupsChart, AdOverviewComputersChart,
        EntraOverviewUsersChart, EntraOverviewGroupsChart,
        EntraUserAccountStateBreakdown, EntraUserOriginBreakdown, EntraGroupTypeBreakdown
    ];


    public static IReadOnlyList<ChartInfo> ForCategory(CategoryInfo category) =>
        All.Where(c => c.CategoryKey == category.Key)
           .OrderBy(c => c.SortOrder)
           .ToList();

    public static IReadOnlyList<ChartInfo> ForCategory(string categoryKey) =>
        All.Where(c => c.CategoryKey == categoryKey)
           .OrderBy(c => c.SortOrder)
           .ToList();
}

/// <summary>
/// Defines an LDAP search operation used to retrieve KPI data.
/// Tokens in BaseDn and Filter are resolved at runtime from ActiveRolesConfig.
/// </summary>
public class KpiSearchDefinition
{
    /// <summary>Base DN for the search. Supports tokens: {DefaultADDN}, {DefaultARConfigDN}, {Custom:PropertyName}</summary>
    public string BaseDn { get; init; } = string.Empty;

    /// <summary>LDAP filter. Supports tokens: {ConfigFilter:PropertyName} to reference ActiveRolesConfig filter properties.</summary>
    public string Filter { get; init; } = string.Empty;

    /// <summary>Search scope (typically "sub").</summary>
    public string Scope { get; init; } = "sub";

    /// <summary>Comma-separated list of attributes to return.</summary>
    public string Attributes { get; init; } = "name,distinguishedName";

    /// <summary>For privileged group KPIs: the well-known group name to search for.</summary>
    public string? GroupName { get; init; }

    /// <summary>
    /// When set, this search shares results with other KPIs using the same key.
    /// The service can execute the search once and distribute results to all KPIs referencing it.
    /// </summary>
    public string? SharedSearchKey { get; init; }

    /// <summary>Resolves the BaseDn token to a concrete value using the provided config.</summary>
    public string ResolveBaseDn(ActiveRolesConfig config)
    {
        if (BaseDn == "{DefaultADDN}") return config.DefaultActiveDirectoryDN;
        if (BaseDn == "{DefaultARConfigDN}") return config.DefaultARConfigurationDN;
        return BaseDn;
    }

    /// <summary>Resolves the Filter token to a concrete value using the provided config.</summary>
    public string ResolveFilter(ActiveRolesConfig config)
    {
        if (Filter.StartsWith("{ConfigFilter:") && Filter.EndsWith("}"))
        {
            var propName = Filter["{ConfigFilter:".Length..^1];
            var prop = typeof(ActiveRolesConfig).GetProperty(propName);
            return prop?.GetValue(config)?.ToString() ?? Filter;
        }
        return Filter;
    }

    /// <summary>Resolves the Attributes token to a concrete value using the provided config.</summary>
    public string ResolveAttributes(ActiveRolesConfig config)
    {
        if (Attributes == "{ConfigAttributes:ADUserAccounts}")
        {
            return string.Join(",", config.DefaultADUserAccountAttributes.Concat(config.CustomADUserAccountAttributes).Distinct());
        }
        return Attributes;
    }
}

public class ActiveRolesConfig
{
    public string WebInterfaceUrl { get; set; } = string.Empty;
    public string RstsUrl { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string Resource { get; set; } = string.Empty;
    public bool IgnoreSslErrors { get; set; }

    // Default UI language / culture code applied before a user selects their own in Settings.
    public string DefaultLanguage { get; set; } = SupportedLanguage.DefaultCode;

    // Single default base DN for all KPI searches
    public string DefaultActiveDirectoryDN { get; set; } = "CN=Active Directory";
    public string DefaultARConfigurationDN { get; set; } = "CN=Configuration";

    // Base DN under which Active Roles exposes connected Azure/Entra tenants.
    // Each immediate child container is a tenant (e.g. CN=contoso.onmicrosoft.com,CN=Azure,CN=Configuration).
    public string DefaultAzureConfigurationDN { get; set; } = "CN=Azure,CN=Configuration";

    // Maximum depth to expand when walking nested group membership (safety cap).
    public int MaxGroupTreeDepth { get; set; } = 10;

    // Directory where KPI snapshots are stored (relative paths are resolved under the content root).
    public string SnapshotDirectory { get; set; } = "App_Data/Snapshots";

    // Directory where saved assessment results are stored (relative paths are resolved under the content root).
    public string AssessmentDirectory { get; set; } = "App_Data/Assessments";

    // Default filters for governance KPIs
    public string DefaultNoGroupOwnerFilter { get; set; } = "(&(objectClass=group)(!(managedBy=*))(!(edsvaSecondaryOwners=*)))";
    public string DefaultNoManagerUserFilter { get; set; } = "(&(objectClass=user)(objectCategory=person)(!(manager=*)))";
    public string DefaultNoManagerServiceAccountFilter { get; set; } = "(&(objectClass=user)(objectCategory=person)(servicePrincipalName=*)(!(manager=*)))";
    public string DefaultServiceAccountsFilter { get; set; } = "(&(objectClass=user)(objectCategory=person)(servicePrincipalName=*))";
    public string DefaultGmsaServiceAccountsFilter { get; set; } = "(objectClass=msDS-GroupManagedServiceAccount)";
    public string DefaultSmsaServiceAccountsFilter { get; set; } = "(objectClass=msDS-ManagedServiceAccount)";
    public string DefaultUserAccountExpiredFilter { get; set; } = "(&(objectClass=user)(objectCategory=person)(edsvaAccountIsExpired=TRUE))";
    public string DefaultUserAccountLockedOutFilter { get; set; } = "(&(objectClass=user)(objectCategory=person)(lockoutTime>=1))";
    public string DefaultEmptyGroupsFilter { get; set; } = "(&(objectClass=group)(!(member=*)))";
    public string DefaultActiveRolesAdminsFilter { get; set; } = "(&(objectClass=group)(name=APP-ACTIVEROLES-ADMINS))";
    public string DefaultADUserAccountsFilter { get; set; } = "(&(objectClass=user)(objectCategory=person))";
    public string DefaultADGroupsFilter { get; set; } = "(objectClass=group)";
    public List<string> DefaultADUserAccountAttributes { get; set; } = new();
    public List<string> CustomADUserAccountAttributes { get; set; } = new();

    // Number of days without an interactive logon after which an enabled account is
    // considered stale (used by the StaleUsers KPI and the HYG-StaleAccounts rule).
    public int StaleAccountThresholdDays { get; set; } = 90;

    // Maximum number of concurrent per-group membership fetches when lazily loading
    // Entra group membership (the 'member' attribute) for the Entra Groups hygiene KPIs.
    public int EntraMembershipFetchConcurrency { get; set; } = 8;

    // Number of groups the client requests per batch when lazily loading Entra group
    // membership. Smaller batches make the header progress badge decrement more smoothly
    // at the cost of more round-trips; larger batches reduce round-trips.
    public int EntraMembershipBatchSize { get; set; } = 40;

    // Delay in milliseconds before the "loading group membership" start toast is shown.
    // The toast is only displayed if membership loading is still in progress after this
    // delay, so fast loads do not flash a transient message.
    public int EntraMembershipToastDelayMs { get; set; } = 500;

    // Member-count threshold at or above which an Entra group is considered "large"
    // (oversized) for the Large Groups hygiene KPI. Derived from the lazily-loaded
    // 'member' attribute, so it does not add per-group lookup latency.
    public int EntraLargeGroupMemberThreshold { get; set; } = 100;

    // Licensed entitlement thresholds for the Managed Objects (Licensing) KPI. Each value is the
    // number of licensed managed objects for a category; the Licensing dashboard compares the latest
    // observed totals against these. A value of 0 means "not configured" (no threshold line / no
    // breach styling). LicensedTotalObjects is the grand-total entitlement across all categories.
    public int LicensedDomainObjects { get; set; }
    public int LicensedPartitionObjects { get; set; }
    public int LicensedAzureObjects { get; set; }
    public int LicensedSaasObjects { get; set; }
    public int LicensedTotalObjects { get; set; }

    // Custom overrides (blank = use default)
    public string CustomNoGroupOwnerBaseDn { get; set; } = string.Empty;
    public string CustomNoManagerUserBaseDn { get; set; } = string.Empty;
    public string CustomNoManagerUserFilter { get; set; } = string.Empty;
    public string CustomNoManagerServiceAccountBaseDn { get; set; } = string.Empty;
    public string CustomNoManagerServiceAccountFilter { get; set; } = string.Empty;
    public string CustomUserAccountExpiredBaseDn { get; set; } = string.Empty;
    public string CustomUserAccountLockedOutBaseDn { get; set; } = string.Empty;
    public string CustomEmptyGroupsBaseDn { get; set; } = string.Empty;
    public string CustomActiveRolesAdminsBaseDn { get; set; } = string.Empty;
    public string CustomActiveRolesAdminsFilter { get; set; } = string.Empty;

    // Service account used to collect the shared dashboard superset at application startup
    // and on scheduled/manual refresh. End-user tokens cannot read AR configuration
    // (Access Templates / AT Links), so a dedicated service account performs collection and
    // the per-user permission filtering is derived from its view.
    public ServiceAccountConfig ServiceAccount { get; set; } = new();
}

/// <summary>
/// Configuration for the background collection service account and the shared-superset refresh schedule.
/// </summary>
public class ServiceAccountConfig
{
    /// <summary>Service-account username (e.g. "PROD\\svc_ars") used to acquire an RSTS token for collection.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// The service-account password, stored ENCRYPTED via ASP.NET Core Data Protection.
    /// Decrypted at runtime through <c>IDataProtector</c>. Never store plaintext here.
    /// Use the one-time protect utility to produce this value.
    /// </summary>
    public string ProtectedPassword { get; set; } = string.Empty;

    /// <summary>
    /// Local time of day (HH:mm, 24-hour) at which the shared superset is refreshed daily.
    /// The main dashboard also exposes a manual refresh for Active Roles admins.
    /// </summary>
    public string DailyRefreshTime { get; set; } = "02:00";

    /// <summary>
    /// Whether the superset should be (re)loaded automatically at application startup.
    /// </summary>
    public bool LoadOnStartup { get; set; } = true;
}

public class KpiInfo
{
    private readonly string _displayName = string.Empty;
    private readonly string _tileLabel = string.Empty;

    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Localized KPI name resolved at read-time (key <c>Kpi_{Key}</c>) via
    /// <see cref="KpiLocalizer"/>, falling back to the literal set at initialization.
    /// </summary>
    public string DisplayName
    {
        get => KpiLocalizer.Localize($"Kpi_{Key}", _displayName);
        init => _displayName = value;
    }

    /// <summary>
    /// Localized short tile label resolved at read-time (key <c>KpiTile_{Key}</c>) via
    /// <see cref="KpiLocalizer"/>, falling back to the literal set at initialization.
    /// </summary>
    public string TileLabel
    {
        get => KpiLocalizer.Localize($"KpiTile_{Key}", _tileLabel);
        init => _tileLabel = value;
    }

    public string CategoryKey { get; init; } = string.Empty;
    public string CssColor { get; init; } = string.Empty;
    public string SectionId { get; init; } = string.Empty;
    public bool HasDrilldown { get; init; }
    public int SortOrder { get; init; }

    /// <summary>When true, this KPI is also surfaced in the "Governance and Risk" category of its dashboard, in addition to its own category.</summary>
    public bool IsRiskKpi { get; init; }

    /// <summary>
    /// When true, this KPI's value can be broken down by segment (AD domain / Entra tenant)
    /// and therefore honours the dashboard's segment filter selection. When false, the KPI
    /// is a global signal (e.g. forest-wide) and is presented unfiltered / in a "Global" group.
    /// Seam for per-segment filtering and per-segment export; not yet consumed by rendering.
    /// </summary>
    public bool IsSegmentable { get; init; }

    /// <summary>Search definitions used to retrieve data for this KPI. Empty for derived KPIs.</summary>
    public IReadOnlyList<KpiSearchDefinition> Searches { get; init; } = [];

    /// <summary>The label shown on the KPI tile. Falls back to DisplayName if not set.</summary>
    public string Label => string.IsNullOrEmpty(TileLabel) ? DisplayName : TileLabel;

    // Overview KPIs (use config-driven filters/attributes; SharedSearchKey enables reuse)
    public static readonly KpiInfo ADUserAccounts = new() { Key = "ADUserAccounts", DisplayName = "Total Users", CategoryKey = "Overview", CssColor = "blue", SortOrder = 0, HasDrilldown = false, Searches = [new() { BaseDn = "{DefaultADDN}", Filter = "{ConfigFilter:DefaultADUserAccountsFilter}", Attributes = "{ConfigAttributes:ADUserAccounts}", SharedSearchKey = "ADUserAccounts" }] };
    public static readonly KpiInfo ADGroups = new() { Key = "ADGroups", DisplayName = "Total Groups", CategoryKey = "Overview", CssColor = "green", SortOrder = 1, HasDrilldown = false, Searches = [new() { BaseDn = "{DefaultADDN}", Filter = "{ConfigFilter:DefaultADGroupsFilter}", Attributes = "name,distinguishedName,groupType,edsaIsDynamicGroup,edsaMember,edsaMemberIndirect,mail,edsvaGFIsGroupFamily,edsaDomainNetbiosName", SharedSearchKey = "ADGroups" }] };
    public static readonly KpiInfo Computers = new() { Key = "Computers", DisplayName = "Total Computers", CategoryKey = "Overview", CssColor = "teal", SortOrder = 2, HasDrilldown = false, Searches = [new() { BaseDn = "{DefaultADDN}", Filter = "(objectClass=computer)", Attributes = "name,distinguishedName,userAccountControl,edsaDomainNetbiosName,operatingSystem,operatingSystemVersion,msDS-SiteName,pwdLastSet,lastLogonTimestamp" }] };

    // Single-source Overview KPIs (AD-only) shown on the Active Directory dashboard's Overview.
    // These reuse the shared AD datasets, so they carry no Searches of their own and honour the domain selection via the reduced summaries.
    public static readonly KpiInfo AdOverviewUsers = new() { Key = "AdOverviewUsers", DisplayName = "Total Users", CategoryKey = "ADOverview", CssColor = "blue", SortOrder = 0, HasDrilldown = false };
    public static readonly KpiInfo AdOverviewGroups = new() { Key = "AdOverviewGroups", DisplayName = "Total Groups", CategoryKey = "ADOverview", CssColor = "green", SortOrder = 1, HasDrilldown = false };
    public static readonly KpiInfo AdOverviewComputers = new() { Key = "AdOverviewComputers", DisplayName = "Total Computers", CategoryKey = "ADOverview", CssColor = "teal", SortOrder = 2, HasDrilldown = false };

    // Single-source Overview KPIs (Entra-only) shown on the Entra ID dashboard's Overview.
    // These reuse the Entra totals dataset and honour the tenant selection via the reduced summary.
    public static readonly KpiInfo EntraOverviewUsers = new() { Key = "EntraOverviewUsers", DisplayName = "Total Users", CategoryKey = "EntraOverview", CssColor = "blue", SortOrder = 0, HasDrilldown = false };
    public static readonly KpiInfo EntraOverviewGroups = new() { Key = "EntraOverviewGroups", DisplayName = "Total Groups", CategoryKey = "EntraOverview", CssColor = "green", SortOrder = 1, HasDrilldown = false };

    // Entra User Accounts KPIs (derived from the Entra totals user objects, honouring the tenant selection)
    public static readonly KpiInfo EntraEnabledUsers = new() { Key = "EntraEnabledUsers", DisplayName = "Enabled Users", CategoryKey = "EntraUserAccounts", CssColor = "green", SectionId = "entraenabledusers", SortOrder = 0, HasDrilldown = true };
    public static readonly KpiInfo EntraDisabledUsers = new() { Key = "EntraDisabledUsers", DisplayName = "Disabled Users", CategoryKey = "EntraUserAccounts", CssColor = "red", SectionId = "entradisabledusers", SortOrder = 1, HasDrilldown = true };
    public static readonly KpiInfo EntraNoManagerUser = new() { Key = "EntraNoManagerUser", DisplayName = "No Manager (User)", CategoryKey = "EntraUserAccounts", CssColor = "purple", SectionId = "entranomanageruser", SortOrder = 2, HasDrilldown = true, IsRiskKpi = true };
    public static readonly KpiInfo EntraGuestUsers = new() { Key = "EntraGuestUsers", DisplayName = "Guest Users", CategoryKey = "EntraUserAccounts", CssColor = "orange", SectionId = "entraguestusers", SortOrder = 3, HasDrilldown = true };
    public static readonly KpiInfo EntraInternalUsers = new() { Key = "EntraInternalUsers", DisplayName = "Internal Users", CategoryKey = "EntraUserAccounts", CssColor = "teal", SectionId = "entrainternalusers", SortOrder = 4, HasDrilldown = true };
    public static readonly KpiInfo EntraExternalUsers = new() { Key = "EntraExternalUsers", DisplayName = "External Users", CategoryKey = "EntraUserAccounts", CssColor = "pink", SectionId = "entraexternalusers", SortOrder = 5, HasDrilldown = true };
    public static readonly KpiInfo EntraDistributionGroups = new() { Key = "EntraDistributionGroups", DisplayName = "Distribution Groups", CategoryKey = "EntraGroups", CssColor = "blue", SectionId = "entradistributiongroups", SortOrder = 0, HasDrilldown = true };
    public static readonly KpiInfo EntraDynamicDistributionGroups = new() { Key = "EntraDynamicDistributionGroups", DisplayName = "Dynamic Distribution Groups", CategoryKey = "EntraGroups", CssColor = "teal", SectionId = "entradynamicdistributiongroups", SortOrder = 1, HasDrilldown = true };
    public static readonly KpiInfo EntraMicrosoft365Groups = new() { Key = "EntraMicrosoft365Groups", DisplayName = "Microsoft 365 Groups", CategoryKey = "EntraGroups", CssColor = "purple", SectionId = "entramicrosoft365groups", SortOrder = 2, HasDrilldown = true };
    public static readonly KpiInfo EntraSecurityGroups = new() { Key = "EntraSecurityGroups", DisplayName = "Security Groups", CategoryKey = "EntraGroups", CssColor = "green", SectionId = "entrasecuritygroups", SortOrder = 3, HasDrilldown = true };
    public static readonly KpiInfo EntraEmptyGroups = new() { Key = "EntraEmptyGroups", DisplayName = "Empty Groups", CategoryKey = "EntraGroups", CssColor = "slate", SectionId = "entraemptygroups", SortOrder = 4, HasDrilldown = true, IsRiskKpi = true };
    public static readonly KpiInfo EntraNoGroupOwner = new() { Key = "EntraNoGroupOwner", DisplayName = "No Group Owner", CategoryKey = "EntraGroups", CssColor = "amber", SectionId = "entranogroupowner", SortOrder = 5, HasDrilldown = true, IsRiskKpi = true };
    public static readonly KpiInfo EntraGuestContainingGroups = new() { Key = "EntraGuestContainingGroups", DisplayName = "Guest-Containing Groups", CategoryKey = "EntraGroups", CssColor = "orange", SectionId = "entraguestcontaininggroups", SortOrder = 6, HasDrilldown = true, IsRiskKpi = true };
    public static readonly KpiInfo EntraPublicGroups = new() { Key = "EntraPublicGroups", DisplayName = "Public M365 Groups", CategoryKey = "EntraGroups", CssColor = "orange", SectionId = "entrapublicgroups", SortOrder = 7, HasDrilldown = true, IsRiskKpi = true };
    public static readonly KpiInfo EntraOnPremSyncedGroups = new() { Key = "EntraOnPremSyncedGroups", DisplayName = "On-Premises Synced Groups", CategoryKey = "EntraGroups", CssColor = "teal", SectionId = "entraonpremsyncedgroups", SortOrder = 8, HasDrilldown = true };
    public static readonly KpiInfo EntraSingleOwnerGroups = new() { Key = "EntraSingleOwnerGroups", DisplayName = "Single-Owner Groups", CategoryKey = "EntraGroups", CssColor = "amber", SectionId = "entrasingleownergroups", SortOrder = 9, HasDrilldown = true, IsRiskKpi = true };
    public static readonly KpiInfo EntraLargeGroups = new() { Key = "EntraLargeGroups", DisplayName = "Large Groups", CategoryKey = "EntraGroups", CssColor = "slate", SectionId = "entralargegroups", SortOrder = 10, HasDrilldown = true, IsRiskKpi = true };

    // AR Configuration KPIs
    public static readonly KpiInfo ActiveRolesAdmins = new() { Key = "ActiveRolesAdmins", DisplayName = "Active Roles Admins", TileLabel = "AR Admins", CategoryKey = "ARConfiguration", CssColor = "red", SectionId = "aradmins", SortOrder = 0, HasDrilldown = true, Searches = [new() { BaseDn = "{DefaultADDN}", Filter = "{ConfigFilter:DefaultActiveRolesAdminsFilter}", GroupName = "APP-ACTIVEROLES-ADMINS" }] };
    public static readonly KpiInfo Servers = new() { Key = "Servers", DisplayName = "AR Servers", CategoryKey = "ARConfiguration", CssColor = "green", SectionId = "servers", SortOrder = 1, HasDrilldown = true, Searches = [new() { BaseDn = "CN=Server Configuration,CN=Configuration", Filter = "(objectClass=edsARService)", Attributes = "edsaEdmServiceComputerName,edsvaPublicProductVersion" }] };
    public static readonly KpiInfo Domains = new() { Key = "Domains", DisplayName = "Managed Domains", CategoryKey = "ARConfiguration", CssColor = "blue", SectionId = "domains", SortOrder = 2, HasDrilldown = true, Searches = [new() { BaseDn = "CN=Managed Domains,CN=Server Configuration,CN=Configuration", Filter = "(objectClass=edsDomainCacheConfig)", Attributes = "name,edsvaDomainDNS,edsaSavedDnsName,edsaUseOverrideAccount" }] };
    public static readonly KpiInfo AccessTemplateLinks = new() { Key = "AccessTemplateLinks", DisplayName = "Access Template Links", CategoryKey = "ARConfiguration", CssColor = "orange", SectionId = "atlinks", SortOrder = 103, HasDrilldown = true, Searches = [new() { BaseDn = "CN=AT Links,CN=Configuration", Filter = "(objectClass=edsACE)", Attributes = "name,distinguishedName,edsaTrusteeSID,edsaSecObjectGUID,edsaAccessTemplateGUID,edsaIsPredefined,edsaSystemObject" }] };
    public static readonly KpiInfo AccessTemplates = new() { Key = "AccessTemplates", DisplayName = "Access Templates", CategoryKey = "ARConfiguration", CssColor = "red", SectionId = "accesstemplates", SortOrder = 104, HasDrilldown = true, Searches = [new() { BaseDn = "CN=Access Templates,CN=Configuration", Filter = "(objectClass=edsAccessTemplate)", Attributes = "name,distinguishedName,edsvaParentCanonicalName" }] };
    public static readonly KpiInfo DynamicGroups = new() { Key = "DynamicGroups", DisplayName = "Dynamic Groups", CategoryKey = "ARConfiguration", CssColor = "purple", SectionId = "dynamicgroups", SortOrder = 105, HasDrilldown = true, Searches = [new() { BaseDn = "CN=Configuration", Filter = "(objectClass=edsDynamicGroup)", Attributes = "name,distinguishedName" }] };
    public static readonly KpiInfo GroupFamilies = new() { Key = "GroupFamilies", DisplayName = "Group Families", CategoryKey = "ARConfiguration", CssColor = "purple", SectionId = "groupfamilies", SortOrder = 106, HasDrilldown = true };
    public static readonly KpiInfo ManagedUnits = new() { Key = "ManagedUnits", DisplayName = "Managed Units", CategoryKey = "ARConfiguration", CssColor = "teal", SectionId = "managedunits", SortOrder = 107, HasDrilldown = true, Searches = [new() { BaseDn = "CN=Managed Units,CN=Configuration", Filter = "(objectClass=edsManagedUnit)", Attributes = "name,distinguishedName,edsaMUConditionsList" }] };
    public static readonly KpiInfo PolicyObjectLinks = new() { Key = "PolicyObjectLinks", DisplayName = "Policy Object Links", CategoryKey = "ARConfiguration", CssColor = "amber", SectionId = "polinks", SortOrder = 108, HasDrilldown = true, Searches = [new() { BaseDn = "CN=AP Links,CN=Configuration", Filter = "(objectClass=edsPolicyObjectLink)", Attributes = "name,distinguishedName" }] };
    public static readonly KpiInfo PolicyObjects = new() { Key = "PolicyObjects", DisplayName = "Policy Objects", CategoryKey = "ARConfiguration", CssColor = "slate", SectionId = "policies", SortOrder = 109, HasDrilldown = true, Searches = [new() { BaseDn = "CN=Policies,CN=Configuration", Filter = "(objectClass=edsPolicyObject)", Attributes = "name,distinguishedName,edsaAPEListXML" }] };
    public static readonly KpiInfo VirtualAttributes = new() { Key = "VirtualAttributes", DisplayName = "Virtual Attributes", CategoryKey = "ARConfiguration", CssColor = "pink", SectionId = "virtualattrs", SortOrder = 110, HasDrilldown = true, Searches = [new() { BaseDn = "CN=Virtual Attributes,CN=Server Configuration,CN=Configuration", Filter = "(objectClass=edsVirtualAttribute)", Attributes = "name,lDAPDisplayName,isSingleValued" }] };
    public static readonly KpiInfo Workflows = new() { Key = "Workflows", DisplayName = "Workflows", CategoryKey = "ARConfiguration", CssColor = "amber", SectionId = "workflows", SortOrder = 111, HasDrilldown = true, Searches = [new() { BaseDn = "CN=Workflow,CN=Policies,CN=Configuration", Filter = "(|(objectClass=edsWorkflowDefinition)(objectClass=edsAutomationWorkflowDefinition))", Attributes = "name,distinguishedName,objectClass,edsaWorkflowIsDisabled" }] };
    public static readonly KpiInfo ConfigDatabases = new() { Key = "ConfigDatabases", DisplayName = "Config Databases", CategoryKey = "ARConfiguration", CssColor = "blue", SectionId = "configdatabases", SortOrder = 112, HasDrilldown = true, Searches = [new() { BaseDn = "CN=Configuration Databases,CN=Server Configuration,CN=Configuration", Filter = "(objectClass=edsReplicationPartner)", Attributes = "edsaSQLAlias,edsaDatabaseName,edsaDatabaseType,edsaReplicationSupport,edsaReplicationRole" }] };
    public static readonly KpiInfo HistoryDatabases = new() { Key = "HistoryDatabases", DisplayName = "History Databases", CategoryKey = "ARConfiguration", CssColor = "teal", SectionId = "historydatabases", SortOrder = 113, HasDrilldown = true, Searches = [new() { BaseDn = "CN=Management History Databases,CN=Server Configuration,CN=Configuration", Filter = "(objectClass=edsMHReplicationPartner)", Attributes = "edsaSQLAlias,edsaDatabaseName,edsaDatabaseType,edsaReplicationRole" }] };

    // AD Governance KPIs
    public static readonly KpiInfo NoGroupOwner = new() { Key = "NoGroupOwner", DisplayName = "No Group Owner", CategoryKey = "ADGroupsCategory", CssColor = "amber", SectionId = "nogroupowner", SortOrder = 6, HasDrilldown = true, IsRiskKpi = true, Searches = [new() { BaseDn = "{DefaultADDN}", Filter = "{ConfigFilter:DefaultNoGroupOwnerFilter}", Attributes = "name,distinguishedName" }] };
    public static readonly KpiInfo NoManagerUser = new() { Key = "NoManagerUser", DisplayName = "No Manager (User)", CategoryKey = "ADUserAccountsCategory", CssColor = "purple", SectionId = "nomanageruser", SortOrder = 13, HasDrilldown = true, IsRiskKpi = true, Searches = [new() { BaseDn = "{DefaultADDN}", Filter = "{ConfigFilter:DefaultNoManagerUserFilter}", Attributes = "name,distinguishedName" }] };
    public static readonly KpiInfo NoManagerServiceAccount = new() { Key = "NoManagerServiceAccount", DisplayName = "No Manager (Service Account)", CategoryKey = "NHIs", CssColor = "teal", SectionId = "nomanagersa", SortOrder = 14, HasDrilldown = true, IsRiskKpi = true, Searches = [new() { BaseDn = "{DefaultADDN}", Filter = "{ConfigFilter:DefaultNoManagerServiceAccountFilter}", Attributes = "name,distinguishedName" }] };
    public static readonly KpiInfo UserAccountLockedOut = new() { Key = "UserAccountLockedOut", DisplayName = "User Account Locked Out", CategoryKey = "ADUserAccountsCategory", CssColor = "pink", SectionId = "accountlockedout", SortOrder = 15, HasDrilldown = true, IsRiskKpi = true, Searches = [new() { BaseDn = "{DefaultADDN}", Filter = "{ConfigFilter:DefaultUserAccountLockedOutFilter}", Attributes = "name,distinguishedName" }] };
    public static readonly KpiInfo EmptyGroups = new() { Key = "EmptyGroups", DisplayName = "Empty Groups", CategoryKey = "ADGroupsCategory", CssColor = "slate", SectionId = "emptygroups", SortOrder = 7, HasDrilldown = true, IsRiskKpi = true, Searches = [new() { BaseDn = "{DefaultADDN}", Filter = "{ConfigFilter:DefaultEmptyGroupsFilter}", Attributes = "name,distinguishedName,edsaDomainNetbiosName" }] };
    public static readonly KpiInfo NeverLoggedIn = new() { Key = "NeverLoggedIn", DisplayName = "Never Logged In", CategoryKey = "ADUserAccountsCategory", CssColor = "orange", SectionId = "neverloggedin", SortOrder = 16, HasDrilldown = true, IsRiskKpi = true };
    public static readonly KpiInfo ExpiredUsers = new() { Key = "ExpiredUsers", DisplayName = "Expired Users", CategoryKey = "ADUserAccountsCategory", CssColor = "red", SectionId = "expiredusers", SortOrder = 17, HasDrilldown = true, IsRiskKpi = true, Searches = [new() { BaseDn = "{DefaultADDN}", Filter = "{ConfigFilter:DefaultUserAccountExpiredFilter}", Attributes = "name,distinguishedName" }] };
    public static readonly KpiInfo ReversibleEncryption = new() { Key = "ReversibleEncryption", DisplayName = "Reversible Encryption", CategoryKey = "ADUserAccountsCategory", CssColor = "red", SectionId = "reversibleencryption", SortOrder = 18, HasDrilldown = true, IsRiskKpi = true };

    // Privileged Groups KPIs
    public static readonly KpiInfo AccountOperators = new() { Key = "AccountOperators", DisplayName = "Account Operators", CategoryKey = "PrivilegedGroups", CssColor = "orange", SectionId = "accountoperators", SortOrder = 0, HasDrilldown = true, Searches = [new() { BaseDn = "{DefaultADDN}", Filter = "(&(objectClass=group)(name=Account Operators))", GroupName = "Account Operators", Attributes = "distinguishedName,edsaDomainNetbiosName,edsaMember,edsaMemberIndirect" }] };
    public static readonly KpiInfo Administrators = new() { Key = "Administrators", DisplayName = "Administrators", CategoryKey = "PrivilegedGroups", CssColor = "red", SectionId = "administrators", SortOrder = 1, HasDrilldown = true, Searches = [new() { BaseDn = "{DefaultADDN}", Filter = "(&(objectClass=group)(name=Administrators))", GroupName = "Administrators", Attributes = "distinguishedName,edsaDomainNetbiosName,edsaMember,edsaMemberIndirect" }] };
    public static readonly KpiInfo BackupOperators = new() { Key = "BackupOperators", DisplayName = "Backup Operators", CategoryKey = "PrivilegedGroups", CssColor = "amber", SectionId = "backupoperators", SortOrder = 2, HasDrilldown = true, Searches = [new() { BaseDn = "{DefaultADDN}", Filter = "(&(objectClass=group)(name=Backup Operators))", GroupName = "Backup Operators", Attributes = "distinguishedName,edsaDomainNetbiosName,edsaMember,edsaMemberIndirect" }] };
    public static readonly KpiInfo DomainAdmins = new() { Key = "DomainAdmins", DisplayName = "Domain Admins", CategoryKey = "PrivilegedGroups", CssColor = "pink", SectionId = "domainadmins", SortOrder = 3, HasDrilldown = true, Searches = [new() { BaseDn = "{DefaultADDN}", Filter = "(&(objectClass=group)(name=Domain Admins))", GroupName = "Domain Admins", Attributes = "distinguishedName,edsaDomainNetbiosName,edsaMember,edsaMemberIndirect" }] };
    public static readonly KpiInfo ServerOperators = new() { Key = "ServerOperators", DisplayName = "Server Operators", CategoryKey = "PrivilegedGroups", CssColor = "purple", SectionId = "serveroperators", SortOrder = 4, HasDrilldown = true, Searches = [new() { BaseDn = "{DefaultADDN}", Filter = "(&(objectClass=group)(name=Server Operators))", GroupName = "Server Operators", Attributes = "distinguishedName,edsaDomainNetbiosName,edsaMember,edsaMemberIndirect" }] };
    public static readonly KpiInfo EnterpriseAdmins = new() { Key = "EnterpriseAdmins", DisplayName = "Enterprise Admins", CategoryKey = "PrivilegedGroups", CssColor = "red", SectionId = "enterpriseadmins", SortOrder = 5, HasDrilldown = true, Searches = [new() { BaseDn = "{DefaultADDN}", Filter = "(&(objectClass=group)(name=Enterprise Admins))", GroupName = "Enterprise Admins", Attributes = "distinguishedName,edsaDomainNetbiosName,edsaMember,edsaMemberIndirect" }] };
    public static readonly KpiInfo SchemaAdmins = new() { Key = "SchemaAdmins", DisplayName = "Schema Admins", CategoryKey = "PrivilegedGroups", CssColor = "pink", SectionId = "schemaadmins", SortOrder = 6, HasDrilldown = true, Searches = [new() { BaseDn = "{DefaultADDN}", Filter = "(&(objectClass=group)(name=Schema Admins))", GroupName = "Schema Admins", Attributes = "distinguishedName,edsaDomainNetbiosName,edsaMember,edsaMemberIndirect" }] };

    // AD User Accounts KPIs (derived from shared ADUserAccounts search)
    public static readonly KpiInfo CannotChangePassword = new() { Key = "CannotChangePassword", DisplayName = "Cannot Change Password", CategoryKey = "ADUserAccountsCategory", CssColor = "slate", SectionId = "cannotchangepassword", SortOrder = 0, HasDrilldown = true };
    public static readonly KpiInfo DisabledUsers = new() { Key = "DisabledUsers", DisplayName = "Disabled Users", CategoryKey = "ADUserAccountsCategory", CssColor = "red", SectionId = "disabledusers", SortOrder = 1, HasDrilldown = true };
    public static readonly KpiInfo NoKerberosPreauth = new() { Key = "NoKerberosPreauth", DisplayName = "Do Not Require Kerberos Preauthentication", CategoryKey = "ADUserAccountsCategory", CssColor = "pink", SectionId = "nokerberospreauth", SortOrder = 2, HasDrilldown = true };
    public static readonly KpiInfo EnabledUsers = new() { Key = "EnabledUsers", DisplayName = "Enabled Users", CategoryKey = "ADUserAccountsCategory", CssColor = "green", SectionId = "enabledusers", SortOrder = 3, HasDrilldown = true };
    public static readonly KpiInfo ExpiringUsers = new() { Key = "ExpiringUsers", DisplayName = "Expiring Users", CategoryKey = "ADUserAccountsCategory", CssColor = "orange", SectionId = "expiringusers", SortOrder = 4, HasDrilldown = true };
    public static readonly KpiInfo MustChangePassword = new() { Key = "MustChangePassword", DisplayName = "Must Change Password", CategoryKey = "ADUserAccountsCategory", CssColor = "blue", SectionId = "mustchangepassword", SortOrder = 5, HasDrilldown = true };
    public static readonly KpiInfo PasswordNeverExpires = new() { Key = "PasswordNeverExpires", DisplayName = "Password Never Expires", CategoryKey = "ADUserAccountsCategory", CssColor = "purple", SectionId = "passwordneverexpires", SortOrder = 6, HasDrilldown = true };
    public static readonly KpiInfo PasswordNotRequired = new() { Key = "PasswordNotRequired", DisplayName = "Password Not Required", CategoryKey = "ADUserAccountsCategory", CssColor = "amber", SectionId = "passwordnotrequired", SortOrder = 7, HasDrilldown = true };
    public static readonly KpiInfo UserReversibleEncryption = new() { Key = "UserReversibleEncryption", DisplayName = "Reversible Encryption", CategoryKey = "ADUserAccountsCategory", CssColor = "red", SectionId = "userreversibleencryption", SortOrder = 8, HasDrilldown = true };
    public static readonly KpiInfo SensitiveCannotDelegate = new() { Key = "SensitiveCannotDelegate", DisplayName = "Sensitive - Cannot Be Delegated", CategoryKey = "ADUserAccountsCategory", CssColor = "slate", SectionId = "sensitivecannotdelegate", SortOrder = 9, HasDrilldown = true };
    public static readonly KpiInfo SmartCardRequired = new() { Key = "SmartCardRequired", DisplayName = "Smart Card Required", CategoryKey = "ADUserAccountsCategory", CssColor = "teal", SectionId = "smartcardrequired", SortOrder = 10, HasDrilldown = true };
    public static readonly KpiInfo TrustedForDelegation = new() { Key = "TrustedForDelegation", DisplayName = "Trusted for Delegation", CategoryKey = "ADUserAccountsCategory", CssColor = "green", SectionId = "trustedfordelegation", SortOrder = 11, HasDrilldown = true };
    public static readonly KpiInfo UseDesEncryption = new() { Key = "UseDesEncryption", DisplayName = "Use DES Encryption", CategoryKey = "ADUserAccountsCategory", CssColor = "orange", SectionId = "usedesencryption", SortOrder = 12, HasDrilldown = true };
    public static readonly KpiInfo DeprovisionedUsers = new() { Key = "DeprovisionedUsers", DisplayName = "Deprovisioned Users", CategoryKey = "ADUserAccountsCategory", CssColor = "pink", SectionId = "deprovisionedusers", SortOrder = 13, HasDrilldown = true };
    public static readonly KpiInfo SpnUserAccounts = new() { Key = "SpnUserAccounts", DisplayName = "Service Accounts (SPN)", CategoryKey = "NHIs", CssColor = "red", SectionId = "spnuseraccounts", SortOrder = 19, HasDrilldown = true, IsRiskKpi = true };
    public static readonly KpiInfo StaleUsers = new() { Key = "StaleUsers", DisplayName = "Stale Accounts (Inactive)", CategoryKey = "ADUserAccountsCategory", CssColor = "amber", SectionId = "staleusers", SortOrder = 20, HasDrilldown = true, IsRiskKpi = true };
    public static readonly KpiInfo ServiceAccounts = new() { Key = "ServiceAccounts", DisplayName = "Service Accounts", CategoryKey = "NHIs", CssColor = "teal", SectionId = "serviceaccounts", SortOrder = 21, HasDrilldown = true, Searches = [new() { BaseDn = "{DefaultADDN}", Filter = "{ConfigFilter:DefaultServiceAccountsFilter}", Attributes = "name,distinguishedName" }] };
    public static readonly KpiInfo GmsaServiceAccounts = new() { Key = "GmsaServiceAccounts", DisplayName = "gMSA Service Accounts", CategoryKey = "NHIs", CssColor = "purple", SectionId = "gmsaserviceaccounts", SortOrder = 22, HasDrilldown = true, Searches = [new() { BaseDn = "{DefaultADDN}", Filter = "{ConfigFilter:DefaultGmsaServiceAccountsFilter}", Attributes = "name,distinguishedName" }] };
    public static readonly KpiInfo SmsaServiceAccounts = new() { Key = "SmsaServiceAccounts", DisplayName = "sMSA Service Accounts", CategoryKey = "NHIs", CssColor = "purple", SectionId = "smsaserviceaccounts", SortOrder = 23, HasDrilldown = true, Searches = [new() { BaseDn = "{DefaultADDN}", Filter = "{ConfigFilter:DefaultSmsaServiceAccountsFilter}", Attributes = "name,distinguishedName" }] };

    // AD Groups KPIs (derived from shared ADGroups search)
    public static readonly KpiInfo DistributionGroups = new() { Key = "DistributionGroups", DisplayName = "Distribution Groups", CategoryKey = "ADGroupsCategory", CssColor = "blue", SectionId = "distributiongroups", SortOrder = 0, HasDrilldown = true };

    // Privileged Users KPIs (derived from shared ADUserAccounts search)
    public static readonly KpiInfo AdminCount = new() { Key = "AdminCount", DisplayName = "Admin Count", CategoryKey = "PrivilegedUsers", CssColor = "red", SectionId = "admincount", SortOrder = 0, HasDrilldown = true };

    // Infrastructure KPIs
    public static readonly KpiInfo Sites = new() { Key = "Sites", DisplayName = "Sites", CategoryKey = "Infrastructure", CssColor = "blue", SectionId = "sites", SortOrder = 0, HasDrilldown = true, Searches = [new() { BaseDn = "{DefaultADDN}", Filter = "(objectClass=site)", Attributes = "name,edsaDomainNetbiosName,distinguishedName" }] };
    public static readonly KpiInfo SiteLinks = new() { Key = "SiteLinks", DisplayName = "Site Links", CategoryKey = "Infrastructure", CssColor = "green", SectionId = "sitelinks", SortOrder = 1, HasDrilldown = true, Searches = [new() { BaseDn = "{DefaultADDN}", Filter = "(objectClass=siteLink)", Attributes = "name,edsaDomainNetbiosName,distinguishedName" }] };
    public static readonly KpiInfo Subnets = new() { Key = "Subnets", DisplayName = "Subnets", CategoryKey = "Infrastructure", CssColor = "teal", SectionId = "subnets", SortOrder = 2, HasDrilldown = true, Searches = [new() { BaseDn = "{DefaultADDN}", Filter = "(objectClass=subnet)", Attributes = "name,edsaDomainNetbiosName,distinguishedName" }] };
    public static readonly KpiInfo OUs = new() { Key = "OUs", DisplayName = "OUs", CategoryKey = "Infrastructure", CssColor = "orange", SectionId = "ous", SortOrder = 3, HasDrilldown = true, Searches = [new() { BaseDn = "{DefaultADDN}", Filter = "(objectClass=organizationalUnit)", Attributes = "name,edsaDomainNetbiosName,distinguishedName" }] };
    public static readonly KpiInfo DomainControllers = new() { Key = "DomainControllers", DisplayName = "Domain Controllers", CategoryKey = "Infrastructure", CssColor = "purple", SectionId = "domaincontrollers", SortOrder = 4, HasDrilldown = true };
    public static readonly KpiInfo DomainLocalGroups = new() { Key = "DomainLocalGroups", DisplayName = "Domain Local Groups", CategoryKey = "ADGroupsCategory", CssColor = "blue", SectionId = "domainlocalgroups", SortOrder = 1, HasDrilldown = true };
    public static readonly KpiInfo GlobalGroups = new() { Key = "GlobalGroups", DisplayName = "Global Groups", CategoryKey = "ADGroupsCategory", CssColor = "blue", SectionId = "globalgroups", SortOrder = 2, HasDrilldown = true };
    public static readonly KpiInfo MailEnabledSecurityGroups = new() { Key = "MailEnabledSecurityGroups", DisplayName = "Mail Enabled Security Groups", CategoryKey = "ADGroupsCategory", CssColor = "blue", SectionId = "mailenabledsecuritygroups", SortOrder = 3, HasDrilldown = true };
    public static readonly KpiInfo SecurityGroups = new() { Key = "SecurityGroups", DisplayName = "Security Groups", CategoryKey = "ADGroupsCategory", CssColor = "blue", SectionId = "securitygroups", SortOrder = 4, HasDrilldown = true };
    public static readonly KpiInfo UniversalGroups = new() { Key = "UniversalGroups", DisplayName = "Universal Groups", CategoryKey = "ADGroupsCategory", CssColor = "blue", SectionId = "universalgroups", SortOrder = 5, HasDrilldown = true };
    public static readonly KpiInfo CircularGroupNesting = new() { Key = "CircularGroupNesting", DisplayName = "Circular Group Nesting", CategoryKey = "ADGroupsCategory", CssColor = "red", SectionId = "circulargroupnesting", SortOrder = 8, HasDrilldown = true, IsRiskKpi = true };

    // Computers category KPIs (derived from Computers dataset)
    public static readonly KpiInfo ComputerClients = new() { Key = "ComputerClients", DisplayName = "Clients", CategoryKey = "ComputersCategory", CssColor = "blue", SectionId = "computerclients", SortOrder = 0, HasDrilldown = true };
    public static readonly KpiInfo ComputerServers = new() { Key = "ComputerServers", DisplayName = "Servers", CategoryKey = "ComputersCategory", CssColor = "green", SectionId = "computerservers", SortOrder = 1, HasDrilldown = true };
    public static readonly KpiInfo WinServer2008R2 = new() { Key = "WinServer2008R2", DisplayName = "Windows Server 2008 R2", CategoryKey = "ComputersCategory", CssColor = "red", SectionId = "winserver2008r2", SortOrder = 2, HasDrilldown = true };
    public static readonly KpiInfo WinServer2012R2 = new() { Key = "WinServer2012R2", DisplayName = "Windows Server 2012 R2", CategoryKey = "ComputersCategory", CssColor = "red", SectionId = "winserver2012r2", SortOrder = 3, HasDrilldown = true };
    public static readonly KpiInfo WinServer2016 = new() { Key = "WinServer2016", DisplayName = "Windows Server 2016", CategoryKey = "ComputersCategory", CssColor = "orange", SectionId = "winserver2016", SortOrder = 4, HasDrilldown = true };
    public static readonly KpiInfo WinServer2019 = new() { Key = "WinServer2019", DisplayName = "Windows Server 2019", CategoryKey = "ComputersCategory", CssColor = "amber", SectionId = "winserver2019", SortOrder = 5, HasDrilldown = true };
    public static readonly KpiInfo WinServer2022 = new() { Key = "WinServer2022", DisplayName = "Windows Server 2022", CategoryKey = "ComputersCategory", CssColor = "teal", SectionId = "winserver2022", SortOrder = 6, HasDrilldown = true };
    public static readonly KpiInfo WinServer2025 = new() { Key = "WinServer2025", DisplayName = "Windows Server 2025", CategoryKey = "ComputersCategory", CssColor = "green", SectionId = "winserver2025", SortOrder = 7, HasDrilldown = true };
    public static readonly KpiInfo ServerOther = new() { Key = "ServerOther", DisplayName = "Server (other)", CategoryKey = "ComputersCategory", CssColor = "slate", SectionId = "serverother", SortOrder = 8, HasDrilldown = true };
    public static readonly KpiInfo Win7 = new() { Key = "Win7", DisplayName = "Windows 7", CategoryKey = "ComputersCategory", CssColor = "red", SectionId = "win7", SortOrder = 9, HasDrilldown = true };
    public static readonly KpiInfo Win81 = new() { Key = "Win81", DisplayName = "Windows 8.1", CategoryKey = "ComputersCategory", CssColor = "red", SectionId = "win81", SortOrder = 10, HasDrilldown = true };
    public static readonly KpiInfo Win10_22H2 = new() { Key = "Win10_22H2", DisplayName = "Windows 10 22H2", CategoryKey = "ComputersCategory", CssColor = "orange", SectionId = "win1022h2", SortOrder = 11, HasDrilldown = true };
    public static readonly KpiInfo Win11_22H2 = new() { Key = "Win11_22H2", DisplayName = "Windows 11 22H2", CategoryKey = "ComputersCategory", CssColor = "blue", SectionId = "win1122h2", SortOrder = 12, HasDrilldown = true };
    public static readonly KpiInfo Win11_23H2 = new() { Key = "Win11_23H2", DisplayName = "Windows 11 23H2", CategoryKey = "ComputersCategory", CssColor = "teal", SectionId = "win1123h2", SortOrder = 13, HasDrilldown = true };
    public static readonly KpiInfo Win11Enterprise = new() { Key = "Win11Enterprise", DisplayName = "Windows 11 Enterprise", CategoryKey = "ComputersCategory", CssColor = "indigo", SectionId = "win11enterprise", SortOrder = 14, HasDrilldown = true };
    public static readonly KpiInfo Win11Pro = new() { Key = "Win11Pro", DisplayName = "Windows 11 Pro", CategoryKey = "ComputersCategory", CssColor = "purple", SectionId = "win11pro", SortOrder = 15, HasDrilldown = true };
    public static readonly KpiInfo ClientsOther = new() { Key = "ClientsOther", DisplayName = "Clients (other)", CategoryKey = "ComputersCategory", CssColor = "slate", SectionId = "clientsother", SortOrder = 16, HasDrilldown = true };
    public static readonly KpiInfo UnconstrainedComputers = new() { Key = "UnconstrainedComputers", DisplayName = "Unconstrained Delegation", CategoryKey = "ComputersCategory", CssColor = "red", SectionId = "unconstrainedcomputers", SortOrder = 17, HasDrilldown = true, IsRiskKpi = true };

    // Licensing KPIs
    public static readonly KpiInfo ManagedObjects = new() { Key = "ManagedObjects", DisplayName = "Managed Objects", CategoryKey = "Licensing", CssColor = "slate", SectionId = "managedobjects", SortOrder = 0, HasDrilldown = true, Searches = [new() { BaseDn = "CN=Managed Object Statistics,CN=Server Configuration,CN=Configuration", Filter = "(objectClass=edsManagedObjectStatisticsData)", Attributes = "name,edsaStatisticsCountXML" }] };

    public static readonly IReadOnlyList<KpiInfo> All =
    [
        ADUserAccounts, ADGroups, Computers,
        AdOverviewUsers, AdOverviewGroups, AdOverviewComputers,
        EntraOverviewUsers, EntraOverviewGroups,
        EntraEnabledUsers, EntraDisabledUsers, EntraNoManagerUser, EntraGuestUsers, EntraInternalUsers, EntraExternalUsers,
        EntraDistributionGroups, EntraDynamicDistributionGroups, EntraMicrosoft365Groups, EntraSecurityGroups, EntraEmptyGroups, EntraNoGroupOwner, EntraGuestContainingGroups, EntraPublicGroups, EntraOnPremSyncedGroups, EntraSingleOwnerGroups, EntraLargeGroups,
        ActiveRolesAdmins, Servers, Domains, AccessTemplateLinks, AccessTemplates, DynamicGroups, GroupFamilies, ManagedUnits, PolicyObjectLinks, PolicyObjects, VirtualAttributes, Workflows, ConfigDatabases, HistoryDatabases,
        NoGroupOwner, NoManagerUser, NoManagerServiceAccount, UserAccountLockedOut, EmptyGroups, NeverLoggedIn, ExpiredUsers, ReversibleEncryption,
        AccountOperators, Administrators, BackupOperators, DomainAdmins, ServerOperators, EnterpriseAdmins, SchemaAdmins,
        EnabledUsers, DisabledUsers, ExpiringUsers, PasswordNeverExpires,
        MustChangePassword, PasswordNotRequired, SmartCardRequired, CannotChangePassword,
        DeprovisionedUsers, SpnUserAccounts, StaleUsers, ServiceAccounts, GmsaServiceAccounts, SmsaServiceAccounts,
        DistributionGroups, DomainLocalGroups, GlobalGroups, MailEnabledSecurityGroups, SecurityGroups, UniversalGroups, CircularGroupNesting,
        AdminCount,
        Sites, SiteLinks, Subnets, OUs, DomainControllers,
        ComputerClients, ComputerServers, WinServer2008R2, WinServer2012R2, WinServer2016, WinServer2019, WinServer2022, WinServer2025, ServerOther,
        Win7, Win81, Win10_22H2, Win11_22H2, Win11_23H2, Win11Enterprise, Win11Pro, ClientsOther, UnconstrainedComputers,
        ManagedObjects
    ];

    public static IEnumerable<KpiInfo> ForCategory(string categoryKey) => All.Where(k => k.CategoryKey == categoryKey);

    /// <summary>
    /// KPI keys whose values are only meaningful once Entra group membership (the <c>member</c>
    /// and <c>edsaAzureGroupManagedBy</c> attributes) has been lazily loaded. Any snapshot,
    /// assessment, or exposure view that consumes these keys before membership completes will
    /// see provisional (typically zero) counts. Single source of truth for the shared staleness
    /// guard and for blocking membership-dependent assessments.
    /// </summary>
    public static readonly IReadOnlySet<string> EntraMembershipDependentKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "EntraEmptyGroups",
            "EntraNoGroupOwner",
            "EntraGuestContainingGroups",
            "EntraSingleOwnerGroups",
            "EntraLargeGroups"
        };

    public static IReadOnlyList<KpiInfo> ForCategory(CategoryInfo category)
    {
        var kpis = All.Where(k => k.CategoryKey == category.Key);

        if (category.IsRiskCategory)
        {
            // A "Governance and Risk" category aggregates KPIs flagged IsRiskKpi whose owning
            // category belongs to the same dashboard, in addition to any KPIs assigned to it directly.
            var riskKpis = All.Where(k => k.IsRiskKpi
                && CategoryInfo.FromKey(k.CategoryKey)?.DashboardKey == category.DashboardKey);
            kpis = kpis.Concat(riskKpis).DistinctBy(k => k.Key);
        }

        return category.KpiSortOrder switch
        {
            CategorySortOrder.AtoZ => kpis.OrderBy(k => k.Label, StringComparer.OrdinalIgnoreCase).ToList(),
            CategorySortOrder.ZtoA => kpis.OrderByDescending(k => k.Label, StringComparer.OrdinalIgnoreCase).ToList(),
            // Pinned KPIs (SortOrder < PinnedSortThreshold) lead in explicit SortOrder order;
            // the remaining KPIs follow sorted A-Z by label.
            CategorySortOrder.CustomThenAtoZ => kpis
                .OrderBy(k => k.SortOrder < PinnedSortThreshold ? 0 : 1)
                .ThenBy(k => k.SortOrder < PinnedSortThreshold ? k.SortOrder : 0)
                .ThenBy(k => k.SortOrder < PinnedSortThreshold ? string.Empty : k.Label, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            _ => kpis.OrderBy(k => k.SortOrder).ToList()
        };
    }

    /// <summary>
    /// KPIs with a <see cref="SortOrder"/> below this value are treated as "pinned" for the
    /// <see cref="CategorySortOrder.CustomThenAtoZ"/> ordering: they lead in explicit SortOrder
    /// order, and every other KPI is sorted A-Z after them.
    /// </summary>
    private const int PinnedSortThreshold = 100;
}

public class LoginModel
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class UserSettings
{
    public int AutoRefreshMinutes { get; set; }
    public KpiSettings KpiSettings { get; set; } = new();

    /// <summary>
    /// UI language / culture code (e.g. "en"). Defaults to English.
    /// </summary>
    public string Language { get; set; } = SupportedLanguage.DefaultCode;
}

/// <summary>
/// Metadata describing a language available in the UI language selector.
/// The flag is a static image under wwwroot/img/flags.
/// </summary>
public class SupportedLanguage
{
    public const string DefaultCode = "en";

    public string Code { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string FlagImage { get; init; } = string.Empty;

    /// <summary>
    /// Languages currently supported by the dashboard. English only for now;
    /// additional entries can be added here as translations become available.
    /// </summary>
    public static readonly IReadOnlyList<SupportedLanguage> All = new List<SupportedLanguage>
    {
        new() { Code = "en", DisplayName = "English", FlagImage = "img/flags/en.svg" },
        new() { Code = "fr", DisplayName = "Français", FlagImage = "img/flags/fr.svg" },
        new() { Code = "it", DisplayName = "Italiano", FlagImage = "img/flags/it.svg" },
        new() { Code = "es", DisplayName = "Español", FlagImage = "img/flags/es.svg" },
        new() { Code = "de", DisplayName = "Deutsch", FlagImage = "img/flags/de.svg" },
        new() { Code = "hu", DisplayName = "Magyar", FlagImage = "img/flags/hu.svg" }
    };
}

public class KpiSettings
{
    public bool OverviewEnabled { get; set; } = true;
    public bool ARConfigurationEnabled { get; set; } = true;
    public bool ADGovernanceEnabled { get; set; } = true;
    public bool EntraIDGovernanceEnabled { get; set; } = true;
    public bool EntraUserAccountsEnabled { get; set; } = true;
    public bool PrivilegedGroupsEnabled { get; set; } = true;
    public bool ADUserAccountsCategoryEnabled { get; set; } = true;
    public bool ADGroupsCategoryEnabled { get; set; } = true;
    public bool PrivilegedUsersEnabled { get; set; } = true;
    public bool LicensingEnabled { get; set; } = true;

    public bool DomainsEnabled { get; set; } = true;
    public bool ServersEnabled { get; set; } = true;
    public bool DynamicGroupsEnabled { get; set; } = true;
    public bool GroupFamiliesEnabled { get; set; } = true;
    public bool ManagedUnitsEnabled { get; set; } = true;
    public bool WorkflowsEnabled { get; set; } = true;
    public bool VirtualAttributesEnabled { get; set; } = true;
    public bool ConfigDatabasesEnabled { get; set; } = true;
    public bool HistoryDatabasesEnabled { get; set; } = true;
    public bool PolicyObjectsEnabled { get; set; } = true;
    public bool PolicyObjectLinksEnabled { get; set; } = true;
    public bool AccessTemplatesEnabled { get; set; } = true;
    public bool AccessTemplateLinksEnabled { get; set; } = true;
    public bool ManagedObjectsEnabled { get; set; } = true;

    public bool NoGroupOwnerEnabled { get; set; } = true;
    public bool NeverLoggedInEnabled { get; set; } = true;
    public bool NoManagerUserEnabled { get; set; } = true;
    public bool NoManagerServiceAccountEnabled { get; set; } = true;
    public bool ServiceAccountsEnabled { get; set; } = true;
    public bool GmsaServiceAccountsEnabled { get; set; } = true;
    public bool SmsaServiceAccountsEnabled { get; set; } = true;
    public bool ExpiredUsersEnabled { get; set; } = true;
    public bool ReversibleEncryptionEnabled { get; set; } = true;
    public bool UserAccountLockedOutEnabled { get; set; } = true;
    public bool EmptyGroupsEnabled { get; set; } = true;
    public bool CircularGroupNestingEnabled { get; set; } = true;
    public bool AccountOperatorsEnabled { get; set; } = true;
    public bool AdministratorsEnabled { get; set; } = true;
    public bool BackupOperatorsEnabled { get; set; } = true;
    public bool DomainAdminsEnabled { get; set; } = true;
    public bool ServerOperatorsEnabled { get; set; } = true;
    public bool EnterpriseAdminsEnabled { get; set; } = true;
    public bool SchemaAdminsEnabled { get; set; } = true;
    public bool ActiveRolesAdminsEnabled { get; set; } = true;
    public bool ADUserAccountsEnabled { get; set; } = true;
    public bool EnabledUsersEnabled { get; set; } = true;
    public bool DisabledUsersEnabled { get; set; } = true;
    public bool ExpiringUsersEnabled { get; set; } = true;
    public bool PasswordNeverExpiresEnabled { get; set; } = true;
    public bool MustChangePasswordEnabled { get; set; } = true;
    public bool PasswordNotRequiredEnabled { get; set; } = true;
    public bool SmartCardRequiredEnabled { get; set; } = true;
    public bool CannotChangePasswordEnabled { get; set; } = true;
    public bool NoKerberosPreauthEnabled { get; set; } = true;
    public bool UserReversibleEncryptionEnabled { get; set; } = true;
    public bool SensitiveCannotDelegateEnabled { get; set; } = true;
    public bool TrustedForDelegationEnabled { get; set; } = true;
    public bool UseDesEncryptionEnabled { get; set; } = true;
    public bool DeprovisionedUsersEnabled { get; set; } = true;
    public bool SpnUserAccountsEnabled { get; set; } = true;
    public bool StaleUsersEnabled { get; set; } = true;
    public bool ADGroupsEnabled { get; set; } = true;
    public bool ComputersEnabled { get; set; } = true;
    public bool DistributionGroupsEnabled { get; set; } = true;
    public bool DomainLocalGroupsEnabled { get; set; } = true;
    public bool GlobalGroupsEnabled { get; set; } = true;
    public bool MailEnabledSecurityGroupsEnabled { get; set; } = true;
    public bool SecurityGroupsEnabled { get; set; } = true;
    public bool UniversalGroupsEnabled { get; set; } = true;
    public bool AdminCountEnabled { get; set; } = true;
    public bool InfrastructureEnabled { get; set; } = true;
    public bool ComputersCategoryEnabled { get; set; } = true;
    public bool NHIsCategoryEnabled { get; set; } = true;
    public bool SitesEnabled { get; set; } = true;
    public bool SiteLinksEnabled { get; set; } = true;
    public bool SubnetsEnabled { get; set; } = true;
    public bool OUsEnabled { get; set; } = true;
    public bool DomainControllersEnabled { get; set; } = true;
    public bool UnconstrainedComputersEnabled { get; set; } = true;
    public bool EntraEnabledUsersEnabled { get; set; } = true;
    public bool EntraDisabledUsersEnabled { get; set; } = true;
    public bool EntraNoManagerUserEnabled { get; set; } = true;
    public bool EntraGuestUsersEnabled { get; set; } = true;
    public bool EntraInternalUsersEnabled { get; set; } = true;
    public bool EntraExternalUsersEnabled { get; set; } = true;
    public bool EntraGroupsEnabled { get; set; } = true;
    public bool EntraDistributionGroupsEnabled { get; set; } = true;
    public bool EntraDynamicDistributionGroupsEnabled { get; set; } = true;
    public bool EntraMicrosoft365GroupsEnabled { get; set; } = true;
    public bool EntraSecurityGroupsEnabled { get; set; } = true;
    public bool EntraEmptyGroupsEnabled { get; set; } = true;
    public bool EntraNoGroupOwnerEnabled { get; set; } = true;
    public bool EntraGuestContainingGroupsEnabled { get; set; } = true;
    public bool EntraPublicGroupsEnabled { get; set; } = true;
    public bool EntraOnPremSyncedGroupsEnabled { get; set; } = true;
    public bool EntraSingleOwnerGroupsEnabled { get; set; } = true;
    public bool EntraLargeGroupsEnabled { get; set; } = true;

    public bool IsCategoryEnabled(string categoryKey) => categoryKey switch
    {
        "Overview" => OverviewEnabled,
        "ARConfiguration" => ARConfigurationEnabled,
        "ADGovernance" => ADGovernanceEnabled,
        "EntraIDGovernance" => EntraIDGovernanceEnabled,
        "EntraUserAccounts" => EntraUserAccountsEnabled,
        "EntraGroups" => EntraGroupsEnabled,
        "PrivilegedGroups" => PrivilegedGroupsEnabled,
        "ADUserAccountsCategory" => ADUserAccountsCategoryEnabled,
        "ADGroupsCategory" => ADGroupsCategoryEnabled,
        "PrivilegedUsers" => PrivilegedUsersEnabled,
        "Infrastructure" => InfrastructureEnabled,
        "ComputersCategory" => ComputersCategoryEnabled,
        "NHIs" => NHIsCategoryEnabled,
        "Licensing" => LicensingEnabled,
        _ => true
    };

    public bool IsKpiEnabled(string category, string kpi)
    {
        if (!IsCategoryEnabled(category)) return false;

        return kpi switch
        {
            "Domains" => DomainsEnabled,
            "Servers" => ServersEnabled,
            "DynamicGroups" => DynamicGroupsEnabled,
            "GroupFamilies" => GroupFamiliesEnabled,
            "ManagedUnits" => ManagedUnitsEnabled,
            "Workflows" => WorkflowsEnabled,
            "VirtualAttributes" => VirtualAttributesEnabled,
            "ConfigDatabases" => ConfigDatabasesEnabled,
            "HistoryDatabases" => HistoryDatabasesEnabled,
            "PolicyObjects" => PolicyObjectsEnabled,
            "PolicyObjectLinks" => PolicyObjectLinksEnabled,
            "AccessTemplates" => AccessTemplatesEnabled,
            "AccessTemplateLinks" => AccessTemplateLinksEnabled,
            "ManagedObjects" => ManagedObjectsEnabled,
            "NoGroupOwner" => NoGroupOwnerEnabled,
            "NeverLoggedIn" => NeverLoggedInEnabled,
            "NoManagerUser" => NoManagerUserEnabled,
            "NoManagerServiceAccount" => NoManagerServiceAccountEnabled,
            "ServiceAccounts" => ServiceAccountsEnabled,
            "GmsaServiceAccounts" => GmsaServiceAccountsEnabled,
            "SmsaServiceAccounts" => SmsaServiceAccountsEnabled,
            "ExpiredUsers" => ExpiredUsersEnabled,
            "ReversibleEncryption" => ReversibleEncryptionEnabled,
            "UserAccountLockedOut" => UserAccountLockedOutEnabled,
            "EmptyGroups" => EmptyGroupsEnabled,
            "CircularGroupNesting" => CircularGroupNestingEnabled,
            "AccountOperators" => AccountOperatorsEnabled,
            "Administrators" => AdministratorsEnabled,
            "BackupOperators" => BackupOperatorsEnabled,
            "DomainAdmins" => DomainAdminsEnabled,
            "ServerOperators" => ServerOperatorsEnabled,
            "EnterpriseAdmins" => EnterpriseAdminsEnabled,
            "SchemaAdmins" => SchemaAdminsEnabled,
            "ActiveRolesAdmins" => ActiveRolesAdminsEnabled,
            "AdminCount" => AdminCountEnabled,
            "ADUserAccounts" => ADUserAccountsEnabled,
            "EnabledUsers" => EnabledUsersEnabled,
            "DisabledUsers" => DisabledUsersEnabled,
            "EntraEnabledUsers" => EntraEnabledUsersEnabled,
            "EntraDisabledUsers" => EntraDisabledUsersEnabled,
            "EntraNoManagerUser" => EntraNoManagerUserEnabled,
            "EntraGuestUsers" => EntraGuestUsersEnabled,
            "EntraInternalUsers" => EntraInternalUsersEnabled,
            "EntraExternalUsers" => EntraExternalUsersEnabled,
            "EntraDistributionGroups" => EntraDistributionGroupsEnabled,
            "EntraDynamicDistributionGroups" => EntraDynamicDistributionGroupsEnabled,
            "EntraMicrosoft365Groups" => EntraMicrosoft365GroupsEnabled,
            "EntraSecurityGroups" => EntraSecurityGroupsEnabled,
            "EntraEmptyGroups" => EntraEmptyGroupsEnabled,
            "EntraNoGroupOwner" => EntraNoGroupOwnerEnabled,
            "EntraGuestContainingGroups" => EntraGuestContainingGroupsEnabled,
            "EntraPublicGroups" => EntraPublicGroupsEnabled,
            "EntraOnPremSyncedGroups" => EntraOnPremSyncedGroupsEnabled,
            "EntraSingleOwnerGroups" => EntraSingleOwnerGroupsEnabled,
            "EntraLargeGroups" => EntraLargeGroupsEnabled,
            "ExpiringUsers" => ExpiringUsersEnabled,
            "PasswordNeverExpires" => PasswordNeverExpiresEnabled,
            "MustChangePassword" => MustChangePasswordEnabled,
            "PasswordNotRequired" => PasswordNotRequiredEnabled,
            "SmartCardRequired" => SmartCardRequiredEnabled,
            "CannotChangePassword" => CannotChangePasswordEnabled,
            "NoKerberosPreauth" => NoKerberosPreauthEnabled,
            "UserReversibleEncryption" => UserReversibleEncryptionEnabled,
            "SensitiveCannotDelegate" => SensitiveCannotDelegateEnabled,
            "TrustedForDelegation" => TrustedForDelegationEnabled,
            "UseDesEncryption" => UseDesEncryptionEnabled,
            "DeprovisionedUsers" => DeprovisionedUsersEnabled,
            "SpnUserAccounts" => SpnUserAccountsEnabled,
            "StaleUsers" => StaleUsersEnabled,
            "ADGroups" => ADGroupsEnabled,
            "Computers" => ComputersEnabled,
            "DistributionGroups" => DistributionGroupsEnabled,
            "DomainLocalGroups" => DomainLocalGroupsEnabled,
            "GlobalGroups" => GlobalGroupsEnabled,
            "MailEnabledSecurityGroups" => MailEnabledSecurityGroupsEnabled,
            "SecurityGroups" => SecurityGroupsEnabled,
            "UniversalGroups" => UniversalGroupsEnabled,
            "Sites" => SitesEnabled,
            "SiteLinks" => SiteLinksEnabled,
            "Subnets" => SubnetsEnabled,
            "OUs" => OUsEnabled,
            "DomainControllers" => DomainControllersEnabled,
            "UnconstrainedComputers" => UnconstrainedComputersEnabled,
            _ => true
        };
    }
}

public class DashboardSummary
{
    /// <summary>
    /// Member-count threshold at or above which an Entra group is treated as "large" for the
    /// Large Groups KPI. Populated from <c>ActiveRolesConfig.EntraLargeGroupMemberThreshold</c>
    /// when the summary is built.
    /// </summary>
    public int EntraLargeGroupMemberThreshold { get; set; } = 100;

    /// <summary>
    /// Shared staleness guard: true when this summary's Entra group membership has not finished
    /// loading, so the membership-dependent Entra Groups KPIs (Empty Groups, No Group Owner,
    /// Guest-Containing Groups, Single-Owner Groups, Large Groups) may be inaccurate. Consumed by
    /// the Snapshots, Assessments, and MITRE Exposure pages to warn before persisting or scoring
    /// a summary captured before lazy membership completed.
    /// </summary>
    public bool EntraMembershipDataPending => EntraTotals?.MembershipDataPending ?? false;

    /// <summary>Human-readable warning shown when <see cref="EntraMembershipDataPending"/> is true.</summary>
    public const string EntraMembershipPendingWarning =
        "Entra group membership is still loading. Group-membership KPIs (Empty Groups, No Group Owner, " +
        "Guest-Containing Groups, Single-Owner Groups, Large Groups) may be inaccurate until loading completes.";

    public ADUserAccountsSummary ADUserAccounts { get; set; } = new();
    public DomainSummary Domains { get; set; } = new();
    public ServerSummary Servers { get; set; } = new();
    public DynamicGroupSummary DynamicGroups { get; set; } = new();
    public GroupFamilySummary GroupFamilies { get; set; } = new();
    public ManagedUnitSummary ManagedUnits { get; set; } = new();
    public WorkflowSummary Workflows { get; set; } = new();
    public VirtualAttributeSummary VirtualAttributes { get; set; } = new();
    public ConfigDatabaseSummary ConfigDatabases { get; set; } = new();
    public HistoryDatabaseSummary HistoryDatabases { get; set; } = new();
    public PolicyObjectSummary PolicyObjects { get; set; } = new();
    public PolicyObjectLinkSummary PolicyObjectLinks { get; set; } = new();
    public AccessTemplateSummary AccessTemplates { get; set; } = new();
    public AccessTemplateLinkSummary AccessTemplateLinks { get; set; } = new();
    public ManagedObjectSummary ManagedObjects { get; set; } = new();
    public NoGroupOwnerSummary NoGroupOwner { get; set; } = new();
    public ADUserAccountDetailSummary NeverLoggedIn { get; set; } = new();
    public GovernanceKpiSummary NoManagerUser { get; set; } = new();
    public GovernanceKpiSummary NoManagerServiceAccount { get; set; } = new();
    public GovernanceKpiSummary ServiceAccounts { get; set; } = new();
    public GovernanceKpiSummary GmsaServiceAccounts { get; set; } = new();
    public GovernanceKpiSummary SmsaServiceAccounts { get; set; } = new();
    public ADUserAccountDetailSummary ExpiredUsers { get; set; } = new();
    public ADUserAccountDetailSummary PasswordNeverExpires { get; set; } = new();
    public GovernanceKpiSummary UserAccountLockedOut { get; set; } = new();
    public GovernanceKpiSummary ReversibleEncryption { get; set; } = new();
    public GovernanceKpiSummary EmptyGroups { get; set; } = new();
    public GovernanceKpiSummary CircularGroupNesting { get; set; } = new();
    public PrivilegedGroupSummary AccountOperators { get; set; } = new();
    public PrivilegedGroupSummary Administrators { get; set; } = new();
    public PrivilegedGroupSummary BackupOperators { get; set; } = new();
    public PrivilegedGroupSummary DomainAdmins { get; set; } = new();
    public PrivilegedGroupSummary ServerOperators { get; set; } = new();
    public PrivilegedGroupSummary EnterpriseAdmins { get; set; } = new();
    public PrivilegedGroupSummary SchemaAdmins { get; set; } = new();
    public PrivilegedGroupSummary ActiveRolesAdmins { get; set; } = new();
    public ADUserAccountDetailSummary AdminCount { get; set; } = new();
    public ADUserAccountDetailSummary EnabledUsers { get; set; } = new();
    public ADUserAccountDetailSummary DisabledUsers { get; set; } = new();
    public ADUserAccountDetailSummary MustChangePassword { get; set; } = new();
    public ADUserAccountDetailSummary PasswordNotRequired { get; set; } = new();
    public ADUserAccountDetailSummary SmartCardRequired { get; set; } = new();
    public ADUserAccountDetailSummary CannotChangePassword { get; set; } = new();
    public ADUserAccountDetailSummary NoKerberosPreauth { get; set; } = new();
    public ADUserAccountDetailSummary UserReversibleEncryption { get; set; } = new();
    public ADUserAccountDetailSummary SensitiveCannotDelegate { get; set; } = new();
    public ADUserAccountDetailSummary TrustedForDelegation { get; set; } = new();
    public ADUserAccountDetailSummary UseDesEncryption { get; set; } = new();
    public ADUserAccountDetailSummary DeprovisionedUsers { get; set; } = new();
    public ADUserAccountDetailSummary SpnUserAccounts { get; set; } = new();
    public ADUserAccountDetailSummary StaleUsers { get; set; } = new();
    public ExpiringUsersSummary ExpiringUsers { get; set; } = new();
    public ADGroupsSummary ADGroups { get; set; } = new();
    public ComputersSummary Computers { get; set; } = new();
    public EntraTotalsSummary EntraTotals { get; set; } = new();
    public ADGroupDetailSummary DistributionGroups { get; set; } = new();
    public ADGroupDetailSummary DomainLocalGroups { get; set; } = new();
    public ADGroupDetailSummary GlobalGroups { get; set; } = new();
    public ADGroupDetailSummary MailEnabledSecurityGroups { get; set; } = new();
    public ADGroupDetailSummary SecurityGroups { get; set; } = new();
    public ADGroupDetailSummary UniversalGroups { get; set; } = new();
    public GovernanceKpiSummary Sites { get; set; } = new();
    public GovernanceKpiSummary SiteLinks { get; set; } = new();
    public GovernanceKpiSummary Subnets { get; set; } = new();
    public GovernanceKpiSummary OUs { get; set; } = new();
    public DomainControllersSummary DomainControllers { get; set; } = new();
    public ComputerBreakdownSummary ComputerClients { get; set; } = new();
    public ComputerBreakdownSummary ComputerServers { get; set; } = new();
    public ComputerBreakdownSummary UnconstrainedComputers { get; set; } = new();
    public ComputerBreakdownSummary WinServer2008R2 { get; set; } = new();
    public ComputerBreakdownSummary WinServer2012R2 { get; set; } = new();
    public ComputerBreakdownSummary WinServer2016 { get; set; } = new();
    public ComputerBreakdownSummary WinServer2019 { get; set; } = new();
    public ComputerBreakdownSummary WinServer2022 { get; set; } = new();
    public ComputerBreakdownSummary WinServer2025 { get; set; } = new();
    public ComputerBreakdownSummary ServerOther { get; set; } = new();
    public ComputerBreakdownSummary Win7 { get; set; } = new();
    public ComputerBreakdownSummary Win81 { get; set; } = new();
    public ComputerBreakdownSummary Win10_22H2 { get; set; } = new();
    public ComputerBreakdownSummary Win11_22H2 { get; set; } = new();
    public ComputerBreakdownSummary Win11_23H2 { get; set; } = new();
    public ComputerBreakdownSummary Win11Enterprise { get; set; } = new();
    public ComputerBreakdownSummary Win11Pro { get; set; } = new();
    public ComputerBreakdownSummary ClientsOther { get; set; } = new();
    public ComputerBreakdownSummary StaleComputers { get; set; } = new();

    // Tier 2 security-health scalar signals.
    public SecurityHealthSummary KrbtgtPasswordAge { get; set; } = new();
    public SecurityHealthSummary WeakPasswordLength { get; set; } = new();
    public SecurityHealthSummary PasswordComplexityDisabled { get; set; } = new();
    public SecurityHealthSummary NoAccountLockout { get; set; } = new();
    public SecurityHealthSummary PasswordMaxAgeDays { get; set; } = new();

    /// <summary>
    /// Aggregated count of Entra group-like objects (Security, Microsoft 365,
    /// Distribution, and Dynamic Distribution groups) across all tenants.
    /// </summary>
    public int EntraGroupsTotal =>
        EntraTotals.CountFor(EntraObjectType.SecurityGroup)
        + EntraTotals.CountFor(EntraObjectType.Microsoft365Group)
        + EntraTotals.CountFor(EntraObjectType.DistributionGroup)
        + EntraTotals.CountFor(EntraObjectType.DynamicDistributionGroup);

    /// <summary>
    /// Per-tenant group counts (sum of the four Entra group object types) in discovered-tenant
    /// order, restricted to the effective/selected tenant set. Used for the Entra dashboard's
    /// "Groups by Source" chart so it breaks down by selected tenant.
    /// </summary>
    private IReadOnlyList<(string Tenant, int Count)> EntraGroupsByTenant()
    {
        var acc = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in EntraTotals.Tenants) acc[t] = 0;
        foreach (var type in new[]
                 {
                     EntraObjectType.SecurityGroup,
                     EntraObjectType.Microsoft365Group,
                     EntraObjectType.DistributionGroup,
                     EntraObjectType.DynamicDistributionGroup
                 })
        {
            foreach (var (tenant, count) in EntraTotals.CountForByTenant(type))
            {
                if (acc.ContainsKey(tenant)) acc[tenant] += count;
            }
        }
        return EntraTotals.Tenants.Select(t => (t, acc.TryGetValue(t, out var c) ? c : 0)).ToList();
    }

    /// <summary>
    /// The set of AD domains (NetBIOS names) available for segment filtering, derived from
    /// the raw items of the AD Overview summaries. This is the AD analogue of
    /// <see cref="EntraTotalsSummary.Tenants"/> and is the source list a domain multi-select
    /// binds to. Order is stable and duplicates/blank domains are removed.
    /// </summary>
    public IReadOnlyList<string> GetAdDomains()
    {
        var domains = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Collect(IEnumerable<JsonElement> items)
        {
            foreach (var item in items)
            {
                var domain = SegmentAttributes.DomainOf(item);
                if (!string.IsNullOrWhiteSpace(domain) && seen.Add(domain))
                    domains.Add(domain);
            }
        }

        if (ADUserAccounts.Error == null) Collect(ADUserAccounts.Items);
        if (ADGroups.Error == null) Collect(ADGroups.Items);
        if (Computers.Error == null) Collect(Computers.Items);

        domains.Sort(StringComparer.OrdinalIgnoreCase);
        return domains;
    }

    /// <summary>
    /// Groups the supplied AD Overview items by domain (NetBIOS name), ordered by the
    /// current available-domain list, returning zero for domains with no items. Used by the
    /// AD dashboard's source-split charts so they break down by selected domain, mirroring
    /// the Entra dashboard's per-tenant breakdown. On a null/errored summary returns [].
    /// </summary>
    private IReadOnlyList<(string Domain, int Count)> AdCountByDomain(IReadOnlyList<JsonElement>? items, Func<JsonElement, bool>? predicate = null)
    {
        if (items == null) return Array.Empty<(string, int)>();
        var source = predicate == null ? items : items.Where(predicate);
        var counts = source
            .Select(SegmentAttributes.DomainOf)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .GroupBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        // Order by the domains present in this reduced summary (post-filter these are the
        // effective/selected domains), matching how Entra orders by effective tenant.
        var ordered = counts.Keys.OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
        return ordered.Select(d => (d, counts[d])).ToList();
    }

    /// <summary>
    /// Applies an AD domain segment selection in place across the AD Overview summaries.
    /// This is the single server-side AD filter choke point: it resolves the selection
    /// against the available domains (empty ⇒ all), then reduces the user, group, and
    /// computer summaries to the effective domain set. Both live tiles and the per-segment
    /// export consume the reduced summaries, so the filtering rule lives in one place.
    /// Returns the resolved effective domains for callers that need to echo the selection.
    /// </summary>
    public IReadOnlyList<string> FilterByDomain(SegmentSelection selection)
    {
        var effective = selection.Resolve(GetAdDomains());
        var effectiveSet = new HashSet<string>(effective, StringComparer.OrdinalIgnoreCase);

        ADUserAccounts = ADUserAccounts.Filter(effectiveSet);
        ADGroups = ADGroups.Filter(effectiveSet);
        Computers = Computers.Filter(effectiveSet);

        return effective;
    }

    /// <summary>
    /// True when the applied AD domain selection resolves to at least one domain, i.e. the
    /// AD tile and AD dashboard section should be shown. Defaults to true until a filter is
    /// applied. Set by <see cref="ApplySegmentFilter"/>.
    /// </summary>
    public bool AdVisible { get; private set; } = true;

    /// <summary>
    /// True when the applied Entra tenant selection resolves to at least one tenant, i.e. the
    /// Entra tile and Entra dashboard section should be shown. Defaults to true until a filter
    /// is applied. Set by <see cref="ApplySegmentFilter"/>.
    /// </summary>
    public bool EntraVisible { get; private set; } = true;

    /// <summary>
    /// True when the viewer is permitted to see the Licensing dashboard, i.e. they can read
    /// <c>edsManagedObjectStatisticsData</c> objects (List Object + Read objectClass, or Read all
    /// properties). Defaults to true (admins / cache-cold direct queries see it); the per-user
    /// projection sets it from the Active Roles permission model. Used to hide the Licensing tile
    /// and dashboard for viewers without that delegated read access.
    /// </summary>
    public bool LicensingVisible { get; set; } = true;

    /// <summary>
    /// Applies both dimensions of a persisted <see cref="SegmentFilterState"/> to this
    /// summary: AD domains via <see cref="FilterByDomain"/> and Entra tenants via
    /// <see cref="EntraTotalsSummary.Filter"/>. This is the shared entry point the
    /// dashboard pages and the export controller both call, so filtering behaviour is
    /// identical for live rendering and export. It also records <see cref="AdVisible"/> and
    /// <see cref="EntraVisible"/> so callers can omit the tile / dashboard / export section
    /// when a dimension resolves to no segments.
    /// </summary>
    public void ApplySegmentFilter(SegmentFilterState filter)
    {
        if (filter == null)
            return;

        var effectiveDomains = FilterByDomain(filter.DomainSelection);
        EntraTotals = EntraTotals.Filter(filter.TenantSelection);

        AdVisible = effectiveDomains.Count > 0;
        EntraVisible = EntraTotals.Tenants.Count > 0;
    }

    /// <summary>Returns the count and error for any KPI by key, enabling generic tile rendering.</summary>
    public (int Count, string? Error) GetKpiResult(string kpiKey) => kpiKey switch
    {
        "ADUserAccounts" => GetCombinedResult(ADUserAccounts.TotalCount, ADUserAccounts.Error, EntraTotals.CountFor(EntraObjectType.User), EntraTotals.Error),
        "ADGroups" => GetCombinedResult(ADGroups.TotalCount, ADGroups.Error, EntraGroupsTotal, EntraTotals.Error),
        "Computers" => (Computers.TotalCount, Computers.Error),

        // Single-source Overview KPIs (AD-only) for the Active Directory dashboard Overview.
        "AdOverviewUsers" => (ADUserAccounts.TotalCount, ADUserAccounts.Error),
        "AdOverviewGroups" => (ADGroups.TotalCount, ADGroups.Error),
        "AdOverviewComputers" => (Computers.TotalCount, Computers.Error),

        // Single-source Overview KPIs (Entra-only) for the Entra ID dashboard Overview.
        "EntraOverviewUsers" => (EntraTotals.CountFor(EntraObjectType.User), EntraTotals.Error),
        "EntraOverviewGroups" => (EntraGroupsTotal, EntraTotals.Error),

        // Entra User Accounts KPIs (derived from the Entra user objects).
        "EntraEnabledUsers" => (EntraTotals.EntraUsers(enabled: true).TotalCount, EntraTotals.Error),
        "EntraDisabledUsers" => (EntraTotals.EntraUsers(enabled: false).TotalCount, EntraTotals.Error),
        "EntraNoManagerUser" => (EntraTotals.EntraUsers(noManager: true).TotalCount, EntraTotals.Error),
        "EntraGuestUsers" => (EntraTotals.CountFor(EntraObjectType.GuestUser), EntraTotals.Error),
        "EntraInternalUsers" => (EntraTotals.EntraUsersByOrigin(external: false).TotalCount, EntraTotals.Error),
        "EntraExternalUsers" => (EntraTotals.EntraUsersByOrigin(external: true).TotalCount, EntraTotals.Error),
        "EntraDistributionGroups" => (EntraTotals.CountFor(EntraObjectType.DistributionGroup), EntraTotals.Error),
        "EntraDynamicDistributionGroups" => (EntraTotals.CountFor(EntraObjectType.DynamicDistributionGroup), EntraTotals.Error),
        "EntraMicrosoft365Groups" => (EntraTotals.CountFor(EntraObjectType.Microsoft365Group), EntraTotals.Error),
        "EntraSecurityGroups" => (EntraTotals.CountFor(EntraObjectType.SecurityGroup), EntraTotals.Error),
        "EntraEmptyGroups" => (EntraTotals.EntraEmptyGroups().TotalCount, EntraTotals.Error),
        "EntraNoGroupOwner" => (EntraTotals.EntraNoGroupOwnerGroups().TotalCount, EntraTotals.Error),
        "EntraGuestContainingGroups" => (EntraTotals.EntraGuestContainingGroups().TotalCount, EntraTotals.Error),
        "EntraPublicGroups" => (EntraTotals.EntraPublicGroups().TotalCount, EntraTotals.Error),
        "EntraOnPremSyncedGroups" => (EntraTotals.EntraOnPremSyncedGroups().TotalCount, EntraTotals.Error),
        "EntraSingleOwnerGroups" => (EntraTotals.EntraSingleOwnerGroups().TotalCount, EntraTotals.Error),
        "EntraLargeGroups" => (EntraTotals.EntraLargeGroups(EntraLargeGroupMemberThreshold).TotalCount, EntraTotals.Error),

        "ActiveRolesAdmins" => (ActiveRolesAdmins.TotalCount, ActiveRolesAdmins.Error),
        "Servers" => (Servers.TotalCount, Servers.Error),
        "Domains" => (Domains.TotalCount, Domains.Error),
        "AccessTemplateLinks" => (AccessTemplateLinks.TotalCount, AccessTemplateLinks.Error),
        "AccessTemplates" => (AccessTemplates.TotalCount, AccessTemplates.Error),
        "DynamicGroups" => (DynamicGroups.TotalCount, DynamicGroups.Error),
        "GroupFamilies" => (GroupFamilies.TotalCount, GroupFamilies.Error),
        "ManagedUnits" => (ManagedUnits.TotalCount, ManagedUnits.Error),
        "PolicyObjectLinks" => (PolicyObjectLinks.TotalCount, PolicyObjectLinks.Error),
        "PolicyObjects" => (PolicyObjects.TotalCount, PolicyObjects.Error),
        "VirtualAttributes" => (VirtualAttributes.TotalCount, VirtualAttributes.Error),
        "ConfigDatabases" => (ConfigDatabases.TotalCount, ConfigDatabases.Error),
        "HistoryDatabases" => (HistoryDatabases.TotalCount, HistoryDatabases.Error),
        "Workflows" => (Workflows.TotalCount, Workflows.Error),
        "NoGroupOwner" => (NoGroupOwner.TotalCount, NoGroupOwner.Error),
        "NoManagerUser" => (NoManagerUser.TotalCount, NoManagerUser.Error),
        "NoManagerServiceAccount" => (NoManagerServiceAccount.TotalCount, NoManagerServiceAccount.Error),
        "ServiceAccounts" => (ServiceAccounts.TotalCount, ServiceAccounts.Error),
        "GmsaServiceAccounts" => (GmsaServiceAccounts.TotalCount, GmsaServiceAccounts.Error),
        "SmsaServiceAccounts" => (SmsaServiceAccounts.TotalCount, SmsaServiceAccounts.Error),
        "UserAccountLockedOut" => (UserAccountLockedOut.TotalCount, UserAccountLockedOut.Error),
        "EmptyGroups" => (EmptyGroups.TotalCount, EmptyGroups.Error),
        "CircularGroupNesting" => (CircularGroupNesting.TotalCount, CircularGroupNesting.Error),
        "NeverLoggedIn" => (NeverLoggedIn.TotalCount, NeverLoggedIn.Error),
        "ExpiredUsers" => (ExpiredUsers.TotalCount, ExpiredUsers.Error),
        "ReversibleEncryption" => (ReversibleEncryption.TotalCount, ReversibleEncryption.Error),
        "AccountOperators" => (AccountOperators.TotalCount, AccountOperators.Error),
        "Administrators" => (Administrators.TotalCount, Administrators.Error),
        "BackupOperators" => (BackupOperators.TotalCount, BackupOperators.Error),
        "DomainAdmins" => (DomainAdmins.TotalCount, DomainAdmins.Error),
        "ServerOperators" => (ServerOperators.TotalCount, ServerOperators.Error),
        "EnterpriseAdmins" => (EnterpriseAdmins.TotalCount, EnterpriseAdmins.Error),
        "SchemaAdmins" => (SchemaAdmins.TotalCount, SchemaAdmins.Error),
        "AdminCount" => (AdminCount.TotalCount, AdminCount.Error),
        "EnabledUsers" => (EnabledUsers.TotalCount, EnabledUsers.Error),
        "DisabledUsers" => (DisabledUsers.TotalCount, DisabledUsers.Error),
        "ExpiringUsers" => (ExpiringUsers.TotalCount, ExpiringUsers.Error),
        "PasswordNeverExpires" => (PasswordNeverExpires.TotalCount, PasswordNeverExpires.Error),
        "MustChangePassword" => (MustChangePassword.TotalCount, MustChangePassword.Error),
        "PasswordNotRequired" => (PasswordNotRequired.TotalCount, PasswordNotRequired.Error),
        "SmartCardRequired" => (SmartCardRequired.TotalCount, SmartCardRequired.Error),
        "CannotChangePassword" => (CannotChangePassword.TotalCount, CannotChangePassword.Error),
        "NoKerberosPreauth" => (NoKerberosPreauth.TotalCount, NoKerberosPreauth.Error),
        "UserReversibleEncryption" => (UserReversibleEncryption.TotalCount, UserReversibleEncryption.Error),
        "SensitiveCannotDelegate" => (SensitiveCannotDelegate.TotalCount, SensitiveCannotDelegate.Error),
        "TrustedForDelegation" => (TrustedForDelegation.TotalCount, TrustedForDelegation.Error),
        "UseDesEncryption" => (UseDesEncryption.TotalCount, UseDesEncryption.Error),
        "DeprovisionedUsers" => (DeprovisionedUsers.TotalCount, DeprovisionedUsers.Error),
        "SpnUserAccounts" => (SpnUserAccounts.TotalCount, SpnUserAccounts.Error),
        "StaleUsers" => (StaleUsers.TotalCount, StaleUsers.Error),
        "DistributionGroups" => (DistributionGroups.TotalCount, DistributionGroups.Error),
        "DomainLocalGroups" => (DomainLocalGroups.TotalCount, DomainLocalGroups.Error),
        "GlobalGroups" => (GlobalGroups.TotalCount, GlobalGroups.Error),
        "MailEnabledSecurityGroups" => (MailEnabledSecurityGroups.TotalCount, MailEnabledSecurityGroups.Error),
        "SecurityGroups" => (SecurityGroups.TotalCount, SecurityGroups.Error),
        "UniversalGroups" => (UniversalGroups.TotalCount, UniversalGroups.Error),
        "Sites" => (Sites.TotalCount, Sites.Error),
        "SiteLinks" => (SiteLinks.TotalCount, SiteLinks.Error),
        "Subnets" => (Subnets.TotalCount, Subnets.Error),
        "OUs" => (OUs.TotalCount, OUs.Error),
        "DomainControllers" => (DomainControllers.TotalCount, DomainControllers.Error),
        "UnconstrainedComputers" => (UnconstrainedComputers.TotalCount, UnconstrainedComputers.Error),
        "ComputerClients" => (ComputerClients.TotalCount, ComputerClients.Error),
        "ComputerServers" => (ComputerServers.TotalCount, ComputerServers.Error),
        "WinServer2008R2" => (WinServer2008R2.TotalCount, WinServer2008R2.Error),
        "WinServer2012R2" => (WinServer2012R2.TotalCount, WinServer2012R2.Error),
        "WinServer2016" => (WinServer2016.TotalCount, WinServer2016.Error),
        "WinServer2019" => (WinServer2019.TotalCount, WinServer2019.Error),
        "WinServer2022" => (WinServer2022.TotalCount, WinServer2022.Error),
        "WinServer2025" => (WinServer2025.TotalCount, WinServer2025.Error),
        "ServerOther" => (ServerOther.TotalCount, ServerOther.Error),
        "Win7" => (Win7.TotalCount, Win7.Error),
        "Win81" => (Win81.TotalCount, Win81.Error),
        "Win10_22H2" => (Win10_22H2.TotalCount, Win10_22H2.Error),
        "Win11_22H2" => (Win11_22H2.TotalCount, Win11_22H2.Error),
        "Win11_23H2" => (Win11_23H2.TotalCount, Win11_23H2.Error),
        "Win11Enterprise" => (Win11Enterprise.TotalCount, Win11Enterprise.Error),
        "Win11Pro" => (Win11Pro.TotalCount, Win11Pro.Error),
        "ClientsOther" => (ClientsOther.TotalCount, ClientsOther.Error),
        "StaleComputers" => (StaleComputers.TotalCount, StaleComputers.Error),
        "KrbtgtPasswordAgeDays" => (KrbtgtPasswordAge.Value, KrbtgtPasswordAge.Error),
        "WeakPasswordLength" => (WeakPasswordLength.Value, WeakPasswordLength.Error),
        "PasswordComplexityDisabled" => (PasswordComplexityDisabled.Value, PasswordComplexityDisabled.Error),
        "NoAccountLockout" => (NoAccountLockout.Value, NoAccountLockout.Error),
        "PasswordMaxAgeDays" => (PasswordMaxAgeDays.Value, PasswordMaxAgeDays.Error),
        "ManagedObjects" => (ManagedObjects.Error != null ? 0 : (ManagedObjects.DataPoints.Any() ? ManagedObjects.DataPoints.Last().Items.Sum(i => i.Count) : 0), ManagedObjects.Error),
        // Active Roles configuration hygiene KPIs derived from already-collected summaries.
        "DisabledWorkflows" => (Workflows.Error != null ? 0 : Workflows.Items.Count(w => !w.IsEnabled), Workflows.Error),
        "BroadDelegationLinks" => (AccessTemplateLinks.Error != null ? 0 : AccessTemplateLinks.Items.Count(l => !l.IsPredefined && IsBroadTrustee(l.Trustee)), AccessTemplateLinks.Error),
        // Unsupported / end-of-life operating system KPIs aggregated from the OS-breakdown summaries.
        "UnsupportedServerOs" => GetCombinedResult(WinServer2008R2.TotalCount, WinServer2008R2.Error, WinServer2012R2.TotalCount, WinServer2012R2.Error),
        "UnsupportedClientOs" => (
            (Win7.Error ?? Win81.Error ?? Win10_22H2.Error) != null ? 0 : Win7.TotalCount + Win81.TotalCount + Win10_22H2.TotalCount,
            Win7.Error ?? Win81.Error ?? Win10_22H2.Error),
        "UnsupportedOs" => (
            (WinServer2008R2.Error ?? WinServer2012R2.Error ?? Win7.Error ?? Win81.Error ?? Win10_22H2.Error) != null ? 0
                : WinServer2008R2.TotalCount + WinServer2012R2.TotalCount + Win7.TotalCount + Win81.TotalCount + Win10_22H2.TotalCount,
            WinServer2008R2.Error ?? WinServer2012R2.Error ?? Win7.Error ?? Win81.Error ?? Win10_22H2.Error),
        _ => (0, null)
    };

    /// <summary>
    /// True when a resolved Access Template Link trustee is a broad, everyone-style principal
    /// (Everyone, Authenticated Users, Domain Users). Delegating administrative Access Templates
    /// to such principals grants powerful rights to the whole population and should be flagged.
    /// </summary>
    private static bool IsBroadTrustee(string? trustee)
    {
        if (string.IsNullOrWhiteSpace(trustee)) return false;
        return trustee.Contains("Everyone", StringComparison.OrdinalIgnoreCase)
            || trustee.Contains("Authenticated Users", StringComparison.OrdinalIgnoreCase)
            || trustee.Contains("Domain Users", StringComparison.OrdinalIgnoreCase);
    }

    private static (int Count, string? Error) GetCombinedResult(int adCount, string? adError, int entraCount, string? entraError)
    {
        // If both fail, show error; if one succeeds, show partial count
        if (adError != null && entraError != null)
            return (0, adError);
        return (adCount + entraCount, null);
    }

    /// <summary>
    /// Returns the Active Directory vs Entra ID breakdown for a combined Overview KPI.
    /// Used to build source-based (AD/Entra) pie charts. Returns an empty list for
    /// KPIs that are not source-split.
    /// </summary>
    public IReadOnlyList<(string Source, int Count)> GetSourceSplit(string kpiKey) => kpiKey switch
    {
        "ADUserAccounts" =>
        [
            ("Active Directory", ADUserAccounts.Error != null ? 0 : ADUserAccounts.TotalCount),
            ("Entra ID", EntraTotals.Error != null ? 0 : EntraTotals.CountFor(EntraObjectType.User))
        ],
        "ADGroups" =>
        [
            ("Active Directory", ADGroups.Error != null ? 0 : ADGroups.TotalCount),
            ("Entra ID", EntraTotals.Error != null ? 0 : EntraGroupsTotal)
        ],
        "Computers" =>
        [
            ("Active Directory", Computers.Error != null ? 0 : Computers.TotalCount)
        ],

        // Single-source Overview splits: the AD dashboard breaks its Overview totals down
        // by selected domain (mirroring the Entra dashboard's per-tenant breakdown); the
        // Entra dashboard breaks down by selected tenant.
        "AdOverviewUsers" => ADUserAccounts.Error != null
            ? []
            : AdCountByDomain(ADUserAccounts.Items).Select(d => (d.Domain, d.Count)).ToList(),
        "AdOverviewGroups" => ADGroups.Error != null
            ? []
            : AdCountByDomain(ADGroups.Items,
                    i => !string.Equals(SegmentAttributes.AttrOf(i, "edsvaGFIsGroupFamily"), "TRUE", StringComparison.OrdinalIgnoreCase))
                .Select(d => (d.Domain, d.Count)).ToList(),
        "AdOverviewComputers" => Computers.Error != null
            ? []
            : AdCountByDomain(Computers.Items).Select(d => (d.Domain, d.Count)).ToList(),
        "EntraOverviewUsers" => EntraTotals.Error != null
            ? []
            : EntraTotals.CountForByTenant(EntraObjectType.User)
                .Select(t => (t.Tenant, t.Count))
                .ToList(),
        "EntraOverviewGroups" => EntraTotals.Error != null
            ? []
            : EntraGroupsByTenant().ToList(),
        _ => []
    };

    /// <summary>
    /// Returns a detail table (columns + rows) for a KPI, for use in exports.
    /// Only data columns are included; UI action/button columns are intentionally omitted.
    /// Returns null when the KPI has no row-based detail (e.g. pure count/breakdown tiles).
    /// </summary>
    public ReportTable? GetKpiDetailTable(string kpiKey)
    {
        // Strongly-typed AD user-account detail summaries (Name / Domain / Distinguished Name).
        ReportTable? UserDetail(ADUserAccountDetailSummary s) => new()
        {
            Columns = ["Name", "Domain", "Distinguished Name"],
            Rows = s.Items.Select(i => (IReadOnlyList<string>)[i.Name, i.Domain, i.Dn]).ToList(),
            Error = s.Error
        };

        // AD user detail including Description (Name / Distinguished Name / Description).
        ReportTable? UserDetailWithDescription(ADUserAccountDetailSummary s) => new()
        {
            Columns = ["Name", "Distinguished Name", "Description"],
            Rows = s.Items.Select(i => (IReadOnlyList<string>)[i.Name, i.Dn, i.Description]).ToList(),
            Error = s.Error
        };

        // Governance-style detail summaries (Name / Domain / Distinguished Name).
        ReportTable? GovDetail(GovernanceKpiSummary s) => new()
        {
            Columns = ["Name", "Domain", "Distinguished Name"],
            Rows = s.Items.Select(i => (IReadOnlyList<string>)[i.Name, i.Domain, i.Dn]).ToList(),
            Error = s.Error
        };

        // Privileged group membership (Name / Domain / Membership Type / Distinguished Name).
        ReportTable? PrivDetail(PrivilegedGroupSummary s) => new()
        {
            Columns = ["Name", "Domain", "Membership", "Distinguished Name"],
            Rows = s.Items.Select(i => (IReadOnlyList<string>)[i.Name, i.Domain, i.MembershipType, i.Dn]).ToList(),
            Error = s.Error
        };

        // AD group detail (Name / Direct / Indirect / Distinguished Name).
        ReportTable? GroupDetail(ADGroupDetailSummary s) => new()
        {
            Columns = ["Name", "Direct Members", "Indirect Members", "Distinguished Name"],
            Rows = s.Items.Select(i => (IReadOnlyList<string>)[i.Name, i.DirectMembers.ToString(), i.IndirectMembers.ToString(), i.Dn]).ToList(),
            Error = s.Error
        };

        // Entra user-account detail (Name / Tenant / Distinguished Name / Enabled).
        ReportTable? EntraUserDetail(EntraUserDetailSummary s) => new()
        {
            Columns = ["Name", "Tenant", "Distinguished Name", "Enabled"],
            Rows = s.Items.Select(i => (IReadOnlyList<string>)[i.Name, i.Tenant, i.Dn, i.Enabled.ToString()]).ToList(),
            Error = s.Error
        };

        // Entra external user-account detail, adds the resolved Home Tenant column.
        ReportTable? EntraExternalUserDetail(EntraUserDetailSummary s) => new()
        {
            Columns = ["Name", "Tenant", "Home Tenant", "Distinguished Name", "Enabled"],
            Rows = s.Items.Select(i => (IReadOnlyList<string>)[i.Name, i.Tenant, i.HomeTenant, i.Dn, i.Enabled.ToString()]).ToList(),
            Error = s.Error
        };

        // Entra group detail (Name / Tenant / Distinguished Name).
        ReportTable? EntraGroupDetail(EntraGroupDetailSummary s) => new()
        {
            Columns = ["Name", "Tenant", "Distinguished Name"],
            Rows = s.Items.Select(i => (IReadOnlyList<string>)[i.Name, i.Tenant, i.Dn]).ToList(),
            Error = s.Error
        };

        return kpiKey switch
        {
            // AD User Accounts category (derived detail lists)
            "EnabledUsers" => UserDetail(EnabledUsers),
            "DisabledUsers" => UserDetail(DisabledUsers),
            "MustChangePassword" => UserDetail(MustChangePassword),
            "PasswordNotRequired" => UserDetail(PasswordNotRequired),
            "SmartCardRequired" => UserDetail(SmartCardRequired),
            "CannotChangePassword" => UserDetail(CannotChangePassword),
            "NoKerberosPreauth" => UserDetail(NoKerberosPreauth),
            "UserReversibleEncryption" => UserDetail(UserReversibleEncryption),
            "SensitiveCannotDelegate" => UserDetail(SensitiveCannotDelegate),
            "TrustedForDelegation" => UserDetail(TrustedForDelegation),
            "SpnUserAccounts" => UserDetail(SpnUserAccounts),
            "StaleUsers" => UserDetail(StaleUsers),
            "UseDesEncryption" => UserDetail(UseDesEncryption),
            "DeprovisionedUsers" => UserDetailWithDescription(DeprovisionedUsers),
            "PasswordNeverExpires" => UserDetail(PasswordNeverExpires),
            "NeverLoggedIn" => UserDetail(NeverLoggedIn),
            "ExpiredUsers" => UserDetail(ExpiredUsers),
            "AdminCount" => UserDetail(AdminCount),
            "ExpiringUsers" => new ReportTable
            {
                Columns = ["Name", "Domain", "Expiry Date", "Days Until Expiry"],
                Rows = ExpiringUsers.Items.Select(i => (IReadOnlyList<string>)
                    [i.Name, i.Domain, i.ExpiryDate.ToString("yyyy-MM-dd"), i.DaysUntilExpiry.ToString()]).ToList(),
                Error = ExpiringUsers.Error
            },

            // Entra User Accounts category (derived from Entra user objects)
            "EntraEnabledUsers" => EntraUserDetail(EntraTotals.EntraUsers(enabled: true)),
            "EntraDisabledUsers" => EntraUserDetail(EntraTotals.EntraUsers(enabled: false)),
            "EntraNoManagerUser" => EntraUserDetail(EntraTotals.EntraUsers(noManager: true)),
            "EntraGuestUsers" => EntraUserDetail(EntraTotals.GuestUsers()),
            "EntraInternalUsers" => EntraUserDetail(EntraTotals.EntraUsersByOrigin(external: false)),
            "EntraExternalUsers" => EntraExternalUserDetail(EntraTotals.EntraUsersByOrigin(external: true)),

            // Entra Groups category (derived from Entra group objects)
            "EntraDistributionGroups" => EntraGroupDetail(EntraTotals.EntraObjectsOf(EntraObjectType.DistributionGroup)),
            "EntraDynamicDistributionGroups" => EntraGroupDetail(EntraTotals.EntraObjectsOf(EntraObjectType.DynamicDistributionGroup)),
            "EntraMicrosoft365Groups" => EntraGroupDetail(EntraTotals.EntraObjectsOf(EntraObjectType.Microsoft365Group)),
            "EntraSecurityGroups" => EntraGroupDetail(EntraTotals.EntraObjectsOf(EntraObjectType.SecurityGroup)),
            "EntraEmptyGroups" => EntraGroupDetail(EntraTotals.EntraEmptyGroups()),
            "EntraNoGroupOwner" => EntraGroupDetail(EntraTotals.EntraNoGroupOwnerGroups()),
            "EntraGuestContainingGroups" => EntraGroupDetail(EntraTotals.EntraGuestContainingGroups()),
            "EntraPublicGroups" => EntraGroupDetail(EntraTotals.EntraPublicGroups()),
            "EntraOnPremSyncedGroups" => EntraGroupDetail(EntraTotals.EntraOnPremSyncedGroups()),
            "EntraSingleOwnerGroups" => EntraGroupDetail(EntraTotals.EntraSingleOwnerGroups()),
            "EntraLargeGroups" => EntraGroupDetail(EntraTotals.EntraLargeGroups(EntraLargeGroupMemberThreshold)),

            // AD Governance category
            "NoManagerUser" => GovDetail(NoManagerUser),
            "NoManagerServiceAccount" => GovDetail(NoManagerServiceAccount),
            "ServiceAccounts" => GovDetail(ServiceAccounts),
            "GmsaServiceAccounts" => GovDetail(GmsaServiceAccounts),
            "SmsaServiceAccounts" => GovDetail(SmsaServiceAccounts),
            "UserAccountLockedOut" => GovDetail(UserAccountLockedOut),
            "ReversibleEncryption" => GovDetail(ReversibleEncryption),
            "EmptyGroups" => GovDetail(EmptyGroups),
            "CircularGroupNesting" => GovDetail(CircularGroupNesting),
            "Sites" => GovDetail(Sites),
            "SiteLinks" => GovDetail(SiteLinks),
            "Subnets" => GovDetail(Subnets),
            "OUs" => GovDetail(OUs),
            "NoGroupOwner" => new ReportTable
            {
                Columns = ["Name", "Distinguished Name"],
                Rows = NoGroupOwner.Items.Select(i => (IReadOnlyList<string>)[i.Name, i.Dn]).ToList(),
                Error = NoGroupOwner.Error
            },

            // Privileged groups
            "AccountOperators" => PrivDetail(AccountOperators),
            "Administrators" => PrivDetail(Administrators),
            "BackupOperators" => PrivDetail(BackupOperators),
            "DomainAdmins" => PrivDetail(DomainAdmins),
            "ServerOperators" => PrivDetail(ServerOperators),
            "EnterpriseAdmins" => PrivDetail(EnterpriseAdmins),
            "SchemaAdmins" => PrivDetail(SchemaAdmins),
            "ActiveRolesAdmins" => PrivDetail(ActiveRolesAdmins),

            // AD Groups category
            "DistributionGroups" => GroupDetail(DistributionGroups),
            "DomainLocalGroups" => GroupDetail(DomainLocalGroups),
            "GlobalGroups" => GroupDetail(GlobalGroups),
            "MailEnabledSecurityGroups" => GroupDetail(MailEnabledSecurityGroups),
            "SecurityGroups" => GroupDetail(SecurityGroups),
            "UniversalGroups" => GroupDetail(UniversalGroups),

            // Infrastructure
            "DomainControllers" => new ReportTable
            {
                Columns = ["Name", "Domain", "Site", "Distinguished Name"],
                Rows = DomainControllers.Items.Select(i => (IReadOnlyList<string>)[i.Name, i.Domain, i.SiteName, i.Dn]).ToList(),
                Error = DomainControllers.Error
            },

            // Computers category (OS breakdown detail)
            "ComputerClients" => ComputerDetail(ComputerClients),
            "UnconstrainedComputers" => ComputerDetail(UnconstrainedComputers),
            "ComputerServers" => ComputerDetail(ComputerServers),
            "WinServer2008R2" => ComputerDetail(WinServer2008R2),
            "WinServer2012R2" => ComputerDetail(WinServer2012R2),
            "WinServer2016" => ComputerDetail(WinServer2016),
            "WinServer2019" => ComputerDetail(WinServer2019),
            "WinServer2022" => ComputerDetail(WinServer2022),
            "WinServer2025" => ComputerDetail(WinServer2025),
            "ServerOther" => ComputerDetail(ServerOther),
            "Win7" => ComputerDetail(Win7),
            "Win81" => ComputerDetail(Win81),
            "Win10_22H2" => ComputerDetail(Win10_22H2),
            "Win11_22H2" => ComputerDetail(Win11_22H2),
            "Win11_23H2" => ComputerDetail(Win11_23H2),
            "Win11Enterprise" => ComputerDetail(Win11Enterprise),
            "Win11Pro" => ComputerDetail(Win11Pro),
            "ClientsOther" => ComputerDetail(ClientsOther),

            // AR Configuration
            "Domains" => new ReportTable
            {
                Columns = ["Name", "DNS Name", "Distinguished Name"],
                Rows = Domains.Items.Select(i => (IReadOnlyList<string>)[i.Name, i.DnsName, i.Dn]).ToList(),
                Error = Domains.Error
            },
            "Servers" => new ReportTable
            {
                Columns = ["Server Name", "Version"],
                Rows = Servers.Items.Select(i => (IReadOnlyList<string>)[i.ServerName, i.Version]).ToList(),
                Error = Servers.Error
            },
            "AccessTemplateLinks" => new ReportTable
            {
                Columns = ["Directory Object", "Access Template", "Trustee"],
                Rows = AccessTemplateLinks.Items.Select(i => (IReadOnlyList<string>)[i.DirectoryObject, i.AccessTemplate, i.Trustee]).ToList(),
                Error = AccessTemplateLinks.Error
            },
            "DynamicGroups" => new ReportTable
            {
                Columns = ["Name", "Distinguished Name"],
                Rows = DynamicGroups.Items.Select(i => (IReadOnlyList<string>)[i.Name, i.Dn]).ToList(),
                Error = DynamicGroups.Error
            },
            "PolicyObjectLinks" => new ReportTable
            {
                Columns = ["Name", "Distinguished Name"],
                Rows = PolicyObjectLinks.Items.Select(i => (IReadOnlyList<string>)[i.Name, i.Dn]).ToList(),
                Error = PolicyObjectLinks.Error
            },
            "ConfigDatabases" => new ReportTable
            {
                Columns = ["SQL Alias", "Database Name", "Database Type", "Replication Support", "Replication Role"],
                Rows = ConfigDatabases.Items.Select(i => (IReadOnlyList<string>)[i.SqlAlias, i.DatabaseName, i.DatabaseType, i.ReplicationSupport, i.ReplicationRole.ToString()]).ToList(),
                Error = ConfigDatabases.Error
            },
            "HistoryDatabases" => new ReportTable
            {
                Columns = ["SQL Alias", "Database Name", "Database Type", "Replication Role"],
                Rows = HistoryDatabases.Items.Select(i => (IReadOnlyList<string>)[i.SqlAlias, i.DatabaseName, i.DatabaseType, i.ReplicationRole.ToString()]).ToList(),
                Error = HistoryDatabases.Error
            },

            _ => null
        };
    }

    private static ReportTable ComputerDetail(ComputerBreakdownSummary s) => new()
    {
        Columns = ["Name", "Domain", "Operating System", "Distinguished Name"],
        Rows = s.Items.Select(i => (IReadOnlyList<string>)
            [i.Name, i.Domain, string.IsNullOrEmpty(i.FriendlyOSName) ? i.OperatingSystem : i.FriendlyOSName, i.Dn]).ToList(),
        Error = s.Error
    };
}

public class ADUserAccountsSummary
{
    public int TotalCount { get; set; }
    public List<JsonElement> Items { get; set; } = new();
    public string? Error { get; set; }

    /// <summary>Reduces this summary to items whose domain is in <paramref name="effectiveDomains"/>.</summary>
    public ADUserAccountsSummary Filter(ISet<string> effectiveDomains)
    {
        if (Error != null || effectiveDomains == null)
            return this;

        var items = Items.Where(i => effectiveDomains.Contains(SegmentAttributes.DomainOf(i))).ToList();
        return new ADUserAccountsSummary { TotalCount = items.Count, Items = items, Error = null };
    }
}

public class ADUserAccountDetailSummary
{
    public int TotalCount { get; set; }
    public List<ADUserAccountDetailInfo> Items { get; set; } = new();
    public string? Error { get; set; }
}

/// <summary>
/// A node in a nested group membership tree. Groups may be expanded lazily
/// (Children == null until loaded). Cycles and depth limits are flagged so the
/// UI can render them as terminal leaves instead of recursing forever.
/// </summary>
public class GroupMemberNode
{
    public string Name { get; set; } = string.Empty;
    public string Dn { get; set; } = string.Empty;
    public bool IsGroup { get; set; }
    public int Depth { get; set; }

    // Populated only for groups. Null = not yet expanded (lazy load).
    public List<GroupMemberNode>? Children { get; set; }

    // True when this DN already appears higher in the current branch (AD cycle).
    public bool CycleReference { get; set; }

    // Group had members but expansion was stopped by the depth cap.
    public bool DepthLimitReached { get; set; }
}

public class ADUserAccountDetailInfo : IPermissionScoped
{
    public string Name { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Dn { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string Description { get; set; } = string.Empty;

    [JsonIgnore] public IReadOnlyCollection<string> EffectiveLinkGuids { get; set; } = System.Array.Empty<string>();
    [JsonIgnore] public string ObjectClass { get; set; } = string.Empty;
}

public class ExpiringUsersSummary
{
    public int TotalCount { get; set; }
    public List<ExpiringUserInfo> Items { get; set; } = new();
    public string? Error { get; set; }
}

public class ExpiringUserInfo : IPermissionScoped
{
    public string Name { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Dn { get; set; } = string.Empty;
    public DateTime ExpiryDate { get; set; }
    public int DaysUntilExpiry { get; set; }

    [JsonIgnore] public IReadOnlyCollection<string> EffectiveLinkGuids { get; set; } = System.Array.Empty<string>();
    [JsonIgnore] public string ObjectClass { get; set; } = string.Empty;
}

public class ADGroupsSummary
{
    public int TotalCount { get; set; }
    public List<JsonElement> Items { get; set; } = new();
    public string? Error { get; set; }

    /// <summary>Reduces this summary to items whose domain is in <paramref name="effectiveDomains"/>.</summary>
    public ADGroupsSummary Filter(ISet<string> effectiveDomains)
    {
        if (Error != null || effectiveDomains == null)
            return this;

        var items = Items.Where(i => effectiveDomains.Contains(SegmentAttributes.DomainOf(i))).ToList();
        return new ADGroupsSummary { TotalCount = items.Count, Items = items, Error = null };
    }
}

public class ComputersSummary
{
    public int TotalCount { get; set; }
    public List<JsonElement> Items { get; set; } = new();
    public string? Error { get; set; }

    /// <summary>Reduces this summary to items whose domain is in <paramref name="effectiveDomains"/>.</summary>
    public ComputersSummary Filter(ISet<string> effectiveDomains)
    {
        if (Error != null || effectiveDomains == null)
            return this;

        var items = Items.Where(i => effectiveDomains.Contains(SegmentAttributes.DomainOf(i))).ToList();
        return new ComputersSummary { TotalCount = items.Count, Items = items, Error = null };
    }
}

public class ADGroupDetailSummary
{
    public int TotalCount { get; set; }
    public List<ADGroupDetailInfo> Items { get; set; } = new();
    public string? Error { get; set; }
}

public class ADGroupDetailInfo : IPermissionScoped
{
    public string Name { get; set; } = string.Empty;
    public string Dn { get; set; } = string.Empty;
    public int DirectMembers { get; set; }
    public int IndirectMembers { get; set; }

    [JsonIgnore] public IReadOnlyCollection<string> EffectiveLinkGuids { get; set; } = System.Array.Empty<string>();
    [JsonIgnore] public string ObjectClass { get; set; } = string.Empty;
}

public class DomainSummary
{
    public int TotalCount { get; set; }
    public List<DomainInfo> Items { get; set; } = new();
    public string? Error { get; set; }
}

public class DomainInfo : IPermissionScoped
{
    public string Name { get; set; } = string.Empty;
    public string DnsName { get; set; } = string.Empty;
    public bool UseOverride { get; set; } = false;
    public string Dn { get; set; } = string.Empty;
    public string Guid { get; set; } = string.Empty;

    [JsonIgnore] public IReadOnlyCollection<string> EffectiveLinkGuids { get; set; } = System.Array.Empty<string>();
    [JsonIgnore] public string ObjectClass { get; set; } = string.Empty;
}

public class ServerSummary
{
    public int TotalCount { get; set; }
    public List<ServerInfo> Items { get; set; } = new();
    public string? Error { get; set; }
}

public class ServerInfo : IPermissionScoped
{
    public string ServerName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Guid { get; set; } = string.Empty;

    [JsonIgnore] public IReadOnlyCollection<string> EffectiveLinkGuids { get; set; } = System.Array.Empty<string>();
    [JsonIgnore] public string ObjectClass { get; set; } = string.Empty;
}

public class DynamicGroupSummary
{
    public int TotalCount { get; set; }
    public List<DynamicGroupInfo> Items { get; set; } = new();
    public string? Error { get; set; }
}

public class DynamicGroupInfo
{
    public string Name { get; set; } = string.Empty;
    public string Dn { get; set; } = string.Empty;
    public string Guid { get; set; } = string.Empty;
}

public class GroupFamilySummary
{
    public int TotalCount { get; set; }
    public List<GroupFamilyInfo> Items { get; set; } = new();
    public string? Error { get; set; }
}

public class GroupFamilyInfo
{
    public string Name { get; set; } = string.Empty;
    public string Dn { get; set; } = string.Empty;
}

public class ManagedUnitSummary
{
    public int TotalCount { get; set; }
    public List<ManagedUnitInfo> Items { get; set; } = new();
    public string? Error { get; set; }
}

public class ManagedUnitInfo
{
    public string Name { get; set; } = string.Empty;
    public string Dn { get; set; } = string.Empty;
    public string Guid { get; set; } = string.Empty;
    public int RuleCount { get; set; }
}

public class WorkflowSummary
{
    public int TotalCount { get; set; }
    public List<WorkflowInfo> Items { get; set; } = new();
    public string? Error { get; set; }
}

public class WorkflowInfo
{
    public string Name { get; set; } = string.Empty;
    public string Dn { get; set; } = string.Empty;
    public string Guid { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = false;
    public bool IsAutomationWorkflow { get; set; } = false;
}

public class VirtualAttributeSummary
{
    public int TotalCount { get; set; }
    public List<VirtualAttributeInfo> Items { get; set; } = new();
    public string? Error { get; set; }
}

public class VirtualAttributeInfo
{
    public string Name { get; set; } = string.Empty;
    public string LdapDisplayName { get; set; } = string.Empty;
    public bool IsMultivalued { get; set; } = false;
    public string Guid { get; set; } = string.Empty;
}

/// <summary>Replication role of an Active Roles configuration or management history database.</summary>
public enum ReplicationRole
{
    Undefined = 0,
    Publisher = 1,
    Subscriber = 2,
    Standalone = 3
}

public class ConfigDatabaseSummary
{
    public int TotalCount { get; set; }
    public List<DatabaseInfo> Items { get; set; } = new();
    public string? Error { get; set; }
}

public class HistoryDatabaseSummary
{
    public int TotalCount { get; set; }
    public List<DatabaseInfo> Items { get; set; } = new();
    public string? Error { get; set; }
}

public class DatabaseInfo
{
    public string SqlAlias { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string DatabaseType { get; set; } = string.Empty;
    public string ReplicationSupport { get; set; } = string.Empty;
    public ReplicationRole ReplicationRole { get; set; } = ReplicationRole.Undefined;
}

public class PolicyObjectSummary
{
    public int TotalCount { get; set; }
    public List<PolicyObjectInfo> Items { get; set; } = new();
    public string? Error { get; set; }
}

public class PolicyObjectInfo
{
    public string Name { get; set; } = string.Empty;
    public string Dn { get; set; } = string.Empty;
    public string Guid { get; set; } = string.Empty;
    public int RuleCount { get; set; }
}

public class AccessTemplateSummary
{
    public int TotalCount { get; set; }
    public List<AccessTemplateInfo> Items { get; set; } = new();
    public string? Error { get; set; }
}

public class AccessTemplateInfo
{
    public string Name { get; set; } = string.Empty;
    public string Dn { get; set; } = string.Empty;
    public string Parent { get; set; } = string.Empty;
    public string Guid { get; set; } = string.Empty;
}

public class ManagedObjectSummary
{
    public int TotalCount { get; set; }
    public List<ManagedObjectDataPoint> DataPoints { get; set; } = new();
    public string? Error { get; set; }
}

public class ManagedObjectDataPoint
{
    public DateTime RunTime { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public List<ManagedObjectItem> Items { get; set; } = new();
}

public class ManagedObjectItem
{
    public string DisplayName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class NoGroupOwnerSummary
{
    public int TotalCount { get; set; }
    public List<NoGroupOwnerInfo> Items { get; set; } = new();
    public string? Error { get; set; }
}

public class NoGroupOwnerInfo : IPermissionScoped
{
    public string Name { get; set; } = string.Empty;
    public string Dn { get; set; } = string.Empty;
    public string Guid { get; set; } = string.Empty;

    [JsonIgnore] public IReadOnlyCollection<string> EffectiveLinkGuids { get; set; } = System.Array.Empty<string>();
    [JsonIgnore] public string ObjectClass { get; set; } = string.Empty;
}

public class GovernanceKpiSummary
{
    public int TotalCount { get; set; }
    public List<GovernanceKpiInfo> Items { get; set; } = new();
    public string? Error { get; set; }
}

public class GovernanceKpiInfo : IPermissionScoped
{
    public string Name { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Dn { get; set; } = string.Empty;
    public string Guid { get; set; } = string.Empty;

    [JsonIgnore] public IReadOnlyCollection<string> EffectiveLinkGuids { get; set; } = System.Array.Empty<string>();
    [JsonIgnore] public string ObjectClass { get; set; } = string.Empty;
}

public class PrivilegedGroupSummary
{
    public int TotalCount { get; set; }
    public List<PrivilegedGroupMemberInfo> Items { get; set; } = new();
    public string? Error { get; set; }

    // The privileged group object itself (used to launch the nested membership tree).
    public string? GroupDn { get; set; }
    public string? GroupName { get; set; }

    // True when at least one member is reached indirectly (via a nested group).
    public bool HasIndirectMembers => Items.Any(m => string.Equals(m.MembershipType, "Indirect", StringComparison.OrdinalIgnoreCase));
}

public class PrivilegedGroupMemberInfo : IPermissionScoped
{
    public string Name { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Dn { get; set; } = string.Empty;
    public string MembershipType { get; set; } = string.Empty; // "Direct" or "Indirect"
    public bool IsGroup { get; set; }

    [JsonIgnore] public IReadOnlyCollection<string> EffectiveLinkGuids { get; set; } = System.Array.Empty<string>();
    [JsonIgnore] public string ObjectClass { get; set; } = string.Empty;
}

public class AccessTemplateLinkSummary
{
    public int TotalCount { get; set; }
    public List<AccessTemplateLinkInfo> Items { get; set; } = new();
    public string? Error { get; set; }
}

public class AccessTemplateLinkInfo
{
    public string Name { get; set; } = string.Empty;
    public string Dn { get; set; } = string.Empty;
    public string Trustee { get; set; } = string.Empty;
    public string DirectoryObject { get; set; } = string.Empty;
    public string AccessTemplate { get; set; } = string.Empty;

    /// <summary>
    /// True when the link is an Active Roles predefined/system link
    /// (edsaIsPredefined=TRUE and edsaSystemObject=TRUE), rather than a user-defined delegation.
    /// </summary>
    public bool IsPredefined { get; set; }
}

public class PolicyObjectLinkSummary
{
    public int TotalCount { get; set; }
    public List<PolicyObjectLinkInfo> Items { get; set; } = new();
    public string? Error { get; set; }
}

public class PolicyObjectLinkInfo
{
    public string Name { get; set; } = string.Empty;
    public string Dn { get; set; } = string.Empty;
}

public class OverviewTotalsCache
{
    public ADUserAccountsSummary ADUserAccounts { get; set; } = new();
    public ADGroupsSummary ADGroups { get; set; } = new();
    public ComputersSummary Computers { get; set; } = new();
    public EntraTotalsSummary EntraTotals { get; set; } = new();
}

public class DomainControllersSummary
{
    public int TotalCount { get; set; }
    public List<DomainControllerInfo> Items { get; set; } = new();
    public string? Error { get; set; }
}

public class DomainControllerInfo : IPermissionScoped
{
    public string Name { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Dn { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;

    [JsonIgnore] public IReadOnlyCollection<string> EffectiveLinkGuids { get; set; } = System.Array.Empty<string>();
    [JsonIgnore] public string ObjectClass { get; set; } = string.Empty;
}

/// <summary>
/// The nine Azure/Entra object types
/// Each maps to a distinct Active Roles object class.
/// </summary>
public enum EntraObjectType
{
    Contact,
    GuestUser,
    User,
    DistributionGroup,
    DynamicDistributionGroup,
    Microsoft365Group,
    ResourceMailbox,
    SecurityGroup,
    SharedMailbox
}

/// <summary>Display names and Active Roles object classes for the Entra object types.</summary>
public static class EntraObjectTypeInfo
{
    public static IReadOnlyList<EntraObjectType> All { get; } = new[]
    {
        EntraObjectType.Contact,
        EntraObjectType.GuestUser,
        EntraObjectType.User,
        EntraObjectType.DistributionGroup,
        EntraObjectType.DynamicDistributionGroup,
        EntraObjectType.Microsoft365Group,
        EntraObjectType.ResourceMailbox,
        EntraObjectType.SecurityGroup,
        EntraObjectType.SharedMailbox
    };

    public static string DisplayName(EntraObjectType type) => type switch
    {
        EntraObjectType.Contact => "Contacts",
        EntraObjectType.GuestUser => "Guest Users",
        EntraObjectType.User => "Users",
        EntraObjectType.DistributionGroup => "Distribution Groups",
        EntraObjectType.DynamicDistributionGroup => "Dynamic Distribution Groups",
        EntraObjectType.Microsoft365Group => "Microsoft 365 Groups",
        EntraObjectType.ResourceMailbox => "Resource Mailboxes",
        EntraObjectType.SecurityGroup => "Security Groups",
        EntraObjectType.SharedMailbox => "Shared Mailboxes",
        _ => type.ToString()
    };

    /// <summary>
    /// The Active Roles object class returned by the REST API for this type.
    /// Note: distribution groups return the non-standard plural 'edsExoDistributionGroups'.
    /// Equipment mailboxes are not reliably represented, so resource mailboxes use the room class only.
    /// </summary>
    public static string ObjectClass(EntraObjectType type) => type switch
    {
        EntraObjectType.Contact => "edsAzureContact",
        EntraObjectType.GuestUser => "edsAzureGuestUser",
        EntraObjectType.User => "edsAzureUser",
        EntraObjectType.DistributionGroup => "edsExoDistributionGroups",
        EntraObjectType.DynamicDistributionGroup => "edsExoDynamicDistributionGroup",
        EntraObjectType.Microsoft365Group => "edsAzureO365Group",
        EntraObjectType.ResourceMailbox => "edsExoRoomMailbox",
        EntraObjectType.SecurityGroup => "edsAzureSecurityGroup",
        EntraObjectType.SharedMailbox => "edsExoSharedMailbox",
        _ => string.Empty
    };
}

/// <summary>A single Entra object with its resolved source tenant and object type.</summary>
public class EntraObjectInfo
{
    public string Name { get; set; } = string.Empty;
    public string Dn { get; set; } = string.Empty;
    public string Tenant { get; set; } = string.Empty;
    public EntraObjectType ObjectType { get; set; }
    public JsonElement Raw { get; set; }
}

/// <summary>A single Entra user-account detail row (Name / Tenant / DN / Enabled).</summary>
public class EntraUserDetailInfo
{
    public string Name { get; set; } = string.Empty;
    public string Tenant { get; set; } = string.Empty;
    public string Dn { get; set; } = string.Empty;
    public bool Enabled { get; set; }

    /// <summary>
    /// For external (guest / #EXT# ) users, the resolved home tenant derived from the
    /// portion of <c>edsaAzureUserPrincipalName</c> before the <c>#EXT#</c> marker.
    /// Empty for internal users.
    /// </summary>
    public string HomeTenant { get; set; } = string.Empty;
}

/// <summary>A derived detail list of Entra user accounts for a User Accounts KPI drilldown.</summary>
public class EntraUserDetailSummary
{
    public int TotalCount { get; set; }
    public List<EntraUserDetailInfo> Items { get; set; } = new();
    public string? Error { get; set; }
}

/// <summary>A single Entra group detail row (Name / Tenant / DN).</summary>
public class EntraGroupDetailInfo
{
    public string Name { get; set; } = string.Empty;
    public string Tenant { get; set; } = string.Empty;
    public string Dn { get; set; } = string.Empty;
}

/// <summary>A derived detail list of Entra group objects for a Groups KPI drilldown.</summary>
public class EntraGroupDetailSummary
{
    public int TotalCount { get; set; }
    public List<EntraGroupDetailInfo> Items { get; set; } = new();
    public string? Error { get; set; }
}

/// <summary>Per-object-type count within the aggregated Entra totals.</summary>
public class EntraObjectTypeCount
{
    public EntraObjectType ObjectType { get; set; }
    public string DisplayName => EntraObjectTypeInfo.DisplayName(ObjectType);
    public int TotalCount { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Represents the active segment filter for a dashboard (AD domains or Entra tenants).
/// Encodes the invariant agreed for filtering: the effective set is the caller's
/// selection intersected with the currently-available segments; if that intersection
/// is empty (nothing selected, or a stale selection that no longer matches any
/// available segment) it is coerced to "all available". This guarantees a minimum of
/// one effective segment whenever any segment exists, so a dashboard is never empty.
/// The same resolved set drives both live tiles and the per-segment export.
/// </summary>
public readonly struct SegmentSelection
{
    private readonly IReadOnlyCollection<string>? _selected;

    /// <summary>
    /// True when the caller explicitly cleared the selection (an empty set). This is
    /// distinct from an unset selection: an explicit-none selection resolves to zero
    /// segments (hiding the dependent tile / dashboard / export section), whereas an
    /// unset selection resolves to all available segments.
    /// </summary>
    private readonly bool _explicitNone;

    private SegmentSelection(IReadOnlyCollection<string>? selected, bool explicitNone = false)
    {
        _selected = selected;
        _explicitNone = explicitNone;
    }

    /// <summary>A selection that resolves to all available segments (the default when unset).</summary>
    public static SegmentSelection All => new(null);

    /// <summary>A selection that explicitly resolves to no segments.</summary>
    public static SegmentSelection None => new(null, explicitNone: true);

    /// <summary>Creates a selection from an explicit list of segment names (case-insensitive).</summary>
    public static SegmentSelection Of(IEnumerable<string>? selected)
    {
        if (selected == null)
            return All;

        var set = selected
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToArray();

        return set.Length == 0 ? All : new SegmentSelection(set);
    }

    /// <summary>
    /// Creates a selection from an explicit list of segment names, preserving an empty
    /// list as an explicit "none" (rather than collapsing it to "all"). Use this for the
    /// user-driven segment filter, where clearing every checkbox means "show nothing".
    /// A null list still means "unset ⇒ all".
    /// </summary>
    public static SegmentSelection ExplicitOf(IEnumerable<string>? selected)
    {
        if (selected == null)
            return All;

        var set = selected
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToArray();

        return set.Length == 0 ? None : new SegmentSelection(set);
    }

    /// <summary>
    /// Resolves this selection against the segments actually available. An unset selection
    /// resolves to all available segments; an explicit-none selection resolves to an empty
    /// set; otherwise the selection is intersected with the available segments. Returns a
    /// stable, de-duplicated set preserving the order of <paramref name="available"/>.
    /// </summary>
    public IReadOnlyList<string> Resolve(IEnumerable<string> available)
    {
        var availableList = available?
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

        if (_explicitNone)
            return new List<string>();

        if (_selected == null || _selected.Count == 0)
            return availableList;

        var selectedSet = new HashSet<string>(_selected, StringComparer.OrdinalIgnoreCase);
        return availableList.Where(selectedSet.Contains).ToList();
    }

    /// <summary>True when no explicit selection is held (i.e. resolves to all available).</summary>
    public bool IsAll => !_explicitNone && (_selected == null || _selected.Count == 0);

    /// <summary>True when the selection was explicitly cleared (i.e. resolves to no segments).</summary>
    public bool IsNone => _explicitNone;
}

/// <summary>
/// Helpers for reading the segment (domain) tag out of the raw <see cref="JsonElement"/>
/// items held by the AD Overview summaries. Mirrors the service-side attribute lookup
/// (direct property, or nested under an "attributes" object) so segmentation reads the
/// same value the rest of the app derives its per-item Domain from.
/// </summary>
public static class SegmentAttributes
{
    /// <summary>The Active Roles attribute carrying an AD object's NetBIOS domain name.</summary>
    public const string DomainAttribute = "edsaDomainNetbiosName";

    /// <summary>Reads the NetBIOS domain from a raw AD item; empty string when absent.</summary>
    public static string DomainOf(JsonElement element) => ReadAttr(element, DomainAttribute);

    /// <summary>Reads an arbitrary attribute from a raw AD item; empty string when absent.</summary>
    public static string AttrOf(JsonElement element, string name) => ReadAttr(element, name);

    /// <summary>
    /// Reads a multi-valued attribute from a raw item as individual string values.
    /// Handles both a JSON array of values and a single scalar value, and looks in the
    /// direct property or under a nested "attributes" object. Returns an empty sequence
    /// when the attribute is absent.
    /// </summary>
    public static IEnumerable<string> MultiAttrOf(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            yield break;

        JsonElement val;
        if (!element.TryGetProperty(name, out val))
        {
            if (!element.TryGetProperty("attributes", out var attrs)
                || attrs.ValueKind != JsonValueKind.Object
                || !attrs.TryGetProperty(name, out val))
            {
                yield break;
            }
        }

        if (val.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in val.EnumerateArray())
            {
                var s = item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText();
                if (!string.IsNullOrWhiteSpace(s))
                    yield return s;
            }
        }
        else if (val.ValueKind == JsonValueKind.String)
        {
            var s = val.GetString();
            if (!string.IsNullOrWhiteSpace(s))
                yield return s;
        }
    }

    private static string ReadAttr(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return string.Empty;

        if (element.TryGetProperty(name, out var val))
            return val.ValueKind == JsonValueKind.String ? val.GetString() ?? string.Empty : val.GetRawText();

        if (element.TryGetProperty("attributes", out var attrs)
            && attrs.ValueKind == JsonValueKind.Object
            && attrs.TryGetProperty(name, out var attrVal))
        {
            return attrVal.ValueKind == JsonValueKind.String ? attrVal.GetString() ?? string.Empty : attrVal.GetRawText();
        }

        return string.Empty;
    }

    /// <summary>
    /// The Active Roles attribute carrying, per object, the DNs of the Access Template Links
    /// that are effective on it. Each DN has the form
    /// <c>CN=&lt;LinkGuid&gt;,CN=AT Links,CN=Configuration</c>; the link GUID is the join key
    /// into <see cref="Services.ArPermissionModel.LinksByGuid"/>.
    /// </summary>
    public const string EffectiveLinksAttribute = "edsvaATLinksEffective";

    /// <summary>The Active Roles / AD attribute carrying an object's structural class.</summary>
    public const string ClassAttribute = "objectClass";

    /// <summary>
    /// Extracts the Access Template Link GUIDs effective on a raw AD item by reading
    /// <see cref="EffectiveLinksAttribute"/> and pulling the CN=&lt;guid&gt; component out of
    /// each link DN. Returns an empty sequence when the attribute is absent.
    /// </summary>
    public static IEnumerable<string> EffectiveLinksOf(JsonElement element)
    {
        foreach (var dn in MultiAttrOf(element, EffectiveLinksAttribute))
        {
            var guid = LinkGuidFromDn(dn);
            if (!string.IsNullOrEmpty(guid))
                yield return guid;
        }
    }

    /// <summary>
    /// Reads an object's structural class (last value of <see cref="ClassAttribute"/>, e.g.
    /// <c>user</c> / <c>group</c>), lowercased; empty string when absent.
    /// </summary>
    public static string ClassOf(JsonElement element)
    {
        string? last = null;
        foreach (var c in MultiAttrOf(element, ClassAttribute))
            last = c;
        return last?.ToLowerInvariant() ?? string.Empty;
    }

    /// <summary>
    /// Pulls the link GUID out of an <see cref="EffectiveLinksAttribute"/> DN
    /// (<c>CN=&lt;guid&gt;,CN=AT Links,...</c>). Returns empty when the DN is not in that shape.
    /// </summary>
    public static string LinkGuidFromDn(string? dn)
    {
        if (string.IsNullOrWhiteSpace(dn))
            return string.Empty;

        var trimmed = dn.TrimStart();
        const string cnPrefix = "CN=";
        if (!trimmed.StartsWith(cnPrefix, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var start = cnPrefix.Length;
        var comma = trimmed.IndexOf(',', start);
        var value = comma < 0 ? trimmed[start..] : trimmed[start..comma];
        return value.Trim().Trim('{', '}');
    }
}

/// <summary>
/// Implemented by dashboard item types that represent an underlying directory object and can be
/// permission-scoped per user. Carries the object's effective Access Template Link GUIDs and its
/// structural class so the shared, service-account-collected superset can be filtered per viewer
/// without re-querying Active Roles. Populated during collection (see the enrichment step); items
/// that are pure aggregates (no underlying object) do not implement this.
/// </summary>
public interface IPermissionScoped
{
    /// <summary>
    /// The Access Template Link GUIDs effective on this object (join key into
    /// <see cref="Services.ArPermissionModel.LinksByGuid"/>). Kept out of serialized output.
    /// </summary>
    IReadOnlyCollection<string> EffectiveLinkGuids { get; }

    /// <summary>The object's structural class (e.g. <c>user</c>, <c>group</c>), lowercased.</summary>
    string ObjectClass { get; }
}

/// <summary>
/// Aggregated totals for all Entra object types across every connected tenant.
/// <see cref="Items"/> retains each object's source tenant for later filtering/breakdowns.
/// </summary>
public class EntraTotalsSummary
{
    /// <summary>Grand total across all object types and tenants.</summary>
    public int TotalCount { get; set; }

    /// <summary>Tenant names discovered under the Azure configuration base.</summary>
    public List<string> Tenants { get; set; } = new();

    /// <summary>Per-object-type counts (aggregated across tenants).</summary>
    public List<EntraObjectTypeCount> ByObjectType { get; set; } = new();

    /// <summary>All collected objects, each tagged with its source tenant and object type.</summary>
    public List<EntraObjectInfo> Items { get; set; } = new();

    public string? Error { get; set; }

    /// <summary>
    /// True once group membership (the <c>member</c> attribute) and owner
    /// (<c>edsaAzureGroupManagedBy</c>) have been lazily loaded and merged into the group
    /// items' raw data. The membership-dependent KPIs (Empty Groups, No Group Owner,
    /// Guest-Containing Groups) are only meaningful when this is true.
    /// </summary>
    public bool MembershipLoaded { get; set; }

    /// <summary>
    /// Number of group items whose membership has already been lazily loaded (across one or
    /// more batches). Persisted in session so that navigating between dashboard pages resumes
    /// batched loading from this offset instead of restarting from the full group count.
    /// </summary>
    public int MembershipLoadedCount { get; set; }

    public int CountFor(EntraObjectType type) =>
        ByObjectType.FirstOrDefault(c => c.ObjectType == type)?.TotalCount ?? 0;

    /// <summary>
    /// True when the collected set contains at least one Entra group object of any type. Used
    /// to decide whether the membership-dependent KPIs (and the "membership still loading"
    /// staleness guard) are relevant at all.
    /// </summary>
    public bool HasGroupObjects =>
        Items.Any(i => i.ObjectType is EntraObjectType.DistributionGroup
            or EntraObjectType.DynamicDistributionGroup
            or EntraObjectType.Microsoft365Group
            or EntraObjectType.SecurityGroup);

    /// <summary>
    /// True when there are Entra group objects but their lazily-loaded membership/owner data
    /// has not been merged in yet. While this is true, the membership-dependent Entra Groups
    /// KPIs (Empty Groups, No Group Owner, Guest-Containing Groups, Single-Owner Groups, Large
    /// Groups) report provisional (typically zero) counts and should not be treated as final by
    /// snapshots, assessments, or the MITRE exposure view.
    /// </summary>
    public bool MembershipDataPending => HasGroupObjects && !MembershipLoaded;


    /// <summary>
    /// Per-tenant counts for a given object type, in discovered-tenant order. Only tenants
    /// in <see cref="Tenants"/> (the effective/selected set after filtering) are returned,
    /// so the Entra dashboard's source-split charts break down by selected tenant.
    /// </summary>
    /// <summary>
    /// Reads the account-enabled state for an Entra user from its raw attributes.
    /// The Active Roles attribute <c>edsaAzureUserAccountEnabled</c> is a boolean-like
    /// string ("TRUE"/"FALSE"); an account is treated as enabled unless explicitly FALSE.
    /// </summary>
    private static bool IsEntraUserEnabled(EntraObjectInfo info)
    {
        var raw = SegmentAttributes.AttrOf(info.Raw, "edsaAzureUserAccountEnabled");
        return !string.Equals(raw, "FALSE", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase)
            && raw != "0";
    }

    /// <summary>
    /// Projects the Entra <see cref="EntraObjectType.User"/> objects into user-account
    /// detail rows (Name / Tenant / DN / Enabled) for the User Accounts category KPIs and
    /// drilldowns. When <paramref name="enabled"/> is non-null the result is filtered to
    /// enabled (true) or disabled (false) accounts; when <paramref name="noManager"/> is
    /// true only users without a <c>manager</c> value are returned.
    /// </summary>
    public EntraUserDetailSummary EntraUsers(bool? enabled = null, bool noManager = false)
    {
        if (Error != null)
            return new EntraUserDetailSummary { Error = Error };

        var users = Items.Where(i => i.ObjectType == EntraObjectType.User);
        var rows = new List<EntraUserDetailInfo>();
        foreach (var u in users)
        {
            var isEnabled = IsEntraUserEnabled(u);
            if (enabled.HasValue && isEnabled != enabled.Value) continue;

            if (noManager)
            {
                var manager = SegmentAttributes.AttrOf(u.Raw, "manager");
                if (!string.IsNullOrWhiteSpace(manager)) continue;
            }

            rows.Add(new EntraUserDetailInfo
            {
                Name = u.Name,
                Tenant = u.Tenant,
                Dn = u.Dn,
                Enabled = isEnabled
            });
        }

        return new EntraUserDetailSummary { Items = rows, TotalCount = rows.Count };
    }

    /// <summary>
    /// Projects the Entra <see cref="EntraObjectType.GuestUser"/> objects into user-account
    /// detail rows (Name / Tenant / DN / Enabled) for the Guest Users KPI and its drilldown.
    /// </summary>
    public EntraUserDetailSummary GuestUsers()
    {
        if (Error != null)
            return new EntraUserDetailSummary { Error = Error };

        var rows = Items
            .Where(i => i.ObjectType == EntraObjectType.GuestUser)
            .Select(u => new EntraUserDetailInfo
            {
                Name = u.Name,
                Tenant = u.Tenant,
                Dn = u.Dn,
                Enabled = IsEntraUserEnabled(u)
            })
            .ToList();

        return new EntraUserDetailSummary { Items = rows, TotalCount = rows.Count };
    }

    /// <summary>
    /// Projects the collected Entra objects of a given <paramref name="type"/> into simple
    /// detail rows (Name / Tenant / DN) for a Groups category KPI drilldown.
    /// </summary>
    public EntraGroupDetailSummary EntraObjectsOf(EntraObjectType type)
    {
        if (Error != null)
            return new EntraGroupDetailSummary { Error = Error };

        var rows = Items
            .Where(i => i.ObjectType == type)
            .Select(o => new EntraGroupDetailInfo
            {
                Name = o.Name,
                Tenant = o.Tenant,
                Dn = o.Dn
            })
            .ToList();

        return new EntraGroupDetailSummary { Items = rows, TotalCount = rows.Count };
    }

    /// <summary>
    /// Projects Entra group objects (across all four group types) that have no direct
    /// members, using the Active Roles <c>member</c> attribute. An absent or empty
    /// <c>member</c> value indicates an empty group. Backs the Empty Groups hygiene KPI
    /// drilldown for the Entra Groups category.
    /// </summary>
    public EntraGroupDetailSummary EntraEmptyGroups()
    {
        if (Error != null)
            return new EntraGroupDetailSummary { Error = Error };

        var rows = Items
            .Where(i => i.ObjectType is EntraObjectType.DistributionGroup
                or EntraObjectType.DynamicDistributionGroup
                or EntraObjectType.Microsoft365Group
                or EntraObjectType.SecurityGroup)
            .Where(i => string.IsNullOrWhiteSpace(SegmentAttributes.AttrOf(i.Raw, "member")))
            .Select(o => new EntraGroupDetailInfo
            {
                Name = o.Name,
                Tenant = o.Tenant,
                Dn = o.Dn
            })
            .ToList();

        return new EntraGroupDetailSummary { Items = rows, TotalCount = rows.Count };
    }

    /// <summary>
    /// Projects Entra group objects (across all four group types) that have no owner,
    /// using the Active Roles <c>edsaAzureGroupManagedBy</c> multi-valued owner attribute.
    /// An absent or empty value indicates the group has no assigned owner. Backs the
    /// No Group Owner hygiene KPI drilldown for the Entra Groups category.
    /// </summary>
    public EntraGroupDetailSummary EntraNoGroupOwnerGroups()
    {
        if (Error != null)
            return new EntraGroupDetailSummary { Error = Error };

        var rows = Items
            .Where(i => i.ObjectType is EntraObjectType.DistributionGroup
                or EntraObjectType.DynamicDistributionGroup
                or EntraObjectType.Microsoft365Group
                or EntraObjectType.SecurityGroup)
            .Where(i => string.IsNullOrWhiteSpace(SegmentAttributes.AttrOf(i.Raw, "edsaAzureGroupManagedBy")))
            .Select(o => new EntraGroupDetailInfo
            {
                Name = o.Name,
                Tenant = o.Tenant,
                Dn = o.Dn
            })
            .ToList();

        return new EntraGroupDetailSummary { Items = rows, TotalCount = rows.Count };
    }

    /// <summary>
    /// Projects Entra group objects (across all four group types) that contain at least one
    /// guest (B2B) member. Guest membership is determined by cross-referencing each group's
    /// <c>member</c> DNs against the set of collected guest-user DNs, so no per-member
    /// attribute lookup is required. Backs the Guest-Containing Groups hygiene KPI drilldown.
    /// </summary>
    public EntraGroupDetailSummary EntraGuestContainingGroups()
    {
        if (Error != null)
            return new EntraGroupDetailSummary { Error = Error };

        var guestDns = Items
            .Where(i => i.ObjectType == EntraObjectType.GuestUser)
            .Select(i => i.Dn)
            .Where(dn => !string.IsNullOrWhiteSpace(dn))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = Items
            .Where(i => i.ObjectType is EntraObjectType.DistributionGroup
                or EntraObjectType.DynamicDistributionGroup
                or EntraObjectType.Microsoft365Group
                or EntraObjectType.SecurityGroup)
            .Where(i => SegmentAttributes.MultiAttrOf(i.Raw, "member")
                .Any(dn => guestDns.Contains(dn)))
            .Select(o => new EntraGroupDetailInfo
            {
                Name = o.Name,
                Tenant = o.Tenant,
                Dn = o.Dn
            })
            .ToList();

        return new EntraGroupDetailSummary { Items = rows, TotalCount = rows.Count };
    }

    /// <summary>
    /// Projects Microsoft 365 group objects whose <c>visibility</c> is <c>Public</c>.
    /// Public M365 groups allow any member to join and see content, so they are surfaced
    /// as an external-exposure hygiene signal. Uses the eagerly-loaded <c>visibility</c>
    /// attribute (no membership load required).
    /// </summary>
    public EntraGroupDetailSummary EntraPublicGroups()
    {
        if (Error != null)
            return new EntraGroupDetailSummary { Error = Error };

        var rows = Items
            .Where(i => i.ObjectType == EntraObjectType.Microsoft365Group)
            .Where(i => string.Equals(
                SegmentAttributes.AttrOf(i.Raw, "visibility"), "Public", StringComparison.OrdinalIgnoreCase))
            .Select(o => new EntraGroupDetailInfo
            {
                Name = o.Name,
                Tenant = o.Tenant,
                Dn = o.Dn
            })
            .ToList();

        return new EntraGroupDetailSummary { Items = rows, TotalCount = rows.Count };
    }

    /// <summary>
    /// Projects Entra group objects synchronized from on-premises Active Directory, using
    /// the eagerly-loaded <c>edsvaOnPremisesSyncEnabled</c> attribute. On-prem-synced groups
    /// cannot have their membership managed in the cloud, so surfacing them aids hybrid
    /// governance. No membership load required.
    /// </summary>
    public EntraGroupDetailSummary EntraOnPremSyncedGroups()
    {
        if (Error != null)
            return new EntraGroupDetailSummary { Error = Error };

        var rows = Items
            .Where(i => i.ObjectType is EntraObjectType.DistributionGroup
                or EntraObjectType.DynamicDistributionGroup
                or EntraObjectType.Microsoft365Group
                or EntraObjectType.SecurityGroup)
            .Where(i =>
            {
                var v = SegmentAttributes.AttrOf(i.Raw, "edsvaOnPremisesSyncEnabled");
                return string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) || v == "1";
            })
            .Select(o => new EntraGroupDetailInfo
            {
                Name = o.Name,
                Tenant = o.Tenant,
                Dn = o.Dn
            })
            .ToList();

        return new EntraGroupDetailSummary { Items = rows, TotalCount = rows.Count };
    }

    /// <summary>
    /// Projects Entra group objects (across all four group types) that have exactly one
    /// owner in the <c>edsaAzureGroupManagedBy</c> multi-valued owner attribute. A single
    /// owner is an orphaning risk (the group becomes unmanaged if that owner leaves).
    /// Depends on the lazily-loaded owner attribute.
    /// </summary>
    public EntraGroupDetailSummary EntraSingleOwnerGroups()
    {
        if (Error != null)
            return new EntraGroupDetailSummary { Error = Error };

        var rows = Items
            .Where(i => i.ObjectType is EntraObjectType.DistributionGroup
                or EntraObjectType.DynamicDistributionGroup
                or EntraObjectType.Microsoft365Group
                or EntraObjectType.SecurityGroup)
            .Where(i => SegmentAttributes.MultiAttrOf(i.Raw, "edsaAzureGroupManagedBy").Count() == 1)
            .Select(o => new EntraGroupDetailInfo
            {
                Name = o.Name,
                Tenant = o.Tenant,
                Dn = o.Dn
            })
            .ToList();

        return new EntraGroupDetailSummary { Items = rows, TotalCount = rows.Count };
    }

    /// <summary>
    /// Projects Entra group objects (across all four group types) whose direct member count
    /// (the lazily-loaded <c>member</c> attribute) meets or exceeds <paramref name="threshold"/>.
    /// Oversized groups have a large access blast radius and are access-review candidates.
    /// </summary>
    public EntraGroupDetailSummary EntraLargeGroups(int threshold)
    {
        if (Error != null)
            return new EntraGroupDetailSummary { Error = Error };

        if (threshold < 1) threshold = 1;

        var rows = Items
            .Where(i => i.ObjectType is EntraObjectType.DistributionGroup
                or EntraObjectType.DynamicDistributionGroup
                or EntraObjectType.Microsoft365Group
                or EntraObjectType.SecurityGroup)
            .Where(i => SegmentAttributes.MultiAttrOf(i.Raw, "member").Count() >= threshold)
            .Select(o => new EntraGroupDetailInfo
            {
                Name = o.Name,
                Tenant = o.Tenant,
                Dn = o.Dn
            })
            .ToList();

        return new EntraGroupDetailSummary { Items = rows, TotalCount = rows.Count };
    }

    /// expose a reliable userType flag, so this is derived from the presence of the
    /// <c>#EXT#</c> marker in <c>edsaAzureUserPrincipalName</c>.
    /// </summary>
    private static bool IsExternalUser(EntraObjectInfo info)
    {
        var upn = SegmentAttributes.AttrOf(info.Raw, "edsaAzureUserPrincipalName");
        return !string.IsNullOrEmpty(upn)
            && upn.Contains("#EXT#", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves the home tenant of an external user from the portion of
    /// <c>edsaAzureUserPrincipalName</c> before the <c>#EXT#</c> marker, e.g.
    /// <c>dane_barentine.com#EXT#@mistyvalley.onmicrosoft.com</c> resolves to
    /// <c>barentine.com</c>. Returns an empty string when the value cannot be parsed.
    /// </summary>
    private static string HomeTenantOf(EntraObjectInfo info)
    {
        var upn = SegmentAttributes.AttrOf(info.Raw, "edsaAzureUserPrincipalName");
        if (string.IsNullOrEmpty(upn))
            return string.Empty;

        var extIndex = upn.IndexOf("#EXT#", StringComparison.OrdinalIgnoreCase);
        if (extIndex < 0)
            return string.Empty;

        var local = upn.Substring(0, extIndex);
        var lastUnderscore = local.LastIndexOf('_');
        return lastUnderscore >= 0 && lastUnderscore < local.Length - 1
            ? local.Substring(lastUnderscore + 1)
            : local;
    }

    /// <summary>
    /// Projects the Entra user-account objects into detail rows filtered to internal
    /// (<paramref name="external"/> = false) or external (<paramref name="external"/> = true)
    /// accounts. From a dashboard perspective an account is external when its
    /// <c>edsaAzureUserPrincipalName</c> contains the <c>#EXT#</c> marker, regardless of the
    /// Active Roles object class (both <see cref="EntraObjectType.User"/> and
    /// <see cref="EntraObjectType.GuestUser"/> are considered) or the parent container.
    /// External rows include the resolved <see cref="EntraUserDetailInfo.HomeTenant"/>.
    /// </summary>
    public EntraUserDetailSummary EntraUsersByOrigin(bool external)
    {
        if (Error != null)
            return new EntraUserDetailSummary { Error = Error };

        var rows = Items
            .Where(i => (i.ObjectType == EntraObjectType.User || i.ObjectType == EntraObjectType.GuestUser)
                        && IsExternalUser(i) == external)
            .Select(u => new EntraUserDetailInfo
            {
                Name = u.Name,
                Tenant = u.Tenant,
                Dn = u.Dn,
                Enabled = IsEntraUserEnabled(u),
                HomeTenant = external ? HomeTenantOf(u) : string.Empty
            })
            .ToList();

        return new EntraUserDetailSummary { Items = rows, TotalCount = rows.Count };
    }

    public IReadOnlyList<(string Tenant, int Count)> CountForByTenant(EntraObjectType type)
    {
        if (Error != null) return Array.Empty<(string, int)>();
        var counts = Items
            .Where(i => i.ObjectType == type)
            .GroupBy(i => i.Tenant, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        return Tenants
            .Select(t => (t, counts.TryGetValue(t, out var c) ? c : 0))
            .ToList();
    }

    /// <summary>
    /// Returns a new summary reduced to the effective tenant set for the given selection.
    /// This is the single server-side filter choke point for Entra: both live tiles and the
    /// per-segment export project onto the result of this reduction, so filtering logic lives
    /// in exactly one place. On error, the error is preserved and no filtering is attempted.
    /// </summary>
    public EntraTotalsSummary Filter(SegmentSelection selection)
    {
        if (Error != null)
            return this;

        var effective = selection.Resolve(Tenants);
        var effectiveSet = new HashSet<string>(effective, StringComparer.OrdinalIgnoreCase);

        // Fast path: nothing to reduce.
        if (effectiveSet.Count == Tenants.Count && Tenants.All(effectiveSet.Contains))
            return this;

        var items = Items.Where(i => effectiveSet.Contains(i.Tenant)).ToList();

        var byType = items
            .GroupBy(i => i.ObjectType)
            .Select(g => new EntraObjectTypeCount { ObjectType = g.Key, TotalCount = g.Count() })
            .ToList();

        return new EntraTotalsSummary
        {
            Tenants = effective.ToList(),
            Items = items,
            ByObjectType = byType,
            TotalCount = items.Count,
            Error = null,
            MembershipLoaded = MembershipLoaded,
            MembershipLoadedCount = MembershipLoadedCount
        };
    }
}

public class ComputerBreakdownSummary
{
    public int TotalCount { get; set; }
    public List<ComputerBreakdownInfo> Items { get; set; } = new();
    public string? Error { get; set; }
}

public class ComputerBreakdownInfo : IPermissionScoped
{
    public string Name { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = string.Empty;
    public string OperatingSystemVersion { get; set; } = string.Empty;
    public string FriendlyOSName { get; set; } = string.Empty;
    public string Dn { get; set; } = string.Empty;

    [JsonIgnore] public IReadOnlyCollection<string> EffectiveLinkGuids { get; set; } = System.Array.Empty<string>();
    [JsonIgnore] public string ObjectClass { get; set; } = string.Empty;
}

/// <summary>
/// A single scalar security-health signal (e.g. krbtgt password age in days, or a
/// weak-policy indicator encoded as a count). <see cref="Value"/> carries the measured
/// number consumed by assessment rules via RiskSummary.GetKpiResult.
/// </summary>
public class SecurityHealthSummary
{
    public int Value { get; set; }
    public string? Error { get; set; }

    /// <summary>
    /// The NetBIOS domain this signal was measured against (e.g. krbtgt / password policy live
    /// per domain). Used to scope the signal by domain visibility for non-admin viewers.
    /// </summary>
    public string Domain { get; set; } = string.Empty;
}
