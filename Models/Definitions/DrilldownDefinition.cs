namespace ActiveRolesDashboard.Models.Definitions;

/// <summary>Detail view shown when a KPI tile is opened.</summary>
public sealed class DrilldownDefinition
{
    public bool Enabled { get; init; }

    /// <summary>Attribute identifying each row.</summary>
    public string ItemKeyAttribute { get; init; } = "objectGUID";

    public IReadOnlyList<DrilldownColumn> Columns { get; init; } = [];
}

/// <summary>A column in the KPI detail view.</summary>
public sealed class DrilldownColumn
{
    public string Attribute { get; init; } = string.Empty;

    /// <summary>Resource key for the column header.</summary>
    public string? LocalizationKey { get; init; }

    /// <summary>Optional format hint, e.g. <c>date</c>, <c>number</c>, <c>dn</c>.</summary>
    public string? Format { get; init; }

    public bool Sortable { get; init; } = true;

    public bool Filterable { get; init; } = true;
}
