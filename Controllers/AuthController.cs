using System.Globalization;
using System.Security.Claims;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Resources;
using ActiveRolesDashboard.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace ActiveRolesDashboard.Controllers;

/// <summary>
/// REST authentication API that backs the Angular SPA. Replaces the former
/// Razor <c>Pages/Login.cshtml.cs</c> and <c>Pages/Logout.cshtml.cs</c>,
/// preserving their behaviour exactly: RSTS token acquisition, session state,
/// a fresh directory-facts evaluation at login (with per-user cache clear when
/// the resolved role/admin flag changed), claims + cookie sign-in, and culture
/// cookie alignment to the authenticated user's saved language.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly RstsAuthService _authService;
    private readonly IStringLocalizer<AuthMessages> _localizer;
    private readonly UserSettingsService _userSettings;
    private readonly DashboardCacheHolder _cache;
    private readonly DirectoryFactsResolver _directoryFacts;
    private readonly PerUserSummaryCache _userCache;

    public AuthController(
        RstsAuthService authService,
        IStringLocalizer<AuthMessages> localizer,
        UserSettingsService userSettings,
        DashboardCacheHolder cache,
        DirectoryFactsResolver directoryFacts,
        PerUserSummaryCache userCache)
    {
        _authService = authService;
        _localizer = localizer;
        _userSettings = userSettings;
        _cache = cache;
        _directoryFacts = directoryFacts;
        _userCache = userCache;
    }

    public sealed class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? ReturnUrl { get; set; }
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return Unauthorized(new { error = _localizer["UsernamePasswordRequired"].Value });
        }

        var tokenResult = await _authService.GetTokenAsync(request.Username, request.Password);

        if (!tokenResult.Success)
        {
            return Unauthorized(new { error = tokenResult.Error ?? _localizer["AuthenticationFailed"].Value });
        }

        // Store token in session to avoid cookie size limits truncating the token.
        HttpContext.Session.SetString("AccessToken", tokenResult.AccessToken);
        HttpContext.Session.SetString("TokenExpiry", DateTime.UtcNow.AddSeconds(tokenResult.ExpiresIn).ToString("o"));

        // Evaluate the user's directory facts (Active Roles admin flag + dashboard role) at login.
        // Unlike the lazy per-request path, this forces a FRESH directory evaluation (bypassing the
        // app-scope fact cache) so a role-group membership change made between logins is picked up
        // immediately. A single group-graph enumeration is shared across the admin and role checks.
        // The fresh results are cached at app scope and mirrored into session, tagged with the
        // directory-facts epoch so a superset rebuild still forces a re-evaluation on the user's
        // next request.
        var fresh = await _directoryFacts.ResolveFreshAsync(tokenResult.AccessToken, request.Username);
        var facts = fresh.Facts;

        // If the resolved role/admin flag changed since the user last logged in, discard their
        // role-scoped cached data blobs so the dashboard rebuilds from the shared superset under the
        // new role on the first request. The freshly resolved facts themselves are retained.
        if (fresh.Changed)
        {
            _userCache.ClearUserData(request.Username);
        }

        HttpContext.Session.SetString("IsActiveRolesAdmin", facts.IsActiveRolesAdmin.ToString());
        HttpContext.Session.SetString("DashboardRole", facts.Role.ToString());
        HttpContext.Session.SetString("DirectoryFactsEpoch", facts.Epoch.ToString());

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, request.Username)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = false, RedirectUri = null, ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(tokenResult.ExpiresIn) });

        // Resolve the newly authenticated user's saved language. Once authenticated this is the
        // single source of truth, so align the culture cookie with it (rather than the pre-auth
        // login-page selection) and localize the post-login overlay message in that culture.
        var userLanguage = ResolveUserLanguage(request.Username);
        if (userLanguage is not null)
        {
            Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(userLanguage)),
                new CookieOptions { Path = string.IsNullOrEmpty(Request.PathBase) ? "/" : Request.PathBase.Value, MaxAge = TimeSpan.FromDays(365) });
        }
        else
        {
            Response.Cookies.Delete(CookieRequestCultureProvider.DefaultCookieName);
        }

        var pathBase = Request.PathBase.Value ?? string.Empty;
        var redirectUrl = ResolveRedirectUrl(request.ReturnUrl, pathBase);

        return Ok(new
        {
            redirectUrl,
            loadingMessage = LocalizeInCulture("LoadingData", userLanguage)
        });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        HttpContext.Session.Clear();
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        var pathBase = Request.PathBase.Value ?? string.Empty;
        return Ok(new { redirectUrl = $"{pathBase}/login" });
    }

    /// <summary>
    /// Only honour a local, same-application return URL to avoid open-redirects.
    /// Falls back to the Angular dashboard under the current PathBase.
    /// </summary>
    private string ResolveRedirectUrl(string? returnUrl, string pathBase)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return returnUrl;
        }
        return $"{pathBase}/dashboard";
    }

    private string? ResolveUserLanguage(string username)
    {
        if (string.IsNullOrEmpty(username))
            return null;

        var language = _userSettings.Load(username).Language;
        return SupportedLanguage.All.Any(l => l.Code == language) ? language : null;
    }

    private string LocalizeInCulture(string key, string? cultureCode)
    {
        if (string.IsNullOrEmpty(cultureCode))
            return _localizer[key];

        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureCode);
            return _localizer[key];
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
