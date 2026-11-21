namespace ActiveRolesDashboard.Models.Definitions;

/// <summary>Detail view shown when a KPI tile is opened.</summary>
public sealed class DrilldownDefinition
{
    public bool Enabled { get; init; }

    /// <summary>Attribute identifying each row.</summary>
    public string ItemKeyAttribute { get; init; } = "objectGUID";

    public IReadOnlyList<DrilldownColumn> Columns { get; init; } = [];

    /// <summary>Optional Group Membership Tree link shown with the detail view.</summary>
    public GroupTreeDefinition? GroupTree { get; init; }
}

/// <summary>Group Membership Tree target of a KPI detail view.</summary>
public sealed class GroupTreeDefinition
{
    public GroupTreeScope Scope { get; init; } = GroupTreeScope.Row;

    /// <summary>
    /// Expression evaluated per row item giving the group DN; an empty result means no tree.
    /// For <see cref="GroupTreeScope.Kpi"/> the first non-empty result among matching items is used.
    /// </summary>
    public string Target { get; init; } = "distinguishedName";

    /// <summary>Optional filter selecting the items considered for a KPI-level target.</summary>
    public string? When { get; init; }
}

/// <summary>A column in the KPI detail view.</summary>
public sealed class DrilldownColumn
{
    /// <summary>Attribute shown in the column. Ignored when <see cref="Expression"/> is set.</summary>
    public string Attribute { get; init; } = string.Empty;

    /// <summary>Optional expression computing the cell value from the row item.</summary>
    public string? Expression { get; init; }

    /// <summary>Column header used when no localized header is found.</summary>
    public string? Header { get; init; }

    /// <summary>Resource key for the column header.</summary>
    public string? LocalizationKey { get; init; }

    /// <summary>Optional format hint, e.g. <c>date</c>, <c>number</c>, <c>dn</c>.</summary>
    public string? Format { get; init; }

    public bool Sortable { get; init; } = true;

    public bool Filterable { get; init; } = true;
}
