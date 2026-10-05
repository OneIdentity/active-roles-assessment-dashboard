using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Controllers;

/// <summary>
/// REST API that backs the Angular first-run setup wizard. Replaces the former
/// Razor <c>Pages/Setup.cshtml(.cs)</c>, preserving its behaviour exactly:
/// surfaces current defaults/options for the wizard (<see cref="Get"/>) and
/// validates + persists the collected configuration into the active appsettings
/// file, protecting the service-account password, reloading configuration and
/// triggering the initial superset build (<see cref="Save"/>).
///
/// Anonymous by design: it is only reachable before the app is configured, and
/// it self-guards by redirecting once <c>ApiBaseUrl</c> is already set.
/// </summary>
[ApiController]
[Route("api/setup")]
[AllowAnonymous]
public class SetupController : ControllerBase
{
    private readonly IOptionsMonitor<ActiveRolesConfig> _arConfig;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SetupController> _logger;
    private readonly ServiceAccountSecretProtector _secretProtector;
    private readonly SupersetLoaderHostedService _supersetLoader;

    public SetupController(
        IOptionsMonitor<ActiveRolesConfig> arConfig,
        IWebHostEnvironment env,
        IConfiguration configuration,
        ILogger<SetupController> logger,
        ServiceAccountSecretProtector secretProtector,
        SupersetLoaderHostedService supersetLoader)
    {
        _arConfig = arConfig;
        _env = env;
        _configuration = configuration;
        _logger = logger;
        _secretProtector = secretProtector;
        _supersetLoader = supersetLoader;
    }

    /// <summary>Full payload collected by the Angular setup wizard. Mirrors the former SetupModel bind properties 1:1.</summary>
    public sealed class SetupRequest
    {
        public string ApiBaseUrl { get; set; } = string.Empty;
        public string RstsUrl { get; set; } = string.Empty;
        public string RstsScope { get; set; } = string.Empty;
        public string ServiceAccountUsername { get; set; } = string.Empty;
        public string ServiceAccountPassword { get; set; } = string.Empty;
        public string WebInterfaceUrl { get; set; } = string.Empty;
        public string Language { get; set; } = SupportedLanguage.DefaultCode;
        // The Angular client sends the directory type as a string (e.g. "ActiveDirectory").
        // AddControllers() has no global string-enum converter, so bind it explicitly here;
        // otherwise model binding fails and [ApiController] returns a 400 before Save() runs.
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public RoleGroupDirectoryType RoleGroupsDirectoryType { get; set; } = RoleGroupDirectoryType.ActiveDirectory;
        public string ActiveRolesAdminsGroup { get; set; } = string.Empty;
        public string DashboardAdminsGroup { get; set; } = string.Empty;
        public string AuditorsGroup { get; set; } = string.Empty;
        public string PowerUsersGroup { get; set; } = string.Empty;
        public string CustomNoManagerUserFilter { get; set; } = string.Empty;
        public string CustomNoManagerServiceAccountFilter { get; set; } = string.Empty;
        public int LicensedDomainObjects { get; set; }
        public int LicensedPartitionObjects { get; set; }
        public int LicensedAzureObjects { get; set; }
        public int LicensedSaasObjects { get; set; }
        public int LicensedTotalObjects { get; set; }
        public bool AnalyticsEnabled { get; set; }
        public string AnalyticsMeasurementId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Returns the current defaults and option lists needed to render the wizard. If the app is
    /// already configured, signals the client to leave the wizard (redirect to login).
    /// </summary>
    [HttpGet]
    public IActionResult Get()
    {
        var config = _arConfig.CurrentValue;
        var pathBase = Request.PathBase.Value ?? string.Empty;

        // Already configured: the wizard should not be shown again.
        if (!string.IsNullOrWhiteSpace(config.ApiBaseUrl))
        {
            return Ok(new { configured = true, redirectUrl = $"{pathBase}/login" });
        }

        var selectedLanguage = SupportedLanguage.All.Any(l => l.Code == config.DefaultLanguage)
            ? config.DefaultLanguage
            : SupportedLanguage.DefaultCode;

        var roleGroups = config.RoleGroups;
        var analytics = config.Analytics;

        return Ok(new
        {
            configured = false,
            languages = SupportedLanguage.All.Select(l => new
            {
                code = l.Code,
                displayName = l.DisplayName,
                // Prefix with PathBase so flag assets resolve under IIS sub-apps.
                flagImage = $"{pathBase}/{l.FlagImage}"
            }),
            directoryTypes = Enum.GetNames<RoleGroupDirectoryType>(),
            defaults = new
            {
                language = selectedLanguage,
                roleGroupsDirectoryType = roleGroups.DirectoryType.ToString(),
                activeRolesAdminsGroup = roleGroups.ActiveRolesAdmins,
                dashboardAdminsGroup = roleGroups.DashboardAdmins,
                auditorsGroup = roleGroups.Auditors,
                powerUsersGroup = roleGroups.PowerUsers,
                analyticsEnabled = analytics.Enabled,
                analyticsMeasurementId = analytics.MeasurementId,
                // Placeholders the wizard shows under each optional field.
                roleGroupPlaceholders = new RoleGroupsConfig(),
                filterPlaceholders = new
                {
                    noManagerUser = config.DefaultFilters.NoManagerUser,
                    noManagerServiceAccount = config.DefaultFilters.NoManagerServiceAccount
                }
            }
        });
    }

