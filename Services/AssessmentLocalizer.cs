using System.Globalization;
using System.Text;
using Microsoft.Extensions.Localization;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// Marker type used to locate the shared assessment resource set
/// (Resources/AssessmentResources.resx and its culture-specific siblings).
/// </summary>
public sealed class AssessmentResources
{
}

/// <summary>
/// Static bridge that localizes assessment rule titles, recommendations and
/// category names at render time.
///
/// Assessment results are persisted with the English literals produced by
/// <c>AssessmentRuleLibrary</c>, but each check/comparison row also stores a
/// stable <c>RuleId</c>. By looking up <c>RuleTitle_{ruleId}</c> /
/// <c>RuleRec_{ruleId}</c> (and <c>Cat_{category}</c>) against
/// <see cref="CultureInfo.CurrentUICulture"/> at display time, historical runs
/// render in the viewer's current language without re-generating them.
///
/// The underlying <see cref="IStringLocalizer"/> is set once at startup via
/// <see cref="Initialize"/>. When no localizer is configured, or the key has no
/// translation for the active culture, the supplied literal fallback (the
/// persisted English text) is returned so nothing regresses.
/// </summary>
public static class AssessmentLocalizer
{
    private static System.Resources.ResourceManager? _resources;

    /// <summary>Wires the shared resource manager. Call once during application startup.</summary>
    /// <remarks>
    /// Binds directly to the embedded resource base name
    /// <c>ActiveRolesDashboard.Resources.AssessmentResources</c> via a
    /// <see cref="System.Resources.ResourceManager"/>. This avoids the
    /// <see cref="IStringLocalizerFactory"/> base-name derivation (which combines the marker
    /// type's namespace with ResourcesPath) not matching the actual compiled resource name and
    /// silently falling back to English. The <paramref name="factory"/> parameter is retained for
    /// call-site compatibility with the startup wiring.
    /// </remarks>
    public static void Initialize(IStringLocalizerFactory factory)
    {
        _resources = new System.Resources.ResourceManager(
            "ActiveRolesDashboard.Resources.AssessmentResources",
            typeof(AssessmentResources).Assembly);
    }

    /// <summary>Localized rule title for <paramref name="ruleId"/>, falling back to <paramref name="fallback"/>.</summary>
    public static string Title(string ruleId, string fallback) =>
        Localize($"RuleTitle_{ruleId}", fallback);

    /// <summary>Localized rule recommendation for <paramref name="ruleId"/>, falling back to <paramref name="fallback"/>.</summary>
    public static string Recommendation(string ruleId, string fallback) =>
        Localize($"RuleRec_{ruleId}", fallback);

    /// <summary>
    /// Title for a check, honouring update-pack text: pack text for the current culture (or its
    /// parent) wins; when a pack changed the title but has no text for the culture, the persisted
    /// title is used instead of a resource translation that may describe the old rule.
    /// </summary>
    public static string Title(ActiveRolesDashboard.Models.AssessmentCheck? check, string ruleId, string fallback) =>
        check == null ? Title(ruleId, fallback)
        : PackText(check, t => t.Title) ?? (check.TitleFromUpdate ? fallback : Title(ruleId, fallback));

    /// <summary>Title for a check (see <see cref="Title(ActiveRolesDashboard.Models.AssessmentCheck?, string, string)"/>).</summary>
    public static string Title(ActiveRolesDashboard.Models.AssessmentCheck check) => Title(check, check.RuleId, check.Title);

    /// <summary>Recommendation for a check, with the same update-pack precedence as titles.</summary>
    public static string Recommendation(ActiveRolesDashboard.Models.AssessmentCheck check) =>
        PackText(check, t => t.Recommendation)
        ?? (check.RecommendationFromUpdate ? check.Recommendation : Recommendation(check.RuleId, check.Recommendation));

    private static string? PackText(ActiveRolesDashboard.Models.AssessmentCheck check, Func<ActiveRolesDashboard.Models.AssessmentUpdates.AssessmentRuleText, string?> pick)
    {
        if (check.LocalizedText is not { Count: > 0 } texts)
            return null;
        for (var culture = CultureInfo.CurrentUICulture; !string.IsNullOrEmpty(culture.Name); culture = culture.Parent)
        {
            var match = texts.FirstOrDefault(kv => string.Equals(kv.Key, culture.Name, StringComparison.OrdinalIgnoreCase));
            if (match.Value != null && !string.IsNullOrWhiteSpace(pick(match.Value)))
                return pick(match.Value);
        }
        return null;
    }

    /// <summary>Localized category display name, falling back to the original <paramref name="categoryName"/>.</summary>
    public static string Category(string categoryName) =>
        Localize($"Cat_{NormalizeCategoryKey(categoryName)}", categoryName);

    /// <summary>
    /// Returns the localized string for <paramref name="key"/> in the current UI culture,
    /// or <paramref name="fallback"/> when unavailable.
    /// </summary>
    public static string Localize(string key, string fallback)
    {
        if (_resources is null || string.IsNullOrEmpty(key))
            return fallback;

        var value = _resources.GetString(key, CultureInfo.CurrentUICulture);
        return string.IsNullOrEmpty(value) ? fallback : value;
    }

    /// <summary>
    /// Turns a human-readable category name (e.g. "CAF B2 Identity &amp; Access Control")
    /// into a stable resource-key suffix by keeping only letters and digits.
    /// </summary>
    private static string NormalizeCategoryKey(string categoryName)
    {
        if (string.IsNullOrEmpty(categoryName))
            return string.Empty;

        var sb = new StringBuilder(categoryName.Length);
        foreach (var ch in categoryName)
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
        }
        return sb.ToString();
    }
}
