using System.Text.Json.Serialization;

namespace ActiveRolesDashboard.Models.Definitions;

/// <summary>
/// Properties shared by every stored definition. Display text is not stored here; it is
/// resolved from the KPI resource files using <see cref="LocalizationKey"/>.
/// </summary>
public abstract class DefinitionBase
{
    /// <summary>Unique, stable identifier. Matches the existing KPI/category/chart key.</summary>
    [JsonRequired, JsonPropertyOrder(-10)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Schema version of this definition.</summary>
    public int Version { get; init; } = 1;

    public bool Enabled { get; init; } = true;

    public int SortOrder { get; init; }

    /// <summary>Resource key for the display name. When empty the type-specific default is used.</summary>
    public string? LocalizationKey { get; init; }

    /// <summary>Text used when the resource key is not found.</summary>
    public string? FallbackText { get; init; }

    /// <summary>Roles allowed to see this item. Empty means all dashboard users.</summary>
    public IReadOnlyList<string> RequiresRole { get; init; } = [];

    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Default resource key prefix, e.g. <c>Kpi_</c> or <c>Cat_</c>.</summary>
    protected abstract string LocalizationPrefix { get; }

    /// <summary>The resource key to resolve for the display name.</summary>
    [JsonIgnore]
    public string EffectiveLocalizationKey =>
        string.IsNullOrWhiteSpace(LocalizationKey) ? LocalizationPrefix + Id : LocalizationKey;
}