    /// <summary>
    /// Validates the mandatory fields, protects the service-account password, writes the collected
    /// configuration into the active appsettings file, reloads configuration and triggers the
    /// initial superset build. Preserves the former SetupModel.OnPost behaviour exactly.
    /// </summary>
    [HttpPost]
    public IActionResult Save([FromBody] SetupRequest request)
    {
        _logger.LogWarning("Setup Save called. ApiBaseUrl='{ApiBaseUrl}', RstsUrl='{RstsUrl}'", request.ApiBaseUrl, request.RstsUrl);

        var apiUrl = request.ApiBaseUrl?.Trim() ?? "";
        var rstsUrl = request.RstsUrl?.Trim() ?? "";
        var saUsername = request.ServiceAccountUsername?.Trim() ?? "";
        var saPassword = request.ServiceAccountPassword ?? "";

        if (string.IsNullOrWhiteSpace(apiUrl))
            return BadRequest(new { error = "REST API URL is required." });
        if (string.IsNullOrWhiteSpace(rstsUrl))
            return BadRequest(new { error = "RSTS Token URL is required." });
        if (string.IsNullOrWhiteSpace(saUsername))
            return BadRequest(new { error = "Service account username is required." });
        if (string.IsNullOrWhiteSpace(saPassword))
            return BadRequest(new { error = "Service account password is required." });

        // Protect the password with Data Protection before it ever touches disk (never plaintext).
        string protectedPassword;
        try
        {
            protectedPassword = _secretProtector.Protect(saPassword);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to protect service-account password during setup.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to protect the service account password. Please try again." });
        }

        // Write to whichever appsettings file is actually in use: prefer the environment-specific
        // file (appsettings.<Environment>.json) if it already exists, otherwise appsettings.json.
        // Do NOT create a new environment file if it is absent.
        var appSettingsPath = ResolveAppSettingsPath();
        var json = System.IO.File.ReadAllText(appSettingsPath);
        var jsonNode = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        if (jsonNode is JsonObject root)
        {
            var activeRoles = root["ActiveRoles"]?.AsObject();
            if (activeRoles is null)
            {
                activeRoles = new JsonObject();
                root["ActiveRoles"] = activeRoles;
            }
            if (activeRoles != null)
            {
                activeRoles["ApiBaseUrl"] = apiUrl;
                activeRoles["RstsUrl"] = rstsUrl;
                activeRoles["RstsScope"] = request.RstsScope?.Trim() ?? "";
                activeRoles["WebInterfaceUrl"] = request.WebInterfaceUrl?.Trim() ?? "";

                // Custom LDAP filter overrides live under the nested CustomFilters section.
                var customFilters = activeRoles["CustomFilters"]?.AsObject();
                if (customFilters is null)
                {
                    customFilters = new JsonObject();
                    activeRoles["CustomFilters"] = customFilters;
                }
                customFilters["NoManagerUser"] = request.CustomNoManagerUserFilter?.Trim() ?? "";
                customFilters["NoManagerServiceAccount"] = request.CustomNoManagerServiceAccountFilter?.Trim() ?? "";

                // Licensed entitlement thresholds live under the nested Licensing section.
                var licensing = activeRoles["Licensing"]?.AsObject();
                if (licensing is null)
                {
                    licensing = new JsonObject();
                    activeRoles["Licensing"] = licensing;
                }
                licensing["DomainObjects"] = Math.Max(0, request.LicensedDomainObjects);
                licensing["PartitionObjects"] = Math.Max(0, request.LicensedPartitionObjects);
                licensing["AzureObjects"] = Math.Max(0, request.LicensedAzureObjects);
                licensing["SaasObjects"] = Math.Max(0, request.LicensedSaasObjects);
                licensing["TotalObjects"] = Math.Max(0, request.LicensedTotalObjects);
                activeRoles["DefaultLanguage"] = SupportedLanguage.All.Any(l => l.Code == request.Language)
                    ? request.Language
                    : SupportedLanguage.DefaultCode;

                // Write the collection service-account credentials into the nested ServiceAccount
                // object. The password is stored ENCRYPTED (Data Protection), never plaintext.
                var serviceAccount = activeRoles["ServiceAccount"]?.AsObject();
                if (serviceAccount is null)
                {
                    serviceAccount = new JsonObject();
                    activeRoles["ServiceAccount"] = serviceAccount;
                }
                serviceAccount["Username"] = saUsername;
                serviceAccount["ProtectedPassword"] = protectedPassword;

                // Persist the role/admin groups by NAME under a dedicated RoleGroups section, along
                // with a single directory-type flag (AD vs Entra) that drives filter/base-DN composition.
                // Empty inputs fall back to the coded defaults so a role is never left unmapped.
                var roleGroups = activeRoles["RoleGroups"]?.AsObject();
                if (roleGroups is null)
                {
                    roleGroups = new JsonObject();
                    activeRoles["RoleGroups"] = roleGroups;
                }
                var roleGroupDefaults = new RoleGroupsConfig();
                var activeRolesAdminsGroup = request.ActiveRolesAdminsGroup?.Trim();
                var dashboardAdminsGroup = request.DashboardAdminsGroup?.Trim();
                var auditorsGroup = request.AuditorsGroup?.Trim();
                var powerUsersGroup = request.PowerUsersGroup?.Trim();
                roleGroups["DirectoryType"] = request.RoleGroupsDirectoryType.ToString();
                roleGroups["ActiveRolesAdmins"] = string.IsNullOrWhiteSpace(activeRolesAdminsGroup) ? roleGroupDefaults.ActiveRolesAdmins : activeRolesAdminsGroup;
                roleGroups["DashboardAdmins"] = string.IsNullOrWhiteSpace(dashboardAdminsGroup) ? roleGroupDefaults.DashboardAdmins : dashboardAdminsGroup;
                roleGroups["Auditors"] = string.IsNullOrWhiteSpace(auditorsGroup) ? roleGroupDefaults.Auditors : auditorsGroup;
                roleGroups["PowerUsers"] = string.IsNullOrWhiteSpace(powerUsersGroup) ? roleGroupDefaults.PowerUsers : powerUsersGroup;

                // Optional Google Analytics usage tracking. Enabling the checkbox constitutes the
                // organizational opt-in, so ConsentGiven mirrors Enabled. No PII is ever sent.
                var analytics = activeRoles["Analytics"]?.AsObject();
                if (analytics is null)
                {
                    analytics = new JsonObject();
                    activeRoles["Analytics"] = analytics;
                }
                analytics["Enabled"] = request.AnalyticsEnabled;
                analytics["MeasurementId"] = request.AnalyticsMeasurementId?.Trim() ?? "";
                analytics["ConsentGiven"] = request.AnalyticsEnabled;
            }
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            System.IO.File.WriteAllText(appSettingsPath, jsonNode.ToJsonString(options));
        }

        // Force configuration reload so the middleware sees the updated ApiBaseUrl immediately.
        if (_configuration is IConfigurationRoot configRoot)
        {
            configRoot.Reload();
        }

        // Kick off the initial superset build now that the app is configured, so the cache starts
        // populating immediately rather than waiting for the next scheduled refresh or a restart.
        _supersetLoader.TriggerManualRefresh();

        var pathBase = Request.PathBase.Value ?? string.Empty;
        return Ok(new { redirectUrl = $"{pathBase}/login" });
    }

    /// <summary>
    /// Resolves the appsettings file that should receive the wizard's changes. Prefers the
    /// environment-specific file (appsettings.&lt;Environment&gt;.json) when it already exists so
    /// local/dev overrides are written where they are actually consumed; otherwise falls back to
    /// the base appsettings.json. Never creates a new environment file if it is absent.
    /// </summary>
    private string ResolveAppSettingsPath()
    {
        var envFile = Path.Combine(_env.ContentRootPath, $"appsettings.{_env.EnvironmentName}.json");
        if (System.IO.File.Exists(envFile))
            return envFile;

        return Path.Combine(_env.ContentRootPath, "appsettings.json");
    }
}
