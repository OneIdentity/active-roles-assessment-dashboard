using System.Security.Claims;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Services.Reporting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using QuestPDF.Infrastructure;

// QuestPDF Community license (free for organizations under the revenue threshold).
QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ActiveRolesConfig>(builder.Configuration.GetSection("ActiveRoles"));

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

builder.Services.AddRazorPages(options =>
{
    options.Conventions.ConfigureFilter(new Microsoft.AspNetCore.Mvc.IgnoreAntiforgeryTokenAttribute());
});
builder.Services.AddControllers();

var app = builder.Build();

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

app.MapRazorPages();
app.MapControllers();

app.Run();
