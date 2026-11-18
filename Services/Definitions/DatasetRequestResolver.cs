using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.Definitions;

namespace ActiveRolesDashboard.Services.Definitions;

/// <summary>Concrete REST search for one dataset, with all definition tokens resolved.</summary>
public sealed record DatasetRequest(string DatasetId, string Base, string Filter, string Scope, IReadOnlyList<string> Attributes);

/// <summary>
/// Turns a <see cref="DatasetDefinition"/> into a concrete <see cref="DatasetRequest"/> by resolving
/// configuration tokens and merging the attributes declared by the KPIs that read the dataset.
/// </summary>
public static class DatasetRequestResolver
{
    public const string DefaultAdDnToken = "{DefaultADDN}";
    public const string DefaultArConfigDnToken = "{DefaultARConfigDN}";
    public const string ConfigFilterPrefix = "{ConfigFilter:";
    public const string UserAttributesToken = "{ConfigAttributes:ADUserAccounts}";

    /// <summary>
    /// Resolves the request for <paramref name="dataset"/>. Returns null when the dataset cannot be
    /// collected generically yet (object reads, or tokens that do not resolve).
    /// </summary>
    public static DatasetRequest? Resolve(DatasetDefinition dataset, ActiveRolesConfig config, IEnumerable<KpiDefinition>? consumers = null)
    {
        var retrieval = dataset.Retrieval;
        if (retrieval.Method != RetrievalMethod.Rest || retrieval.Target != RetrievalTarget.Search)
            return null;

        var baseDn = ResolveBase(retrieval.Base, config);
        var filter = ResolveFilter(retrieval.Filter, config);
        if (string.IsNullOrWhiteSpace(baseDn) || string.IsNullOrWhiteSpace(filter) || filter.StartsWith('{'))
            return null;

        var attributes = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string a)
        {
            if (!string.IsNullOrWhiteSpace(a) && seen.Add(a)) attributes.Add(a);
        }

        foreach (var attribute in dataset.Attributes)
        {
            if (string.Equals(attribute, UserAttributesToken, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var a in config.DefaultADUserAccountAttributes.Concat(config.CustomADUserAccountAttributes)) Add(a);
                // Matches the legacy user query: managed-scope logic needs the effective policy links.
                Add("edsvaAPOLinksEffective");
            }
            else
            {
                Add(attribute);
            }
        }

        foreach (var kpi in consumers ?? [])
            foreach (var a in kpi.Attributes) Add(a);

        return new DatasetRequest(dataset.Id, baseDn, filter, string.IsNullOrWhiteSpace(retrieval.Scope) ? "sub" : retrieval.Scope, attributes);
    }

    /// <summary>KPIs whose values are read from <paramref name="datasetId"/>.</summary>
    public static IEnumerable<KpiDefinition> ConsumersOf(DefinitionRegistry registry, string datasetId) =>
        registry.Set.Kpis.Where(k => string.Equals(k switch
        {
            SearchKpi s => s.DatasetId,
            ObjectKpi o => o.DatasetId,
            DerivedKpi d => d.SourceDatasetId,
            _ => null
        }, datasetId, StringComparison.OrdinalIgnoreCase));

    private static string? ResolveBase(string? value, ActiveRolesConfig config) => value switch
    {
        null => null,
        DefaultAdDnToken => config.DefaultActiveDirectoryDN,
        DefaultArConfigDnToken => config.DefaultARConfigurationDN,
        _ => value
    };

    private static string? ResolveFilter(string? value, ActiveRolesConfig config)
    {
        if (value is null || !value.StartsWith(ConfigFilterPrefix, StringComparison.Ordinal) || !value.EndsWith('}'))
            return value;

        var name = value[ConfigFilterPrefix.Length..^1];
        var property = typeof(DefaultFiltersConfig).GetProperty(name);
        return property?.GetValue(config.DefaultFilters) as string ?? value;
    }
}
