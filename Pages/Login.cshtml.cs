using System.Globalization;
using System.Security.Claims;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace ActiveRolesDashboard.Pages;

public class LoginModel : PageModel
{
    private readonly RstsAuthService _authService;
    private readonly IStringLocalizer<LoginModel> _localizer;
    private readonly UserSettingsService _userSettings;

    public LoginModel(RstsAuthService authService, IStringLocalizer<LoginModel> localizer, UserSettingsService userSettings)
    {
        _authService = authService;
        _localizer = localizer;
        _userSettings = userSettings;
    }

    [BindProperty]
    public string Username { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public IReadOnlyList<SupportedLanguage> Languages { get; } = SupportedLanguage.All;

    public SupportedLanguage SelectedLanguage =>
        Languages.FirstOrDefault(l => l.Code == CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
        ?? Languages.First(l => l.Code == SupportedLanguage.DefaultCode);

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = _localizer["UsernamePasswordRequired"];
            return Page();
        }

        var tokenResult = await _authService.GetTokenAsync(Username, Password);

        if (!tokenResult.Success)
        {
            ErrorMessage = tokenResult.Error ?? _localizer["AuthenticationFailed"];
            return Page();
        }

        // Store token in session to avoid cookie size limits truncating the token
        HttpContext.Session.SetString("AccessToken", tokenResult.AccessToken);
        HttpContext.Session.SetString("TokenExpiry", DateTime.UtcNow.AddSeconds(tokenResult.ExpiresIn).ToString("o"));

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, Username)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = false, RedirectUri = null, ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(tokenResult.ExpiresIn) });

        // Resolve the newly authenticated user's saved language. Once authenticated this is the
        // single source of truth, so align the culture cookie with it (rather than the pre-auth
        // login-page selection) and localize the post-login overlay message in that culture.
        var userLanguage = ResolveUserLanguage(Username);
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

        // No-JS fallback: full-page form POST expects an HTML redirect page rather than JSON.
        if (!AcceptsJson())
        {
            RedirectUrl = $"{Request.PathBase}/";
            return Page();
        }

        return new JsonResult(new
        {
            redirectUrl = $"{Request.PathBase}/",
            loadingMessage = LocalizeInCulture("LoadingData", userLanguage)
        });
    }

    private bool AcceptsJson()
    {
        var accept = Request.Headers.Accept.ToString();
        return accept.Contains("application/json", StringComparison.OrdinalIgnoreCase);
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

    public string? RedirectUrl { get; set; }
}
