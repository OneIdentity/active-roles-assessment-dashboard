using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.Definitions.Evaluation;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// Compares the definition-driven KPI values in <see cref="DashboardSummary.KpiResults"/> with the
/// values produced by the hand-written logic. The legacy value is still the one displayed;
/// mismatches are logged and kept for diagnostics so migrations can be verified against live data
/// before the legacy code is removed.
/// </summary>
public sealed class DefinitionParityChecker(ILogger<DefinitionParityChecker> logger)
{
    private readonly object _gate = new();
    private IReadOnlyList<KpiParityResult> _last = [];

    public IReadOnlyList<KpiParityResult> LastResults
    {
        get { lock (_gate) return _last; }
    }

    public static EvaluationContext CreateContext(ActiveRolesConfig config, DateTimeOffset now) => new()
    {
        Now = now,
        Parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["staleDays"] = (double)(config.StaleAccountThresholdDays > 0 ? config.StaleAccountThresholdDays : 90),
            ["passwordAgeDays"] = (double)(config.PasswordAgeThresholdDays > 0 ? config.PasswordAgeThresholdDays : 365),
        }
    };

    public IReadOnlyList<KpiParityResult> Check(DashboardSummary summary)
    {
        var results = new List<KpiParityResult>();

        foreach (var (kpiId, generic) in summary.KpiResults)
        {
            var (legacy, error) = summary.GetKpiResult(kpiId);
            if (error is not null) continue;

            var result = new KpiParityResult(kpiId, legacy, generic.Error is null ? generic.Value : null, generic.Error);
            results.Add(result);
            if (!result.Matches)
                logger.LogWarning("Definition parity mismatch for KPI {Kpi}: legacy {Legacy}, definition {Generic} {Failure}", kpiId, legacy, result.Definition, result.Error);
        }

        if (results.Count > 0)
            logger.LogInformation("Definition parity: {Matched}/{Total} migrated KPIs match", results.Count(r => r.Matches), results.Count);

        lock (_gate) _last = results;
        return results;
    }
}

public sealed record KpiParityResult(string KpiId, long Legacy, long? Definition, string? Error)
{
    public bool Matches => Error is null && Definition == Legacy;
}
