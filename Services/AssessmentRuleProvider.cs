using ActiveRolesDashboard.Models;

namespace ActiveRolesDashboard.Services;

/// <summary>Supplies the effective assessment rules (built-in rules with active update packs applied).</summary>
public interface IAssessmentRuleProvider
{
    /// <summary>The current effective rule set, including pack status and fingerprint.</summary>
    AssessmentRuleSet Current { get; }

    /// <summary>Effective rules that apply to <paramref name="type"/>.</summary>
    IEnumerable<AssessmentRule> ForType(AssessmentType type);

    /// <summary>True when any effective rule for <paramref name="type"/> depends on Entra group membership data.</summary>
    bool DependsOnEntraGroupMembership(AssessmentType type);
}

/// <summary>
/// Caches the effective rule set built by <see cref="AssessmentUpdateStore"/> and rebuilds it
/// whenever packs change. If building fails the built-in rules are used, so assessments always run.
/// </summary>
public sealed class AssessmentRuleProvider : IAssessmentRuleProvider
{
    private readonly AssessmentUpdateStore _store;
    private readonly ILogger<AssessmentRuleProvider>? _logger;
    private readonly object _lock = new();
    private AssessmentRuleSet? _current;

    public AssessmentRuleProvider(AssessmentUpdateStore store, ILogger<AssessmentRuleProvider>? logger = null)
    {
        _store = store;
        _logger = logger;
        _store.Changed += Invalidate;
    }

    public AssessmentRuleSet Current
    {
        get
        {
            var current = _current;
            if (current != null)
                return current;
            lock (_lock)
                return _current ??= Build();
        }
    }

    public IEnumerable<AssessmentRule> ForType(AssessmentType type) =>
        Current.Rules.Where(r => r.Types.Contains(type));

    public bool DependsOnEntraGroupMembership(AssessmentType type) =>
        ForType(type).Any(r => KpiInfo.EntraMembershipDependentKeys.Contains(r.KpiKey));

    public void Invalidate()
    {
        lock (_lock)
            _current = null;
    }

    private AssessmentRuleSet Build()
    {
        try
        {
            var set = _store.BuildRuleSet();
            foreach (var invalid in set.Packs.Where(p => p.Status == AssessmentUpdatePackStatus.Invalid))
                _logger?.LogWarning("Assessment update pack {PackId} is invalid and was not applied: {Errors}",
                    invalid.State.Id, string.Join("; ", invalid.Errors));
            _logger?.LogInformation("Assessment rule set built: {RuleCount} rules, {ActivePacks} active update pack(s), fingerprint {Fingerprint}.",
                set.Rules.Count, set.ActivePacks.Count(), set.Fingerprint);
            return set;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _logger?.LogError(ex, "Failed to load assessment update packs; using built-in assessment rules.");
            return AssessmentRuleSetBuilder.Build(AssessmentRuleLibrary.All, Array.Empty<(AssessmentUpdatePackState, string?)>());
        }
    }
}

/// <summary>Provider returning only the built-in rules (used when no update store is configured).</summary>
public sealed class BuiltInAssessmentRuleProvider : IAssessmentRuleProvider
{
    public static readonly BuiltInAssessmentRuleProvider Instance = new();

    private readonly Lazy<AssessmentRuleSet> _set = new(() =>
        AssessmentRuleSetBuilder.Build(AssessmentRuleLibrary.All, Array.Empty<(AssessmentUpdatePackState, string?)>()));

    public AssessmentRuleSet Current => _set.Value;
    public IEnumerable<AssessmentRule> ForType(AssessmentType type) => AssessmentRuleLibrary.ForType(type);
    public bool DependsOnEntraGroupMembership(AssessmentType type) => AssessmentRuleLibrary.DependsOnEntraGroupMembership(type);
}
