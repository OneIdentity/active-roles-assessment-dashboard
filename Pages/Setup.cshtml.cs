using System.Text.Json;
using System.Text.Json.Nodes;
using ActiveRolesDashboard.Models;
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

    public SetupModel(IOptionsMonitor<ActiveRolesConfig> arConfig, IWebHostEnvironment env, IConfiguration configuration, ILogger<SetupModel> logger)
    {
        _arConfig = arConfig;
        _env = env;
        _configuration = configuration;
        _logger = logger;
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

        // Save all settings to appsettings.json
        var appSettingsPath = Path.Combine(_env.ContentRootPath, "appsettings.json");
        var json = System.IO.File.ReadAllText(appSettingsPath);
        var jsonNode = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        if (jsonNode is JsonObject root)
        {
            var activeRoles = root["ActiveRoles"]?.AsObject();
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
                activeRoles["DefaultLanguage"] = SupportedLanguage.All.Any(l => l.Code == Language)
                    ? Language
                    : SupportedLanguage.DefaultCode;
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

        return RedirectToPage("/Login");
    }
}
