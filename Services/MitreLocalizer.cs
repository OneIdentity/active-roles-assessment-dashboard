using System.Globalization;
using ActiveRolesDashboard.Models;
using Microsoft.Extensions.Localization;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// Marker type for the shared MITRE ATT&amp;CK resource set
/// (Resources/MitreResources.resx and its culture-specific siblings).
/// </summary>
public sealed class MitreResources
{
}

/// <summary>
/// Static bridge that localizes MITRE tactic names and technique names, descriptions and
/// mitigations at render time. The technique catalogue in <c>Models/MitreModels.cs</c> holds
/// the English text, which is used as the fallback when no translation exists for the
/// current UI culture.
/// </summary>
public static class MitreLocalizer
{
    private static System.Resources.ResourceManager? _resources;

    /// <summary>Wires the shared resource manager. Call once during application startup.</summary>
    /// <remarks>
    /// Binds directly to the embedded resource base name
    /// <c>ActiveRolesDashboard.Resources.MitreResources</c> rather than relying on
    /// <see cref="IStringLocalizerFactory"/> base-name derivation, which does not match the
    /// compiled resource name and silently falls back to English.
    /// </remarks>
    public static void Initialize(IStringLocalizerFactory factory)
    {
        _resources = new System.Resources.ResourceManager(
            "ActiveRolesDashboard.Resources.MitreResources",
            typeof(MitreResources).Assembly);
    }

    public static string TacticName(MitreTactic tactic) =>
        Localize($"Tactic_{tactic}", MitreTacticInfo.DisplayName(tactic));

    public static string TechniqueName(string techniqueId, string fallback) =>
        Localize($"Tech_{techniqueId}_Name", fallback);

    public static string TechniqueDescription(string techniqueId, string fallback) =>
        Localize($"Tech_{techniqueId}_Desc", fallback);

    public static string TechniqueMitigation(string techniqueId, string fallback) =>
        Localize($"Tech_{techniqueId}_Mit", fallback);

    public static string Rationale(string techniqueId, string kpiKey, string fallback) =>
        Localize($"Rat_{techniqueId}_{kpiKey}", fallback);

    private static string Localize(string key, string fallback)
    {
        if (_resources is null)
            return fallback;

        var value = _resources.GetString(key, CultureInfo.CurrentUICulture);
        return string.IsNullOrEmpty(value) ? fallback : value;
    }
}
