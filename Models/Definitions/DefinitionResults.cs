using System.Text.Json;

namespace ActiveRolesDashboard.Models.Definitions;

/// <summary>Items retrieved for one dataset, or the error that prevented retrieval.</summary>
public sealed record DatasetResult(IReadOnlyList<JsonElement> Items, string? Error)
{
    public static DatasetResult Failed(string error) => new([], error);
}

/// <summary>Value computed for a definition-driven KPI, or the error that prevented it.</summary>
public sealed record KpiResult(long Value, string? Error)
{
    public static KpiResult Failed(string error) => new(0, error);
}
