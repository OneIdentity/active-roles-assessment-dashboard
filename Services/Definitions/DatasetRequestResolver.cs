using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.Definitions;

namespace ActiveRolesDashboard.Services.Definitions;

/// <summary>Concrete REST search for one dataset, with all definition tokens resolved.</summary>
/// <remarks>
/// When <paramref name="ExpandMembers"/> is true the search locates fixed group objects and the
/// dataset items are their (direct and indirect) members.
/// </remarks>
public sealed record DatasetRequest(string DatasetId, string Base, string Filter, string Scope, IReadOnlyList<string> Attributes, bool ExpandMembers = false);

/// <summary>
/// Turns a <see cref="DatasetDefinition"/> into a concrete <see cref="DatasetRequest"/> by resolving
/// configuration tokens and merging the attributes declared by the KPIs that read the dataset.
/// </summary>
public static class DatasetRequestResolver
{
    public const string DefaultAdDnToken = "{DefaultADDN}";
    public const string DefaultArConfigDnToken = "{DefaultARConfigDN}";
    public const string DefaultAzureConfigDnToken = "{DefaultAzureConfigurationDN}";
    public const string ConfigFilterPrefix = "{ConfigFilter:";
    public const string UserAttributesToken = "{ConfigAttributes:ADUserAccounts}";

    /// <summary>
    /// Resolves the request for <paramref name="dataset"/>. Returns null when the dataset cannot be
    /// collected generically (tokens that do not resolve, or an unconfigured role group).
    /// </summary>
    public static DatasetRequest? Resolve(DatasetDefinition dataset, ActiveRolesConfig config, IEnumerable<KpiDefinition>? consumers = null)
    {
        var retrieval = dataset.Retrieval;
        if (retrieval.Method != RetrievalMethod.Rest)
            return null;

        var expand = retrieval.Target == RetrievalTarget.Objects;
        var baseDn = ResolveBase(retrieval.Base, config);
        var filter = expand && string.IsNullOrWhiteSpace(retrieval.Filter)
            ? ObjectsFilter(retrieval.Objects)
            : ResolveFilter(retrieval.Filter, config, ref baseDn);
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

        if (expand)
        {
            // Needed to expand the located groups into their members.
            Add("distinguishedName");
            Add("edsaDomainNetbiosName");
            Add("member");
        }

        return new DatasetRequest(dataset.Id, baseDn, filter, string.IsNullOrWhiteSpace(retrieval.Scope) ? "sub" : retrieval.Scope, attributes, expand);
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
        DefaultAzureConfigDnToken => config.DefaultAzureConfigurationDN,
        _ => value
    };

    /// <summary>
    /// True when the dataset searches for a configured role group (e.g. ActiveRolesAdmins) whose
    /// name is blank; such a group has no members rather than being an error.
    /// </summary>
    public static bool IsUnconfiguredRoleGroup(DatasetDefinition dataset, ActiveRolesConfig config)
    {
        var value = dataset.Retrieval.Filter;
        if (value is null || !value.StartsWith(ConfigFilterPrefix, StringComparison.Ordinal) || !value.EndsWith('}'))
            return false;
        var property = typeof(RoleGroupsConfig).GetProperty(value[ConfigFilterPrefix.Length..^1]);
        return property?.PropertyType == typeof(string) && string.IsNullOrWhiteSpace(property.GetValue(config.RoleGroups) as string);
    }

    private static string? ResolveFilter(string? value, ActiveRolesConfig config, ref string? baseDn)
    {
        if (value is null || !value.StartsWith(ConfigFilterPrefix, StringComparison.Ordinal) || !value.EndsWith('}'))
            return value;

        var name = value[ConfigFilterPrefix.Length..^1];
        if (typeof(DefaultFiltersConfig).GetProperty(name)?.GetValue(config.DefaultFilters) is string filter)
            return filter;

        // Role groups (e.g. ActiveRolesAdmins) are configured by name; build the same filter and
        // base as the legacy role group search.
        if (typeof(RoleGroupsConfig).GetProperty(name)?.GetValue(config.RoleGroups) is string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName)) return null;
            var escaped = EscapeLdapValue(groupName.Trim());
            if (config.RoleGroups.DirectoryType == RoleGroupDirectoryType.Entra)
            {
                baseDn = config.DefaultAzureConfigurationDN;
                return $"(&(|(objectClass=edsAzureSecurityGroup)(objectClass=edsAzureO365Group))(name={escaped}))";
            }

            baseDn = config.DefaultActiveDirectoryDN;
            return $"(&(objectClass=group)(name={escaped}))";
        }

        return value;
    }

    private static string? ObjectsFilter(IReadOnlyList<string> objects)
    {
        var terms = objects.Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o.Contains('=')
                ? $"(distinguishedName={EscapeLdapValue(o.Trim())})"
                : $"(name={EscapeLdapValue(o.Trim())})")
            .ToList();
        return terms.Count switch
        {
            0 => null,
            1 => terms[0],
            _ => $"(|{string.Concat(terms)})"
        };
    }

    private static string EscapeLdapValue(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            sb.Append(c switch
            {
                '\\' => "\\5c",
                '*' => "\\2a",
                '(' => "\\28",
                ')' => "\\29",
                '\0' => "\\00",
                _ => c.ToString()
            });
        }

        return sb.ToString();
    }
}
