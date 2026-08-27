using System.Text.Json;
using System.Text.Json.Nodes;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Pages;

[IgnoreAntiforgeryToken]
public class SetupModel : PageModel
{
    private readonly IOptionsMonitor<ActiveRolesConfig> _arConfig;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SetupModel> _logger;
    private readonly ServiceAccountSecretProtector _secretProtector;
    private readonly SupersetLoaderHostedService _supersetLoader;

    public SetupModel(IOptionsMonitor<ActiveRolesConfig> arConfig, IWebHostEnvironment env, IConfiguration configuration, ILogger<SetupModel> logger, ServiceAccountSecretProtector secretProtector, SupersetLoaderHostedService supersetLoader)
    {
        _arConfig = arConfig;
        _env = env;
        _configuration = configuration;
        _logger = logger;
        _secretProtector = secretProtector;
        _supersetLoader = supersetLoader;
    }

    [BindProperty]
    public string ApiBaseUrl { get; set; } = string.Empty;

    [BindProperty]
    public string Language { get; set; } = SupportedLanguage.DefaultCode;

    public IReadOnlyList<SupportedLanguage> Languages => SupportedLanguage.All;

    public SupportedLanguage SelectedLanguage =>
        SupportedLanguage.All.FirstOrDefault(l => l.Code == Language) ?? SupportedLanguage.All[0];

    [BindProperty]
    public string RstsUrl { get; set; } = string.Empty;

    [BindProperty]
    public string ServiceAccountUsername { get; set; } = string.Empty;

    [BindProperty]
    public string ServiceAccountPassword { get; set; } = string.Empty;

    [BindProperty]
    public string WebInterfaceUrl { get; set; } = string.Empty;

    [BindProperty]
    public string CustomNoGroupOwnerBaseDn { get; set; } = string.Empty;
    [BindProperty]
    public string CustomNoManagerUserBaseDn { get; set; } = string.Empty;
    [BindProperty]
    public string CustomNoManagerUserFilter { get; set; } = string.Empty;
    [BindProperty]
    public string CustomNoManagerServiceAccountBaseDn { get; set; } = string.Empty;
    [BindProperty]
    public string CustomNoManagerServiceAccountFilter { get; set; } = string.Empty;
    [BindProperty]
    public string CustomUserAccountExpiredBaseDn { get; set; } = string.Empty;
    [BindProperty]
    public string CustomUserAccountLockedOutBaseDn { get; set; } = string.Empty;
    [BindProperty]
    public string CustomEmptyGroupsBaseDn { get; set; } = string.Empty;
    [BindProperty]
    public string CustomActiveRolesAdminsBaseDn { get; set; } = string.Empty;
    [BindProperty]
    public string CustomActiveRolesAdminsFilter { get; set; } = string.Empty;

    [BindProperty]
    public int LicensedDomainObjects { get; set; }
    [BindProperty]
    public int LicensedPartitionObjects { get; set; }
    [BindProperty]
    public int LicensedAzureObjects { get; set; }
    [BindProperty]
    public int LicensedSaasObjects { get; set; }
    [BindProperty]
    public int LicensedTotalObjects { get; set; }

    public ActiveRolesConfig Defaults => _arConfig.CurrentValue;

    public string? ErrorMessage { get; set; }

    public IActionResult OnGet()
    {
        _logger.LogWarning("Setup OnGet called. ApiBaseUrl='{ApiBaseUrl}'", _arConfig.CurrentValue.ApiBaseUrl);
        // If already configured, redirect to login
        if (!string.IsNullOrWhiteSpace(_arConfig.CurrentValue.ApiBaseUrl))
            return RedirectToPage("/Login");

        Language = SupportedLanguage.All.Any(l => l.Code == _arConfig.CurrentValue.DefaultLanguage)
            ? _arConfig.CurrentValue.DefaultLanguage
            : SupportedLanguage.DefaultCode;

        return Page();
    }

