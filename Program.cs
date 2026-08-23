using System.Security.Claims;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Services.Reporting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using QuestPDF.Infrastructure;

// QuestPDF Community license (free for organizations under the revenue threshold).
QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ActiveRolesConfig>(builder.Configuration.GetSection("ActiveRoles"));

// Data Protection: persist the key ring under the content root so the encrypted
// service-account secret can be decrypted across restarts and deployments. The service
// account password is stored encrypted in appsettings (never plaintext) and unprotected
// at runtime via ServiceAccountSecretProtector.
var dataProtectionKeysPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys");
Directory.CreateDirectory(dataProtectionKeysPath);
builder.Services.AddDataProtection()
    .SetApplicationName("ActiveRolesDashboard")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));

builder.Services.AddSingleton<ActiveRolesDashboard.Services.ServiceAccountSecretProtector>();

var arConfig = builder.Configuration.GetSection("ActiveRoles").Get<ActiveRolesConfig>()!;

builder.Services.AddHttpClient("RSTS")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = arConfig.IgnoreSslErrors
            ? HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            : null
    });

builder.Services.AddHttpClient("ActiveRolesApi")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = arConfig.IgnoreSslErrors
            ? HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            : null
    });

builder.Services.AddSingleton<RstsAuthService>();
builder.Services.AddSingleton<ActiveRolesService>();
builder.Services.AddSingleton<UserSettingsService>();
builder.Services.AddSingleton<SnapshotService>();
builder.Services.AddSingleton<AssessmentService>();
builder.Services.AddSingleton<MitreExposureService>();

// Shared-superset cache, service-account collection, and per-user AR permission filtering.
builder.Services.AddSingleton<DashboardCacheHolder>();
builder.Services.AddSingleton<ServiceAccountTokenProvider>();
builder.Services.AddSingleton<ArPermissionModelService>();
builder.Services.AddSingleton<SupersetLoaderHostedService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SupersetLoaderHostedService>());

// Reporting / export services.
builder.Services.AddSingleton<ReportBuilder>();
builder.Services.AddSingleton<AssessmentReportBuilder>();
builder.Services.AddSingleton<IReportExporter, PdfReportExporter>();
builder.Services.AddSingleton<IReportExporter, WordReportExporter>();
builder.Services.AddSingleton<IReportExporter, ExcelReportExporter>();
builder.Services.AddSingleton<ReportExporterFactory>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.LogoutPath = "/Logout";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.Cookie.Path = "/";
        options.Events.OnRedirectToLogin = context =>
        {
            var returnUrl = context.Request.Path + context.Request.QueryString;
            var pathBase = context.Request.PathBase.Value ?? "";
            if (returnUrl == "" || returnUrl == "/")
            {
                returnUrl = "/";
            }
            var loginUrl = $"{pathBase}/Login?ReturnUrl={Uri.EscapeDataString(pathBase + returnUrl)}";
            context.Response.Redirect(loginUrl);
            return Task.CompletedTask;
        };
        options.Events.OnSigningIn = context =>
        {
            context.CookieOptions.Path = "/";
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToReturnUrl = context =>
        {
            // Suppress auto-redirect; let the login handler control navigation.
            return Task.CompletedTask;
        };
    });

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Path = "/";
});

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddRazorPages(options =>
{
    options.Conventions.ConfigureFilter(new Microsoft.AspNetCore.Mvc.IgnoreAntiforgeryTokenAttribute());
}).AddViewLocalization();
builder.Services.AddControllers();

// Localization: supported UI cultures. English only for now; add cultures here as
// translations become available. The active culture is chosen per-user (see below).
builder.Services.Configure<Microsoft.AspNetCore.Builder.RequestLocalizationOptions>(options =>
{
    var supported = ActiveRolesDashboard.Models.SupportedLanguage.All
        .Select(l => new System.Globalization.CultureInfo(l.Code))
        .ToList();
    options.DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(
        ActiveRolesDashboard.Models.SupportedLanguage.DefaultCode);
    options.SupportedCultures = supported;
    options.SupportedUICultures = supported;

    // Resolve culture from the authenticated user's saved language, then the
    // configured default, before falling back to the built-in providers.
    options.RequestCultureProviders.Insert(0,
        new ActiveRolesDashboard.Services.UserSettingsRequestCultureProvider());
});

