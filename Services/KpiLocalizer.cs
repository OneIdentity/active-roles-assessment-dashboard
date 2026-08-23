using System.Globalization;
using Microsoft.Extensions.Localization;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// Marker type used to locate the shared KPI/category resource set
/// (Resources/KpiResources.resx and its culture-specific siblings).
/// </summary>
public sealed class KpiResources
{
}

/// <summary>
/// Static bridge that lets the KPI / category definitions in
/// <c>Models/DashboardModels.cs</c> (which are <c>static readonly</c> and therefore
/// have no access to DI) resolve their display names from resources at read-time.
///
/// The underlying <see cref="IStringLocalizer"/> is set once at startup via
/// <see cref="Initialize"/>. Lookups resolve against
/// <see cref="CultureInfo.CurrentUICulture"/>, which the request-localization
/// middleware sets per request. When no localizer is configured, or the key has no
/// translation for the active culture, the supplied literal fallback is returned.
/// </summary>
public static class KpiLocalizer
{
    private static System.Resources.ResourceManager? _resources;

    /// <summary>Wires the shared resource manager. Call once during application startup.</summary>
    /// <remarks>
    /// Binds directly to the embedded resource base name
    /// <c>ActiveRolesDashboard.Resources.KpiResources</c> via a
    /// <see cref="System.Resources.ResourceManager"/> so lookups match the compiled resource name
    /// exactly, rather than relying on the <see cref="IStringLocalizerFactory"/> deriving the base
    /// name from the marker type's namespace and ResourcesPath. The <paramref name="factory"/>
    /// parameter is retained for call-site compatibility with the startup wiring.
    /// </remarks>
    public static void Initialize(IStringLocalizerFactory factory)
    {
        _resources = new System.Resources.ResourceManager(
            "ActiveRolesDashboard.Resources.KpiResources",
            typeof(KpiResources).Assembly);
    }

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
}
