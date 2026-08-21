using System.Security.Claims;
using ActiveRolesDashboard.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ActiveRolesDashboard.Pages;

public class LoginModel : PageModel
{
    private readonly RstsAuthService _authService;

    public LoginModel(RstsAuthService authService)
    {
        _authService = authService;
    }

    [BindProperty]
    public string Username { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "Username and password are required.";
            return Page();
        }

        var tokenResult = await _authService.GetTokenAsync(Username, Password);

        if (!tokenResult.Success)
        {
            ErrorMessage = tokenResult.Error ?? "Authentication failed.";
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

        RedirectUrl = $"{Request.PathBase}/";
        return Page();
    }

    public string? RedirectUrl { get; set; }
}
