using System.Reflection;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.Definitions;
using ActiveRolesDashboard.Services.Definitions;

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
            // These two declare their own search, but legacy derives them from the user dataset.
            ["NoManagerUser"] = ("ADUserAccounts", "isEmpty(manager)"),
            ["ExpiredUsers"] = ("ADUserAccounts", "exists(daysUntil(accountExpires)) && daysUntil(accountExpires) <= 0"),
            ["AdOverviewUsers"] = ("ADUserAccounts", ""),
            ["AdOverviewGroups"] = ("ADGroups", ""),
            ["AdOverviewComputers"] = ("Computers", ""),
            ["DomainControllers"] = ("Computers", "hasFlag(userAccountControl, 0x2000)"),
            ["ManagedEnabledUsers"] = ("ADUserAccounts", "!hasFlag(userAccountControl, 0x2) && !containsAny(edsvaAPOLinksEffective, $excludeLinkDns)"),
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
            ["EntraOverviewUsers"] = (Entra, EntraUser),
            ["EntraOverviewGroups"] = (Entra, EntraGroup),
            ["EntraEnabledUsers"] = (Entra, EntraUser + " && " + EntraEnabled),
            ["EntraDisabledUsers"] = (Entra, EntraUser + " && !" + EntraEnabled),
            ["EntraNoManagerUser"] = (Entra, EntraUser + " && isEmpty(manager)"),
            ["EntraGuestUsers"] = (Entra, "entraObjectType == 'GuestUser'"),
            ["EntraInternalUsers"] = (Entra, EntraAnyUser + " && !" + EntraExternal),
            ["EntraExternalUsers"] = (Entra, EntraAnyUser + " && " + EntraExternal),
            ["EntraHybridUsers"] = (Entra, EntraUser + " && " + EntraSynced),
            ["EntraCloudOnlyUsers"] = (Entra, EntraUser + " && !" + EntraSynced),
            ["EntraManagedUsers"] = (Entra, EntraUser + " && " + EntraEnabled + " && !containsAny(edsvaAPOLinksEffective, $excludeLinkDns)"),
            ["EntraDistributionGroups"] = (Entra, "entraObjectType == 'DistributionGroup'"),
            ["EntraDynamicDistributionGroups"] = (Entra, "entraObjectType == 'DynamicDistributionGroup'"),
            ["EntraMicrosoft365Groups"] = (Entra, "entraObjectType == 'Microsoft365Group'"),
            ["EntraSecurityGroups"] = (Entra, "entraObjectType == 'SecurityGroup'"),
            ["EntraEmptyGroups"] = (Entra, EntraGroup + " && isEmpty(member)"),
            ["EntraNoGroupOwner"] = (Entra, EntraGroup + " && isEmpty(edsaAzureGroupManagedBy)"),
            ["EntraPublicGroups"] = (Entra, "entraObjectType == 'Microsoft365Group' && visibility == 'Public'"),
            ["EntraOnPremSyncedGroups"] = (Entra, EntraGroup + " && " + EntraSynced),
            ["EntraSingleOwnerGroups"] = (Entra, EntraGroup + " && count(edsaAzureGroupManagedBy) == 1"),
            ["EntraLargeGroups"] = (Entra, EntraGroup + " && count(member) >= $entraLargeGroupThreshold"),
            ["GroupFamilies"] = ("ADGroups", "edsvaGFIsGroupFamily == 'TRUE'"),
            ["DynamicGroups"] = ("ADGroups", "edsaIsDynamicGroup == 'TRUE'"),
            ["BroadDelegationLinks"] = (DelegationLinks, NotPredefined + " && (contains(trustee, 'Everyone') || contains(trustee, 'Authenticated Users') || contains(trustee, 'Domain Users'))"),
            ["UserDelegationLinks"] = (DelegationLinks, NotPredefined + " && trusteeIsUser"),
            ["RootDelegationLinks"] = (DelegationLinks, NotPredefined + " && isRootScope"),
            ["DynamicGroupsBrokenRules"] = (DynamicGroupsBrokenRulesId, "count(unresolvedGuids) > 0"),
            ["ManagedUnitsBrokenRules"] = (ManagedUnitsBrokenRulesId, "count(unresolvedGuids) > 0"),
            ["EntraManagedTenants"] = (EntraTenantsId, ""),
            ["ExchangeSendAsKpi"] = (ExchangeSendAsId, "count(trustees) > 0"),
            ["ExchangeFullAccessDelegatesKpi"] = (ExchangeFullAccessId, "count(trustees) > 0"),
        };

    /// <summary>
    /// Search KPIs whose value is the number of items returned by their own search. Their
    /// evaluation is a plain count expression over the KPI's dataset.
    /// </summary>
    public static readonly IReadOnlySet<string> MigratedSearches = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "NoGroupOwner", "NoManagerServiceAccount", "UserAccountLockedOut", "EmptyGroups",
        "ServiceAccounts", "GmsaServiceAccounts", "SmsaServiceAccounts",
        "Sites", "SiteLinks", "Subnets", "OUs", "Trusts", "Computers",
        "ExchangeServersKpi", "ExchangeTotalMailboxes", "ExchangeMailboxDatabasesKpi", "ExchangeAcceptedDomainsKpi",
        "ExchangeMailboxServersKpi", "ExchangeDAGsKpi", "ExchangeUserMailboxesKpi", "ExchangeSharedMailboxesKpi",
        "ExchangeRoomMailboxesKpi", "ExchangeEquipmentMailboxesKpi", "ExchangeMailUsersKpi", "ExchangeMailContactsKpi",
        "ExchangeDistributionGroupsKpi", "ExchangeMailEnabledSecurityGroupsKpi", "ExchangeDynamicDistributionGroupsKpi",
        "ExchangeGroupsNoOwnerKpi", "ExchangeEmptyDistributionGroupsKpi", "ExchangeMailboxNoManagerKpi",
        "ExchangeOrphanedMailboxesKpi", "ExchangeLitigationHoldDisabledKpi", "ExchangeSendOnBehalfKpi",
        "Servers", "Domains", "AccessTemplateLinks", "AccessTemplates", "ManagedUnits", "PolicyObjectLinks", "PolicyObjects",
        "VirtualAttributes", "Workflows", "ConfigDatabases", "HistoryDatabases", "ScheduledTasks", "ScriptModules",
    };

    /// <summary>
    /// Search KPIs whose value is the number of their own search results matching a filter.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> MigratedSearchFilters =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["EmptyAccessTemplates"] = "isEmpty(edsaATEList)",
            ["PolicyObjectsNoRules"] = "xmlCount(edsaAPEListXML, 'APE') == 0",
            ["DenyAccessTemplates"] = "aceHasDeny(edsaATEList)",
            ["OrphanAccessTemplateLinks"] = "isBlank(edsaAccessTemplateGUID) || isBlank(edsaSecObjectGUID) || isBlank(edsaTrusteeSID)",
            ["OrphanPolicyObjectLinks"] = "isBlank(edsaAPOGUID) || isBlank(edsaSecObjectGUID)",
        };

    /// <summary>
    /// Search KPIs evaluated by a registered function over their own search: KPI id -> (function, args).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string Function, Dictionary<string, string> Args)> MigratedSearchFunctions =
        new Dictionary<string, (string, Dictionary<string, string>)>(StringComparer.OrdinalIgnoreCase)
        {
            ["UnlinkedAccessTemplates"] = ("unreferencedItems", new()
            {
                ["referenceDataset"] = "AccessTemplateLinks", ["referenceAttribute"] = "edsaAccessTemplateGUID",
                ["keyAttribute"] = "objectGUID", ["match"] = "guid"
            }),
            ["UnlinkedPolicyObjects"] = ("unreferencedItems", new()
            {
                ["referenceDataset"] = "OrphanPolicyObjectLinks", ["referenceAttribute"] = "edsaAPOGUID",
                ["keyAttribute"] = "objectGUID", ["match"] = "guid"
            }),
        };

    /// <summary>
    /// KPIs migrated to a registered transform function: KPI id -> (source dataset, function name).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string Dataset, string Function)> MigratedFunctions =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["CircularGroupNesting"] = ("ADGroups", "circularNesting"),
            ["ManagedObjects"] = ("ManagedObjects", "managedObjectStatistics"),
            ["EntraGuestContainingGroups"] = (Entra, "guestContainingGroups"),
        };

    /// <summary>
    /// KPIs combining the results of other KPIs: KPI id -> (source KPIs, aggregate). A KPI that
    /// also declares a search keeps exporting its dataset, which other KPIs read.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string[] Sources, CompositeAggregate Aggregate)> MigratedComposites =
        new Dictionary<string, (string[], CompositeAggregate)>(StringComparer.OrdinalIgnoreCase)
        {
            ["ADUserAccounts"] = (["AdOverviewUsers", "EntraOverviewUsers"], CompositeAggregate.Sum),
            ["ADGroups"] = (["AdOverviewGroups", "EntraOverviewGroups"], CompositeAggregate.Sum),
            ["MainTotalMailboxes"] = (["ExchangeTotalMailboxes"], CompositeAggregate.First),
        };

    private const string IsServer = "contains(operatingSystem, 'server')";
    private const string EnabledNonDc = "!hasFlag(userAccountControl, 0x2) && !hasFlag(userAccountControl, 0x2000)";

    private const string NotGroupFamily = "edsvaGFIsGroupFamily != 'TRUE'";

    private const string Entra = EntraTotalsSummary.DatasetId;
    private const string EntraUser = "entraObjectType == 'User'";
    private const string EntraAnyUser = "in(entraObjectType, 'User', 'GuestUser')";
    private const string EntraGroup = "in(entraObjectType, 'DistributionGroup', 'DynamicDistributionGroup', 'Microsoft365Group', 'SecurityGroup')";
    private const string EntraEnabled = "!in(edsaAzureUserAccountEnabled, 'false', '0')";
    private const string EntraSynced = "in(edsvaOnPremisesSyncEnabled, 'true', '1')";
    private const string EntraExternal = "contains(edsaAzureUserPrincipalName, '#EXT#')";

    private const string DelegationLinks = "DelegationLinks";
    private const string DynamicGroupsBrokenRulesId = "DynamicGroupsWithBrokenRules";
    private const string ManagedUnitsBrokenRulesId = "ManagedUnitsWithBrokenRules";
    private const string EntraTenantsId = "EntraTenants";
    private const string ExchangeSendAsId = "ExchangeSendAsMailboxes";
    private const string ExchangeFullAccessId = "ExchangeFullAccessMailboxes";
    private const string NotPredefined = "!(edsaIsPredefined == 'TRUE' && edsaSystemObject == 'TRUE')";
    private const string MailboxFilter = "(&(objectClass=user)(homeMDB=*)(!(msExchRecipientTypeDetails=4)))";

    /// <summary>
    /// Datasets whose items need directory lookups (GUID checks, tenant counts, trustee and scope
    /// resolution, mailbox DACLs) declared as enrichments of a searched dataset.
    /// </summary>
    private static DatasetDefinition? ResolvedDatasetDefinition(string id, DataSource source) => id switch
    {
        DynamicGroupsBrokenRulesId => Enriched(id, source, KpiInfo.DynamicGroupsBrokenRules.Searches[0], Enrich(EnrichmentNames.UnresolvedGuids, "edsaDGConditionsList")),
        ManagedUnitsBrokenRulesId => Enriched(id, source, KpiInfo.ManagedUnitsBrokenRules.Searches[0], Enrich(EnrichmentNames.UnresolvedGuids, "edsaMUConditionsList")),
        EntraTenantsId => Enriched(id, source,
            new KpiSearchDefinition { BaseDn = DatasetRequestResolver.DefaultAzureConfigDnToken, Filter = "(objectClass=edsAzureTenantcontainer)", Scope = "one", Attributes = "name,distinguishedName" },
            new EnrichmentDefinition { Name = EnrichmentNames.DescendantCount }),
        ExchangeSendAsId or ExchangeFullAccessId => Enriched(id, source,
            new KpiSearchDefinition { BaseDn = DatasetRequestResolver.DefaultAdDnToken, Filter = MailboxFilter, Attributes = "name,distinguishedName,edsaDomainNetbiosName,objectGUID,objectSid" },
            new EnrichmentDefinition { Name = EnrichmentNames.MailboxPermissions, Args = new Dictionary<string, string> { ["right"] = id == ExchangeSendAsId ? "SendAs" : "FullAccess" } }),
        DelegationLinks => Enriched(id, source, KpiInfo.AccessTemplateLinks.Searches[0],
            Enrich(EnrichmentNames.Trustee, "edsaTrusteeSID"), Enrich(EnrichmentNames.RootScope, "edsaSecObjectGUID")),
        _ => null
    };

    private static EnrichmentDefinition Enrich(string name, string attribute) =>
        new() { Name = name, Args = new Dictionary<string, string> { ["attribute"] = attribute } };

    private static DatasetDefinition Enriched(string id, DataSource source, KpiSearchDefinition search, params EnrichmentDefinition[] enrichments)
    {
        var d = ToDataset(search, id, source);
        return new DatasetDefinition
        {
            Id = id, DataSource = source, Retrieval = d.Retrieval, Attributes = d.Attributes,
            KeyAttribute = "distinguishedName", Enrichments = enrichments
        };
    }

    /// <summary>
    /// The Entra object dataset is collected by the typed Entra code and exposed through
    /// <see cref="DashboardSummary.Datasets"/>; it is declared so definitions can reference it.
    /// </summary>
    private static DatasetDefinition EntraObjectsDataset() => new()
    {
        Id = Entra,
        DataSource = DataSource.EntraId,
        Retrieval = new RetrievalDefinition { Base = "{DefaultAzureConfigurationDN}" },
        Attributes = ["name", "distinguishedName", "edsaAzureUserAccountEnabled", "manager", "edsaAzureUserPrincipalName",
            "edsvaOnPremisesSyncEnabled", "edsvaAPOLinksEffective", "visibility", "member", "edsaAzureGroupManagedBy"],
        SegmentAttribute = EntraTotalsSummary.DatasetTenantAttribute,
        KeyAttribute = "distinguishedName"
    };

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

        var migrated = Migrated.TryGetValue(k.Key, out var m);
        if (MigratedComposites.TryGetValue(k.Key, out var c))
        {
            if (k.Searches.Count > 0) datasets.Add(ToDataset(k.Searches.Single(), k.Key, source));
            return new CompositeKpi
            {
                Id = k.Key, FallbackText = Literal(k, "_displayName"), CategoryId = k.CategoryKey, SortOrder = k.SortOrder,
                DataSource = source, Display = display, IsRiskKpi = k.IsRiskKpi, IsSegmentable = k.IsSegmentable,
                Drilldown = ToDrilldown(k),
                Sources = c.Sources,
                Aggregate = c.Aggregate
            };
        }

        if (MigratedFunctions.TryGetValue(k.Key, out var f))
        {
            if (f.Dataset == Entra) datasets.Add(EntraObjectsDataset());
            if (k.Searches.Count > 0 && string.Equals(k.Searches.Single().SharedSearchKey ?? k.Key, f.Dataset, StringComparison.OrdinalIgnoreCase))
                datasets.Add(ToDataset(k.Searches.Single(), f.Dataset, source));
            return new DerivedKpi
            {
                Id = k.Key, FallbackText = Literal(k, "_displayName"), CategoryId = k.CategoryKey, SortOrder = k.SortOrder,
                DataSource = source, Display = display, IsRiskKpi = k.IsRiskKpi, IsSegmentable = k.IsSegmentable,
                Drilldown = ToDrilldown(k),
                SourceDatasetId = f.Dataset,
                Evaluation = new EvaluationDefinition { Type = EvaluationType.Function, Function = f.Function }
            };
        }

        if (k.Searches.Count == 0 || migrated)
        {
            if (migrated && m.Dataset == Entra) datasets.Add(EntraObjectsDataset());
            if (migrated && ResolvedDatasetDefinition(m.Dataset, source) is { } resolved) datasets.Add(resolved);
            return new DerivedKpi
            {
                Id = k.Key, FallbackText = Literal(k, "_displayName"), CategoryId = k.CategoryKey, SortOrder = k.SortOrder,
                DataSource = source, Display = display, IsRiskKpi = k.IsRiskKpi, IsSegmentable = k.IsSegmentable,
                Drilldown = ToDrilldown(k),
                SourceDatasetId = migrated ? m.Dataset : null,
                Evaluation = migrated ? new EvaluationDefinition { Filter = m.Filter } : legacy
            };
        }

        // Every compiled-in KPI has at most one search; extra searches would need composite KPIs.
        var search = k.Searches.Single();
        var datasetId = search.SharedSearchKey ?? k.Key;
        var isObject = !string.IsNullOrEmpty(search.GroupName);
        datasets.Add(ToDataset(search, datasetId, source));

        return isObject
            ? new ObjectKpi
            {
                Id = k.Key, FallbackText = Literal(k, "_displayName"), CategoryId = k.CategoryKey, SortOrder = k.SortOrder,
                DataSource = source, Display = display, IsRiskKpi = k.IsRiskKpi, IsSegmentable = k.IsSegmentable,
                Drilldown = ToDrilldown(k), DatasetId = datasetId,
                // Fixed-object KPIs count the expanded (direct and indirect) members of the group.
                Evaluation = new EvaluationDefinition()
            }
            : new SearchKpi
            {
                Id = k.Key, FallbackText = Literal(k, "_displayName"), CategoryId = k.CategoryKey, SortOrder = k.SortOrder,
                DataSource = source, Display = display, IsRiskKpi = k.IsRiskKpi, IsSegmentable = k.IsSegmentable,
                Drilldown = ToDrilldown(k), DatasetId = datasetId,
                Evaluation = MigratedSearches.Contains(k.Key) ? new EvaluationDefinition()
                    : MigratedSearchFilters.TryGetValue(k.Key, out var filter) ? new EvaluationDefinition { Filter = filter }
                    : MigratedSearchFunctions.TryGetValue(k.Key, out var fn) ? new EvaluationDefinition
                    {
                        Type = EvaluationType.Function, Function = fn.Function,
                        Args = fn.Args.ToDictionary(a => a.Key, a => System.Text.Json.JsonSerializer.SerializeToElement(a.Value))
                    }
                    : legacy
            };
    }

    private static DrilldownColumn Col(string header, string attribute) => new() { Header = header, Attribute = attribute };
    private static DrilldownColumn ExprCol(string header, string expression) => new() { Header = header, Expression = expression };

    private static readonly DrilldownColumn[] NameDomainDn = [Col("Name", "name"), Col("Domain", "edsaDomainNetbiosName"), Col("Distinguished Name", "distinguishedName")];
    private static readonly DrilldownColumn[] NameDn = [Col("Name", "name"), Col("Distinguished Name", "distinguishedName")];
    private static readonly DrilldownColumn[] NameTenantDn = [Col("Name", "name"), Col("Tenant", EntraTotalsSummary.DatasetTenantAttribute), Col("Distinguished Name", "distinguishedName")];
    private static readonly DrilldownColumn[] EntraUserColumns = [.. NameTenantDn, ExprCol("Enabled", EntraEnabled)];

    /// <summary>
    /// Drilldown columns reproducing the typed detail tables from the definition datasets. KPIs
    /// whose tables need values the datasets do not carry (managed status, indirect membership,
    /// correlated identities, resolved trustees) keep the typed table until those are modelled.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, DrilldownColumn[]> DrilldownColumns = BuildDrilldownColumns();

    private static Dictionary<string, DrilldownColumn[]> BuildDrilldownColumns()
    {
        var map = new Dictionary<string, DrilldownColumn[]>(StringComparer.OrdinalIgnoreCase);
        void Add(DrilldownColumn[] columns, params string[] ids) { foreach (var id in ids) map[id] = columns; }

        Add(NameDomainDn, "OnPremOnlyUsers", "MustChangePassword", "StalePasswords", "PasswordNotRequired", "SmartCardRequired",
            "CannotChangePassword", "NoKerberosPreauth", "UserReversibleEncryption", "SensitiveCannotDelegate", "TrustedForDelegation",
            "SpnUserAccounts", "StaleUsers", "UseDesEncryption", "PasswordNeverExpires", "NeverLoggedIn", "ExpiredUsers", "AdminCount",
            "NoManagerUser", "ReversibleEncryption", "CircularGroupNesting");
        Add([Col("Name", "name"), Col("Distinguished Name", "distinguishedName"), Col("Description", "description")], "DeprovisionedUsers");
        Add([Col("Name", "name"), Col("Domain", "edsaDomainNetbiosName"), Col("Site", "msDS-SiteName"), Col("Distinguished Name", "distinguishedName")], "DomainControllers");
        Add(NameDn, "EmptyAccessTemplates", "UnlinkedAccessTemplates", "DenyAccessTemplates", "PolicyObjectsNoRules", "UnlinkedPolicyObjects",
            "DynamicGroupsBrokenRules", "ManagedUnitsBrokenRules", "PolicyObjectLinks", "GroupFamilies", "DynamicGroups");
        Add(NameTenantDn, "EntraDistributionGroups", "EntraDynamicDistributionGroups", "EntraMicrosoft365Groups", "EntraSecurityGroups",
            "EntraEmptyGroups", "EntraNoGroupOwner", "EntraGuestContainingGroups", "EntraPublicGroups", "EntraOnPremSyncedGroups",
            "EntraSingleOwnerGroups", "EntraLargeGroups");
        Add(EntraUserColumns, "EntraNoManagerUser", "EntraGuestUsers", "EntraInternalUsers", "EntraCloudOnlyUsers");
        Add(NameDomainDn, "ExchangeServersKpi", "ExchangeTotalMailboxes", "ExchangeMailboxDatabasesKpi", "ExchangeAcceptedDomainsKpi",
            "ExchangeMailboxServersKpi", "ExchangeDAGsKpi", "ExchangeUserMailboxesKpi", "ExchangeSharedMailboxesKpi", "ExchangeRoomMailboxesKpi",
            "ExchangeEquipmentMailboxesKpi", "ExchangeMailUsersKpi", "ExchangeMailContactsKpi", "ExchangeDistributionGroupsKpi",
            "ExchangeMailEnabledSecurityGroupsKpi", "ExchangeDynamicDistributionGroupsKpi", "ExchangeGroupsNoOwnerKpi",
            "ExchangeEmptyDistributionGroupsKpi", "ExchangeMailboxNoManagerKpi", "ExchangeOrphanedMailboxesKpi", "ExchangeLitigationHoldDisabledKpi");
        Add(NameDomainDn, "NoManagerServiceAccount", "ServiceAccounts", "GmsaServiceAccounts", "SmsaServiceAccounts", "UserAccountLockedOut",
            "EmptyGroups", "Sites", "SiteLinks", "Subnets", "Trusts", "OUs");
        Add(NameDn, "NoGroupOwner");
        Add([Col("Name", "name"), Col("Domain", "edsaDomainNetbiosName"),
            ExprCol("Operating System", "friendlyOsName(operatingSystem, operatingSystemVersion)"),
            Col("Distinguished Name", "distinguishedName")],
            "ComputerClients", "UnconstrainedComputers", "ComputerServers", "WinServer2008R2", "WinServer2012R2", "WinServer2016",
            "WinServer2019", "WinServer2022", "WinServer2025", "ServerOther", "Win7", "Win81", "Win10_22H2", "Win11_22H2",
            "Win11_23H2", "Win11Enterprise", "Win11Pro", "ClientsOther", "EnabledComputers", "DisabledComputers",
            "StaleComputers", "NeverLoggedOnComputers");
        Add([Col("Server Name", "edsaEdmServiceComputerName"), Col("Version", "edsvaPublicProductVersion")], "Servers");
        Add([Col("Name", "name"), Col("DNS Name", "edsaSavedDnsName"), Col("Distinguished Name", "distinguishedName")], "Domains");
        Add([Col("Name", "name"), Col("Container", "edsvaParentCanonicalName"),
            ExprCol("Built-in", "in(edsaIsPredefined, 'TRUE', 'true') && in(edsaSystemObject, 'TRUE', 'true')"),
            Col("Distinguished Name", "distinguishedName")], "AccessTemplates");
        return map;
    }

    private static DrilldownDefinition ToDrilldown(KpiInfo k) => new()
    {
        Enabled = k.HasDrilldown,
        Columns = k.HasDrilldown && DrilldownColumns.TryGetValue(k.Key, out var columns) ? columns : []
    };

    private static DatasetDefinition ToDataset(KpiSearchDefinition search, string datasetId, DataSource source)
    {
        var isObject = !string.IsNullOrEmpty(search.GroupName);
        return new DatasetDefinition
        {
            Id = search.SharedSearchKey ?? datasetId,
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
        };
    }

    private static string Literal(object instance, string field) =>
        instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance) as string ?? string.Empty;

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
}
