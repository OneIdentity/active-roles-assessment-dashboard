using System.Linq;
using ActiveRolesDashboard.Models;
using Microsoft.AspNetCore.Localization;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// Resolves the request culture in priority order:
/// 1. the authenticated user's saved language (usersettings),
/// 2. for unauthenticated requests, an explicit culture cookie (set by the
///    login-page language selector) is honoured via the built-in cookie provider,
/// 3. the configured <c>ActiveRoles:DefaultLanguage</c>,
/// 4. otherwise defers to the remaining providers / the default request culture.
/// Only languages present in <see cref="SupportedLanguage.All"/> are honoured.
/// </summary>
public class UserSettingsRequestCultureProvider : RequestCultureProvider
{
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        // Authenticated users: their saved language always wins.
        var userLanguage = ResolveUserLanguage(httpContext);
        if (userLanguage is not null)
            return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(userLanguage, userLanguage));

        // Unauthenticated users (e.g. the login page): honour an explicit culture
        // cookie so a login-page language selection takes effect. Returning null
        // defers to the built-in CookieRequestCultureProvider that follows.
        if (HasCultureCookie(httpContext))
            return Task.FromResult<ProviderCultureResult?>(null);

        // Otherwise fall back to the configured default language.
        var code = ResolveDefaultLanguage(httpContext);
        if (code is null)
            return Task.FromResult<ProviderCultureResult?>(null);

        return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(code, code));
    }

    private static bool HasCultureCookie(HttpContext httpContext)
    {
        var cookie = httpContext.Request.Cookies[Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.DefaultCookieName];
        if (string.IsNullOrEmpty(cookie))
            return false;

        var parsed = Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.ParseCookieValue(cookie);
        return parsed is not null
            && parsed.UICultures.Count > 0
            && IsSupported(parsed.UICultures[0].Value);
    }

    private static string? ResolveUserLanguage(HttpContext httpContext)
    {
        var username = httpContext.User?.Identity?.Name;
        if (string.IsNullOrEmpty(username))
            return null;

        var settingsService = httpContext.RequestServices.GetService<UserSettingsService>();
        var language = settingsService?.Load(username).Language;
        return IsSupported(language) ? language : null;
    }

    private static string? ResolveDefaultLanguage(HttpContext httpContext)
    {
        var config = httpContext.RequestServices.GetService<IConfiguration>();
        var defaultLanguage = config?["ActiveRoles:DefaultLanguage"];
        return IsSupported(defaultLanguage) ? defaultLanguage : null;
    }

    private static bool IsSupported(string? code) =>
        !string.IsNullOrWhiteSpace(code) && SupportedLanguage.All.Any(l => l.Code == code);
}
