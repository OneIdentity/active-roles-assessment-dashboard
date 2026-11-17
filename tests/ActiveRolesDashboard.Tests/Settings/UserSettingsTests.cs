using System.Globalization;
using System.Security.Claims;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace ActiveRolesDashboard.Tests.Settings;

/// <summary>
/// Per-user settings persistence (file naming, isolation, defaults, malformed files) and the
/// request-culture provider that applies the saved language, login cookie and configured default.
/// </summary>
[Trait("Stage", "Settings")]
public class UserSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ard-settings-" + Guid.NewGuid().ToString("N"));

    private string Folder => Path.Combine(_root, "usersettings");

    private UserSettingsService Service()
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.ContentRootPath.Returns(_root);
        return new UserSettingsService(env);
    }

    // ---------------------------------------------------------------- persistence

    [Fact]
    public void Constructor_creates_settings_folder()
    {
        Service();

        Assert.True(Directory.Exists(Folder));
    }

    [Fact]
    public void Unknown_user_gets_defaults()
    {
        var s = Service().Load(@"TEST\nobody");

        Assert.Equal(SupportedLanguage.DefaultCode, s.Language);
        Assert.Equal(0, s.AutoRefreshMinutes);
        Assert.NotNull(s.KpiSettings);
    }

    [Fact]
    public void Settings_round_trip()
    {
        var svc = Service();
        svc.Save(@"TEST\alice", new UserSettings { AutoRefreshMinutes = 15, Language = "fr" });

        var s = svc.Load(@"TEST\alice");

        Assert.Equal(15, s.AutoRefreshMinutes);
        Assert.Equal("fr", s.Language);
    }

    [Fact]
    public void Users_are_isolated()
    {
        var svc = Service();
        svc.Save(@"TEST\alice", new UserSettings { Language = "de" });
        svc.Save(@"TEST\bob", new UserSettings { Language = "hu" });

        Assert.Equal("de", svc.Load(@"TEST\alice").Language);
        Assert.Equal("hu", svc.Load(@"TEST\bob").Language);
    }

    [Theory]
    [InlineData(@"TEST\alice", "TEST_alice.json")]
    [InlineData("alice@test.local", "alice@test.local.json")]
    [InlineData("a:b*c?d\"e<f>g|h/i", "a_b_c_d_e_f_g_h_i.json")]
    public void Username_is_sanitized_into_a_safe_file_name(string user, string expected)
    {
        Service().Save(user, new UserSettings());

        Assert.True(File.Exists(Path.Combine(Folder, expected)), string.Join(", ", Directory.GetFiles(Folder).Select(Path.GetFileName)));
    }

    [Theory]
    [InlineData(@"..\..\evil")]
    [InlineData("../../evil")]
    [InlineData(@"C:\Windows\evil")]
    public void Path_traversal_usernames_stay_inside_the_settings_folder(string user)
    {
        Service().Save(user, new UserSettings());

        Assert.Single(Directory.GetFiles(Folder));
        Assert.Empty(Directory.GetFiles(_root));
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "evil.json")));
    }

    [Fact]
    public void Overwrite_replaces_previous_settings()
    {
        var svc = Service();
        svc.Save(@"TEST\alice", new UserSettings { AutoRefreshMinutes = 5, Language = "es" });
        svc.Save(@"TEST\alice", new UserSettings { AutoRefreshMinutes = 0, Language = "en" });

        var s = svc.Load(@"TEST\alice");

        Assert.Equal(0, s.AutoRefreshMinutes);
        Assert.Equal("en", s.Language);
    }

    [Fact]
    public void Json_null_file_yields_defaults()
    {
        var svc = Service();
        File.WriteAllText(Path.Combine(Folder, "TEST_alice.json"), "null");

        Assert.Equal(SupportedLanguage.DefaultCode, svc.Load(@"TEST\alice").Language);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("[1,2,3]")]
    public void Malformed_file_yields_defaults(string content)
    {
        var svc = Service();
        File.WriteAllText(Path.Combine(Folder, "TEST_alice.json"), content);

        var s = svc.Load(@"TEST\alice");

        Assert.Equal(SupportedLanguage.DefaultCode, s.Language);
    }

    [Fact]
    public void Unknown_properties_are_ignored()
    {
        var svc = Service();
        File.WriteAllText(Path.Combine(Folder, "TEST_alice.json"), "{\"Language\":\"it\",\"Future\":42}");

        Assert.Equal("it", svc.Load(@"TEST\alice").Language);
    }

    // ---------------------------------------------------------------- request culture

    private HttpContext Context(string? user = null, string? cookieCulture = null, string? defaultLanguage = null)
    {
        var services = new ServiceCollection()
            .AddSingleton(Service())
            .AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ActiveRoles:DefaultLanguage"] = defaultLanguage })
                .Build())
            .BuildServiceProvider();

        var http = new DefaultHttpContext { RequestServices = services };
        if (user != null)
            http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, user)], "test"));
        if (cookieCulture != null)
            http.Request.Headers.Cookie = $"{CookieRequestCultureProvider.DefaultCookieName}={Uri.EscapeDataString(CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(cookieCulture)))}";
        return http;
    }

    private static async Task<string?> Culture(HttpContext http) =>
        (await new UserSettingsRequestCultureProvider().DetermineProviderCultureResult(http))?.UICultures[0].Value;

    [Fact]
    public async Task Saved_user_language_wins_over_cookie_and_default()
    {
        Service().Save(@"TEST\alice", new UserSettings { Language = "de" });

        Assert.Equal("de", await Culture(Context(@"TEST\alice", cookieCulture: "fr", defaultLanguage: "it")));
    }

    [Fact]
    public async Task Unsupported_saved_language_falls_back_to_default()
    {
        Service().Save(@"TEST\alice", new UserSettings { Language = "xx" });

        Assert.Equal("it", await Culture(Context(@"TEST\alice", defaultLanguage: "it")));
    }

    [Fact]
    public async Task Anonymous_with_supported_cookie_defers_to_cookie_provider()
    {
        Assert.Null(await Culture(Context(cookieCulture: "fr", defaultLanguage: "it")));
    }

    [Fact]
    public async Task Anonymous_with_unsupported_cookie_uses_default()
    {
        Assert.Equal("it", await Culture(Context(cookieCulture: "ja", defaultLanguage: "it")));
    }

    [Fact]
    public async Task Anonymous_uses_configured_default_language()
    {
        Assert.Equal("es", await Culture(Context(defaultLanguage: "es")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("klingon")]
    [InlineData("FR")]
    public async Task Missing_or_unsupported_default_defers(string? defaultLanguage)
    {
        Assert.Null(await Culture(Context(defaultLanguage: defaultLanguage)));
    }

    [Fact]
    public async Task Malformed_user_settings_do_not_break_culture_resolution()
    {
        Service();
        File.WriteAllText(Path.Combine(Folder, "TEST_alice.json"), "{not json");

        Assert.Equal(SupportedLanguage.DefaultCode, await Culture(Context(@"TEST\alice", defaultLanguage: "es")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
        _ = CultureInfo.InvariantCulture;
    }
}
