using System.Reflection;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.Definitions;

namespace ActiveRolesDashboard.Tests.Definitions;

/// <summary>
/// Builds definition sets from the compiled-in KPI, category and chart instances. Used to
/// generate the embedded <c>Definitions/*.json</c> files and to detect drift between them and
/// the code while both exist. Evaluation is recorded as the <c>Legacy</c> transform function
/// until the generic evaluator replaces the hand-written logic.
/// </summary>
internal static class LegacyDefinitionExporter
{
    public const string LegacyFunction = "Legacy";

    /// <summary>
    /// KPIs migrated from hand-written logic to expressions: KPI id -> (source dataset, filter).
    /// The legacy code still produces the displayed value; the parity checker compares both.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string Dataset, string Filter)> Migrated =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["EnabledUsers"] = ("ADUserAccounts", "!hasFlag(userAccountControl, 0x2)"),
            ["DisabledUsers"] = ("ADUserAccounts", "hasFlag(userAccountControl, 0x2)"),
            ["HybridUsers"] = ("ADUserAccounts", "!isEmpty(`msDS-ExternalDirectoryObjectId`)"),
            ["OnPremOnlyUsers"] = ("ADUserAccounts", "isEmpty(`msDS-ExternalDirectoryObjectId`)"),
            ["ExpiringUsers"] = ("ADUserAccounts", "daysUntil(accountExpires) > 0"),
            ["ReversibleEncryption"] = ("ADUserAccounts", "hasFlag(userAccountControl, 0x80)"),
            ["PasswordNeverExpires"] = ("ADUserAccounts", "hasFlag(userAccountControl, 0x10000)"),
            ["DeprovisionedUsers"] = ("ADUserAccounts", "edsvaDeprovisionStatus == '1'"),
            ["StalePasswords"] = ("ADUserAccounts", "!hasFlag(userAccountControl, 0x2) && daysSince(pwdLastSet) > $passwordAgeDays"),
            ["NeverLoggedIn"] = ("ADUserAccounts", "lastLogonTimestamp == ''"),
            ["StaleUsers"] = ("ADUserAccounts", "!hasFlag(userAccountControl, 0x2) && daysSince(lastLogonTimestamp) > $staleDays"),
            ["AdminCount"] = ("ADUserAccounts", "adminCount == '1'"),
            ["MustChangePassword"] = ("ADUserAccounts", "pwdLastSet == 0"),
            ["PasswordNotRequired"] = ("ADUserAccounts", "hasFlag(userAccountControl, 0x20)"),
            ["CannotChangePassword"] = ("ADUserAccounts", "hasFlag(userAccountControl, 0x40)"),
            ["UserReversibleEncryption"] = ("ADUserAccounts", "hasFlag(userAccountControl, 0x80)"),
            ["SmartCardRequired"] = ("ADUserAccounts", "hasFlag(userAccountControl, 0x40000)"),
            ["TrustedForDelegation"] = ("ADUserAccounts", "hasFlag(userAccountControl, 0x80000)"),
            ["SensitiveCannotDelegate"] = ("ADUserAccounts", "hasFlag(userAccountControl, 0x100000)"),
            ["UseDesEncryption"] = ("ADUserAccounts", "hasFlag(userAccountControl, 0x200000)"),
            ["NoKerberosPreauth"] = ("ADUserAccounts", "hasFlag(userAccountControl, 0x400000)"),
            ["SpnUserAccounts"] = ("ADUserAccounts", "!isEmpty(servicePrincipalName)"),
            ["DistributionGroups"] = ("ADGroups", NotGroupFamily + " && groupType >= 0"),
            ["SecurityGroups"] = ("ADGroups", NotGroupFamily + " && groupType < 0"),
            ["DomainLocalGroups"] = ("ADGroups", NotGroupFamily + " && hasFlag(groupType, 0x4)"),
            ["GlobalGroups"] = ("ADGroups", NotGroupFamily + " && hasFlag(groupType, 0x2)"),
            ["UniversalGroups"] = ("ADGroups", NotGroupFamily + " && hasFlag(groupType, 0x8)"),
            ["MailEnabledSecurityGroups"] = ("ADGroups", NotGroupFamily + " && groupType < 0 && !isEmpty(mail)"),
            ["EnabledComputers"] = ("Computers", "!hasFlag(userAccountControl, 0x2)"),
            ["DisabledComputers"] = ("Computers", "hasFlag(userAccountControl, 0x2)"),
            ["UnconstrainedComputers"] = ("Computers", "hasFlag(userAccountControl, 0x80000)"),
            ["StaleComputers"] = ("Computers", EnabledNonDc + " && daysSince(lastLogonTimestamp) > $staleDays"),
            ["NeverLoggedOnComputers"] = ("Computers", EnabledNonDc + " && !exists(daysSince(lastLogonTimestamp))"),
            ["ComputerClients"] = ("Computers", "!" + IsServer),
            ["ComputerServers"] = ("Computers", IsServer + " && !hasFlag(userAccountControl, 0x2000)"),
            ["WinServer2008R2"] = ("Computers", IsServer + " && in(operatingSystemVersion, '6.1')"),
            ["WinServer2012R2"] = ("Computers", IsServer + " && in(operatingSystemVersion, '6.3')"),
            ["WinServer2016"] = ("Computers", IsServer + " && in(operatingSystemVersion, '10.0 (14393)')"),
            ["WinServer2019"] = ("Computers", IsServer + " && in(operatingSystemVersion, '10.0 (17763)')"),
            ["WinServer2022"] = ("Computers", IsServer + " && in(operatingSystemVersion, '10.0 (20348)')"),
            ["WinServer2025"] = ("Computers", IsServer + " && in(operatingSystemVersion, '10.0 (26100)')"),
            ["ServerOther"] = ("Computers", IsServer + " && !in(operatingSystemVersion, '6.1', '6.3', '10.0 (14393)', '10.0 (17763)', '10.0 (20348)', '10.0 (26100)')"),
            ["Win7"] = ("Computers", "!" + IsServer + " && in(operatingSystemVersion, '6.1')"),
            ["Win81"] = ("Computers", "!" + IsServer + " && in(operatingSystemVersion, '6.3')"),
            ["Win10_22H2"] = ("Computers", "!" + IsServer + " && in(operatingSystemVersion, '10.0 (19045)')"),
            ["Win11_22H2"] = ("Computers", "!" + IsServer + " && in(operatingSystemVersion, '10.0 (22621)')"),
            ["Win11_23H2"] = ("Computers", "!" + IsServer + " && in(operatingSystemVersion, '10.0 (22631)')"),
            ["Win11Enterprise"] = ("Computers", "!" + IsServer + " && in(operatingSystemVersion, '10.0 (26200)')"),
            ["Win11Pro"] = ("Computers", "!" + IsServer + " && in(operatingSystemVersion, '10.0 (26100)')"),
            ["ClientsOther"] = ("Computers", "!" + IsServer + " && !in(operatingSystemVersion, '6.1', '6.3', '10.0 (19045)', '10.0 (22621)', '10.0 (22631)', '10.0 (26200)', '10.0 (26100)')"),
        };

    /// <summary>
    /// Search KPIs whose value is the number of items returned by their own search. Their
    /// evaluation is a plain count expression over the KPI's dataset.
    /// </summary>
    public static readonly IReadOnlySet<string> MigratedSearches = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "NoGroupOwner", "NoManagerServiceAccount", "UserAccountLockedOut", "EmptyGroups",
        "ServiceAccounts", "GmsaServiceAccounts", "SmsaServiceAccounts",
        "Sites", "SiteLinks", "Subnets", "OUs", "Trusts",
    };

    private const string IsServer = "contains(operatingSystem, 'server')";
    private const string EnabledNonDc = "!hasFlag(userAccountControl, 0x2) && !hasFlag(userAccountControl, 0x2000)";

    private const string NotGroupFamily = "edsvaGFIsGroupFamily != 'TRUE'";

    public static IReadOnlyDictionary<string, DefinitionSet> ExportByDashboard()
    {
        var categoryDashboard = CategoryInfo.All.ToDictionary(c => c.Key, c => c.DashboardKey, StringComparer.OrdinalIgnoreCase);

        var datasets = new Dictionary<string, (string Dashboard, DatasetDefinition Dataset)>(StringComparer.OrdinalIgnoreCase);
        var kpisByDashboard = new Dictionary<string, List<KpiDefinition>>(StringComparer.OrdinalIgnoreCase);

        foreach (var kpi in KpiInfo.All)
        {
            var dashboard = categoryDashboard.GetValueOrDefault(kpi.CategoryKey, "Main");
            var definition = ToKpi(kpi, dashboard, out var kpiDatasets);
            foreach (var ds in kpiDatasets)
            {
                datasets.TryAdd(ds.Id, (dashboard, ds));
            }

            Bucket(kpisByDashboard, dashboard).Add(definition);
        }

        var result = new Dictionary<string, DefinitionSet>(StringComparer.OrdinalIgnoreCase);
        var dashboards = CategoryInfo.All.Select(c => c.DashboardKey)
            .Concat(kpisByDashboard.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var dashboard in dashboards)
        {
            result[dashboard] = new DefinitionSet
            {
                Categories = CategoryInfo.All
                    .Where(c => string.Equals(c.DashboardKey, dashboard, StringComparison.OrdinalIgnoreCase))
                    .Select(ToCategory)
                    .ToList(),
                Datasets = datasets.Values
                    .Where(d => string.Equals(d.Dashboard, dashboard, StringComparison.OrdinalIgnoreCase))
                    .Select(d => d.Dataset)
                    .ToList(),
                Kpis = kpisByDashboard.GetValueOrDefault(dashboard) ?? []
            };
        }

        return result;
    }

    private static List<T> Bucket<T>(Dictionary<string, List<T>> map, string key)
    {
        if (!map.TryGetValue(key, out var list))
        {
            map[key] = list = [];
        }

        return list;
    }

    private static CategoryDefinition ToCategory(CategoryInfo c) => new()
    {
        Id = c.Key,
        FallbackText = Literal(c, "_displayName"),
        DashboardId = c.DashboardKey,
        SortOrder = c.SortOrder,
        Kind = c.IsOverviewCategory ? CategoryKind.Overview : c.IsRiskCategory ? CategoryKind.Risk : CategoryKind.Standard,
        KpiSortOrder = Enum.Parse<DefinitionSortOrder>(c.KpiSortOrder.ToString()),
        Charts = ChartInfo.ForCategory(c.Key).Select(ToChart).ToList()
    };

    private static ChartDefinition ToChart(ChartInfo c) => new()
    {
        Id = c.Key,
        FallbackText = Literal(c, "_title"),
        SortOrder = c.SortOrder,
        Type = Enum.Parse<ChartKind>(c.Type.ToString()),
        SourceSplitKpiId = string.IsNullOrEmpty(c.SourceSplitKpiKey) ? null : c.SourceSplitKpiKey,
        SliceOffset = c.SliceOffset,
        AllowTypeToggle = !c.DisableTypeToggle,
        Series = c.Series.Select(s => new ChartSeriesDefinition
        {
            KpiId = s.KpiKey,
            Label = string.IsNullOrEmpty(s.Label) ? null : s.Label,
            Color = string.IsNullOrEmpty(s.CssColor) ? null : s.CssColor
        }).ToList()
    };

    private static KpiDefinition ToKpi(KpiInfo k, string dashboard, out List<DatasetDefinition> datasets)
    {
        datasets = [];
        var source = k.DataSource;
        var display = new KpiDisplay
        {
            Color = k.CssColor,
            SectionId = string.IsNullOrEmpty(k.SectionId) ? null : k.SectionId,
            TileFallbackText = NullIfEmpty(Literal(k, "_tileLabel"))
        };
        var legacy = new EvaluationDefinition { Type = EvaluationType.Function, Function = LegacyFunction };

        if (k.Searches.Count == 0)
        {
            var migrated = Migrated.TryGetValue(k.Key, out var m);
            return new DerivedKpi
            {
                Id = k.Key, FallbackText = Literal(k, "_displayName"), CategoryId = k.CategoryKey, SortOrder = k.SortOrder,
                DataSource = source, Display = display, IsRiskKpi = k.IsRiskKpi, IsSegmentable = k.IsSegmentable,
                Drilldown = new DrilldownDefinition { Enabled = k.HasDrilldown },
                SourceDatasetId = migrated ? m.Dataset : null,
                Evaluation = migrated ? new EvaluationDefinition { Filter = m.Filter } : legacy
            };
        }

        // Every compiled-in KPI has at most one search; extra searches would need composite KPIs.
        var search = k.Searches.Single();
        var datasetId = search.SharedSearchKey ?? k.Key;
        var isObject = !string.IsNullOrEmpty(search.GroupName);
        datasets.Add(new DatasetDefinition
        {
            Id = datasetId,
            DataSource = source,
            Retrieval = new RetrievalDefinition
            {
                Target = isObject ? RetrievalTarget.Objects : RetrievalTarget.Search,
                Base = search.BaseDn,
                Filter = search.Filter,
                Scope = search.Scope,
                Objects = isObject ? [search.GroupName!] : []
            },
            Attributes = search.Attributes.StartsWith('{')
                ? [search.Attributes]
                : search.Attributes.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        });

        return isObject
            ? new ObjectKpi
            {
                Id = k.Key, FallbackText = Literal(k, "_displayName"), CategoryId = k.CategoryKey, SortOrder = k.SortOrder,
                DataSource = source, Display = display, IsRiskKpi = k.IsRiskKpi, IsSegmentable = k.IsSegmentable,
                Drilldown = new DrilldownDefinition { Enabled = k.HasDrilldown }, DatasetId = datasetId,
                // Fixed-object KPIs count the expanded (direct and indirect) members of the group.
                Evaluation = new EvaluationDefinition()
            }
            : new SearchKpi
            {
                Id = k.Key, FallbackText = Literal(k, "_displayName"), CategoryId = k.CategoryKey, SortOrder = k.SortOrder,
                DataSource = source, Display = display, IsRiskKpi = k.IsRiskKpi, IsSegmentable = k.IsSegmentable,
                Drilldown = new DrilldownDefinition { Enabled = k.HasDrilldown }, DatasetId = datasetId,
                Evaluation = MigratedSearches.Contains(k.Key) ? new EvaluationDefinition() : legacy
            };
    }

    private static string Literal(object instance, string field) =>
        instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance) as string ?? string.Empty;

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
}