    public IActionResult OnPost()
    {
        _logger.LogWarning("Setup OnPost called. Bound ApiBaseUrl='{ApiBaseUrl}', RstsUrl='{RstsUrl}'", ApiBaseUrl, RstsUrl);

        // Validate mandatory fields
        var apiUrl = ApiBaseUrl?.Trim() ?? "";
        var rstsUrl = RstsUrl?.Trim() ?? "";
        var saUsername = ServiceAccountUsername?.Trim() ?? "";
        var saPassword = ServiceAccountPassword ?? "";

        if (string.IsNullOrWhiteSpace(apiUrl))
        {
            ErrorMessage = "REST API URL is required.";
            return Page();
        }
        if (string.IsNullOrWhiteSpace(rstsUrl))
        {
            ErrorMessage = "RSTS Token URL is required.";
            return Page();
        }
        if (string.IsNullOrWhiteSpace(saUsername))
        {
            ErrorMessage = "Service account username is required.";
            return Page();
        }
        if (string.IsNullOrWhiteSpace(saPassword))
        {
            ErrorMessage = "Service account password is required.";
            return Page();
        }

        // Protect the password with Data Protection before it ever touches disk (never plaintext).
        string protectedPassword;
        try
        {
            protectedPassword = _secretProtector.Protect(saPassword);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to protect service-account password during setup.");
            ErrorMessage = "Failed to protect the service account password. Please try again.";
            return Page();
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
                activeRoles["WebInterfaceUrl"] = WebInterfaceUrl?.Trim() ?? "";
                activeRoles["CustomNoGroupOwnerBaseDn"] = CustomNoGroupOwnerBaseDn?.Trim() ?? "";
                activeRoles["CustomNoManagerUserBaseDn"] = CustomNoManagerUserBaseDn?.Trim() ?? "";
                activeRoles["CustomNoManagerUserFilter"] = CustomNoManagerUserFilter?.Trim() ?? "";
                activeRoles["CustomNoManagerServiceAccountBaseDn"] = CustomNoManagerServiceAccountBaseDn?.Trim() ?? "";
                activeRoles["CustomNoManagerServiceAccountFilter"] = CustomNoManagerServiceAccountFilter?.Trim() ?? "";
                activeRoles["CustomUserAccountExpiredBaseDn"] = CustomUserAccountExpiredBaseDn?.Trim() ?? "";
                activeRoles["CustomUserAccountLockedOutBaseDn"] = CustomUserAccountLockedOutBaseDn?.Trim() ?? "";
                activeRoles["CustomEmptyGroupsBaseDn"] = CustomEmptyGroupsBaseDn?.Trim() ?? "";
                activeRoles["CustomActiveRolesAdminsBaseDn"] = CustomActiveRolesAdminsBaseDn?.Trim() ?? "";
                activeRoles["CustomActiveRolesAdminsFilter"] = CustomActiveRolesAdminsFilter?.Trim() ?? "";
                activeRoles["LicensedDomainObjects"] = Math.Max(0, LicensedDomainObjects);
                activeRoles["LicensedPartitionObjects"] = Math.Max(0, LicensedPartitionObjects);
                activeRoles["LicensedAzureObjects"] = Math.Max(0, LicensedAzureObjects);
                activeRoles["LicensedSaasObjects"] = Math.Max(0, LicensedSaasObjects);
                activeRoles["LicensedTotalObjects"] = Math.Max(0, LicensedTotalObjects);
                activeRoles["DefaultLanguage"] = SupportedLanguage.All.Any(l => l.Code == Language)
                    ? Language
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
            }
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            System.IO.File.WriteAllText(appSettingsPath, jsonNode.ToJsonString(options));
        }

        // Force configuration reload so the middleware sees the updated ApiBaseUrl immediately
        if (_configuration is IConfigurationRoot configRoot)
        {
            configRoot.Reload();
        }

        // Kick off the initial superset build now that the app is configured, so the cache starts
        // populating immediately rather than waiting for the next scheduled refresh or a restart.
        _supersetLoader.TriggerManualRefresh();

        return RedirectToPage("/Login");
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