var app = builder.Build();

// One-time secret protection utility:
//   dotnet run -- --protect-secret [plaintext]
// Encrypts the given service-account password using Data Protection and prints the value
// to paste into appsettings under ActiveRoles:ServiceAccount:ProtectedPassword. Exits without
// starting the web host so the plaintext is never served or logged by the running app.
if (args.Contains("--protect-secret", StringComparer.OrdinalIgnoreCase))
{
    var protector = app.Services.GetRequiredService<ActiveRolesDashboard.Services.ServiceAccountSecretProtector>();

    var idx = Array.FindIndex(args, a => string.Equals(a, "--protect-secret", StringComparison.OrdinalIgnoreCase));
    var plaintext = idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
    if (string.IsNullOrEmpty(plaintext))
    {
        Console.Write("Enter service-account password to protect: ");
        plaintext = Console.ReadLine();
    }

    if (string.IsNullOrEmpty(plaintext))
    {
        Console.Error.WriteLine("No password provided. Nothing to protect.");
        return;
    }

    var encrypted = protector.Protect(plaintext);
    Console.WriteLine();
    Console.WriteLine("Protected password (copy into appsettings ActiveRoles:ServiceAccount:ProtectedPassword):");
    Console.WriteLine(encrypted);
    return;
}

// Wire the static KPI/category localizer so KpiInfo/CategoryInfo display names
// (which are static readonly and cannot use DI) resolve from resources at read-time.
ActiveRolesDashboard.Services.KpiLocalizer.Initialize(
    app.Services.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizerFactory>());

// Wire the static assessment localizer so persisted rule titles/recommendations/categories
// (identified by RuleId) resolve from resources at render-time in the current UI culture.
ActiveRolesDashboard.Services.AssessmentLocalizer.Initialize(
    app.Services.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizerFactory>());

// PathBase: set manually for reverse-proxy/Kestrel scenarios.
// IIS in-process/out-of-process hosting sets PathBase automatically for sub-applications.
var pathBase = app.Configuration["PathBase"];
if (!string.IsNullOrEmpty(pathBase))
{
    app.UsePathBase(pathBase);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
// Must run after authentication so the culture provider can read the
// authenticated user's saved language from their user settings.
app.UseRequestLocalization(app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Builder.RequestLocalizationOptions>>().Value);

// First-run redirect: if ApiBaseUrl is not configured, send to Setup wizard
app.Use(async (context, next) =>
{
    var config = context.RequestServices.GetRequiredService<IOptionsMonitor<ActiveRolesConfig>>().CurrentValue;
    var path = context.Request.Path.Value ?? "";
    if (string.IsNullOrWhiteSpace(config.ApiBaseUrl) && !path.StartsWith("/Setup", StringComparison.OrdinalIgnoreCase) && !path.StartsWith("/css", StringComparison.OrdinalIgnoreCase) && !path.StartsWith("/js", StringComparison.OrdinalIgnoreCase) && !path.StartsWith("/lib", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Redirect($"{context.Request.PathBase}/Setup");
        return;
    }
    await next();
});

// Cache readiness endpoint: polled by the login/wait screen while the shared superset is
// being built at startup. Returns the current cache state so the UI can show "Building cache…"
// until Ready. Anonymous by design (no user data is exposed, only lifecycle status).
app.MapGet("/cache/status", (DashboardCacheHolder cache) => Results.Json(new
{
    state = cache.State.ToString(),
    ready = cache.IsReady,
    collectedAtUtc = cache.CollectedAtUtc,
    error = cache.LastError
})).AllowAnonymous();

app.MapRazorPages();
app.MapControllers();

app.Run();
