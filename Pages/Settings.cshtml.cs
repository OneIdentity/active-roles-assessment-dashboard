using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Pages;

[Authorize]
public class SettingsModel : PageModel
{
    private readonly IOptionsMonitor<ActiveRolesConfig> _arConfig;
    private readonly UserSettingsService _userSettingsService;
    private readonly IWebHostEnvironment _env;
    private readonly IStringLocalizer<SettingsModel> _localizer;
    private readonly PerUserSummaryCache _summaryCache;
    private readonly ServiceAccountSecretProtector _secretProtector;

    public SettingsModel(IOptionsMonitor<ActiveRolesConfig> arConfig, UserSettingsService userSettingsService, IWebHostEnvironment env, IStringLocalizer<SettingsModel> localizer, PerUserSummaryCache summaryCache, ServiceAccountSecretProtector secretProtector)
    {
        _arConfig = arConfig;
        _userSettingsService = userSettingsService;
        _env = env;
        _localizer = localizer;
        _summaryCache = summaryCache;
        _secretProtector = secretProtector;
    }

    [BindProperty]
    public string WebInterfaceUrl { get; set; } = string.Empty;

    [BindProperty]
    public int AutoRefreshMinutes { get; set; }

    [BindProperty]
    public string Language { get; set; } = SupportedLanguage.DefaultCode;

    public IReadOnlyList<SupportedLanguage> Languages => SupportedLanguage.All;

    public SupportedLanguage SelectedLanguage =>
        SupportedLanguage.All.FirstOrDefault(l => l.Code == Language) ?? SupportedLanguage.All[0];

    public SupportedLanguage SelectedDefaultLanguage =>
        SupportedLanguage.All.FirstOrDefault(l => l.Code == DefaultLanguage) ?? SupportedLanguage.All[0];

    [BindProperty]
    public int EntraLargeGroupMemberThreshold { get; set; }

    [BindProperty]
    public KpiSettings KpiSettings { get; set; } = new();

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

    // REST API Configuration (restart required)
    [BindProperty]
    public string ApiBaseUrl { get; set; } = string.Empty;
    [BindProperty]
    public string RstsUrl { get; set; } = string.Empty;
    [BindProperty]
    public string Resource { get; set; } = string.Empty;
    [BindProperty]
    public bool IgnoreSslErrors { get; set; }

    // Default Filters
    [BindProperty]
    public string DefaultNoGroupOwnerFilter { get; set; } = string.Empty;
    [BindProperty]
    public string DefaultNoManagerUserFilter { get; set; } = string.Empty;
    [BindProperty]
    public string DefaultNoManagerServiceAccountFilter { get; set; } = string.Empty;
    [BindProperty]
    public string DefaultUserAccountExpiredFilter { get; set; } = string.Empty;
    [BindProperty]
    public string DefaultUserAccountLockedOutFilter { get; set; } = string.Empty;
    [BindProperty]
    public string DefaultEmptyGroupsFilter { get; set; } = string.Empty;
    [BindProperty]
    public string DefaultActiveRolesAdminsFilter { get; set; } = string.Empty;
    [BindProperty]
    public string DefaultADUserAccountsFilter { get; set; } = string.Empty;
    [BindProperty]
    public string DefaultADGroupsFilter { get; set; } = string.Empty;

    // App-wide default language (distinct from the per-user Language setting)
    [BindProperty]
    public string DefaultLanguage { get; set; } = string.Empty;

    // KPI Configuration - custom AD user account attributes (newline/comma separated in the UI)
    [BindProperty]
    public string CustomADUserAccountAttributes { get; set; } = string.Empty;

    // Service Account Credentials (restart required)
    [BindProperty]
    public string ServiceAccountUsername { get; set; } = string.Empty;
    [BindProperty]
    public string ServiceAccountPassword { get; set; } = string.Empty;

    // Data Refresh (restart required)
    [BindProperty]
    public string DailyRefreshTime { get; set; } = string.Empty;
    [BindProperty]
    public bool LoadOnStartup { get; set; }

    // Licensing Thresholds
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

    public bool SettingsChanged { get; set; }
    public bool IsActiveRolesAdmin { get; set; }
    public bool RestartRequired { get; set; }

    public string? Message { get; set; }

    public void OnGet()
    {
        IsActiveRolesAdmin = bool.TryParse(HttpContext.Session.GetString("IsActiveRolesAdmin"), out var val) && val;
        var config = _arConfig.CurrentValue;
        WebInterfaceUrl = config.WebInterfaceUrl;

        var username = User.Identity?.Name ?? "";
        var userSettings = _userSettingsService.Load(username);

        AutoRefreshMinutes = userSettings.AutoRefreshMinutes;
        KpiSettings = userSettings.KpiSettings;
        Language = SupportedLanguage.All.Any(l => l.Code == userSettings.Language)
            ? userSettings.Language
            : SupportedLanguage.DefaultCode;

        // Load KPI configuration from appsettings
        CustomNoGroupOwnerBaseDn = config.CustomNoGroupOwnerBaseDn;
        CustomNoManagerUserBaseDn = config.CustomNoManagerUserBaseDn;
        CustomNoManagerUserFilter = config.CustomNoManagerUserFilter;
        CustomNoManagerServiceAccountBaseDn = config.CustomNoManagerServiceAccountBaseDn;
        CustomNoManagerServiceAccountFilter = config.CustomNoManagerServiceAccountFilter;
        CustomUserAccountExpiredBaseDn = config.CustomUserAccountExpiredBaseDn;
        CustomUserAccountLockedOutBaseDn = config.CustomUserAccountLockedOutBaseDn;
        CustomEmptyGroupsBaseDn = config.CustomEmptyGroupsBaseDn;
        CustomActiveRolesAdminsBaseDn = config.CustomActiveRolesAdminsBaseDn;
        CustomActiveRolesAdminsFilter = config.CustomActiveRolesAdminsFilter;
        EntraLargeGroupMemberThreshold = config.Entra.LargeGroupMemberThreshold;

        // REST API Configuration
        ApiBaseUrl = config.ApiBaseUrl;
        RstsUrl = config.RstsUrl;
        Resource = config.Resource;
        IgnoreSslErrors = config.IgnoreSslErrors;

        // Default Filters
        DefaultNoGroupOwnerFilter = config.DefaultFilters.NoGroupOwner;
        DefaultNoManagerUserFilter = config.DefaultFilters.NoManagerUser;
        DefaultNoManagerServiceAccountFilter = config.DefaultFilters.NoManagerServiceAccount;
        DefaultUserAccountExpiredFilter = config.DefaultFilters.UserAccountExpired;
        DefaultUserAccountLockedOutFilter = config.DefaultFilters.UserAccountLockedOut;
        DefaultEmptyGroupsFilter = config.DefaultFilters.EmptyGroups;
        DefaultActiveRolesAdminsFilter = config.DefaultFilters.ActiveRolesAdmins;
        DefaultADUserAccountsFilter = config.DefaultFilters.ADUserAccounts;
        DefaultADGroupsFilter = config.DefaultFilters.ADGroups;

        // App-wide default language
        DefaultLanguage = config.DefaultLanguage;

        // KPI Configuration - custom AD user account attributes (one per line for editing)
        CustomADUserAccountAttributes = string.Join("\n", config.CustomADUserAccountAttributes);

        // Service Account Credentials (password is never rendered back to the client)
        ServiceAccountUsername = config.ServiceAccount.Username;
        ServiceAccountPassword = string.Empty;

        // Data Refresh schedule
        DailyRefreshTime = config.DataRefresh.DailyRefreshTime;
        LoadOnStartup = config.DataRefresh.LoadOnStartup;

        // Licensing Thresholds
        LicensedDomainObjects = config.Licensing.DomainObjects;
        LicensedPartitionObjects = config.Licensing.PartitionObjects;
        LicensedAzureObjects = config.Licensing.AzureObjects;
        LicensedSaasObjects = config.Licensing.SaasObjects;
        LicensedTotalObjects = config.Licensing.TotalObjects;
    }

    public IActionResult OnPost()
    {
        if (AutoRefreshMinutes < 0)
            AutoRefreshMinutes = 0;

        if (EntraLargeGroupMemberThreshold < 1)
            EntraLargeGroupMemberThreshold = 1;

        // Clamp licensing thresholds to non-negative values.
        LicensedDomainObjects = Math.Max(0, LicensedDomainObjects);
        LicensedPartitionObjects = Math.Max(0, LicensedPartitionObjects);
        LicensedAzureObjects = Math.Max(0, LicensedAzureObjects);
        LicensedSaasObjects = Math.Max(0, LicensedSaasObjects);
        LicensedTotalObjects = Math.Max(0, LicensedTotalObjects);

        // Normalize the daily refresh time (HH:mm, 24-hour); fall back to the current value if invalid.
        if (!TimeSpan.TryParseExact((DailyRefreshTime ?? "").Trim(),
                new[] { @"hh\:mm", @"h\:mm" }, CultureInfo.InvariantCulture, out var refreshTime))
        {
            DailyRefreshTime = _arConfig.CurrentValue.DataRefresh.DailyRefreshTime;
        }
        else
        {
            DailyRefreshTime = refreshTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        }

        // Detect changes to connection settings that only take effect after a restart.
        var config = _arConfig.CurrentValue;
        RestartRequired =
            !string.Equals((ApiBaseUrl ?? "").Trim(), config.ApiBaseUrl ?? "", StringComparison.Ordinal) ||
            !string.Equals((RstsUrl ?? "").Trim(), config.RstsUrl ?? "", StringComparison.Ordinal) ||
            !string.Equals((Resource ?? "").Trim(), config.Resource ?? "", StringComparison.Ordinal) ||
            IgnoreSslErrors != config.IgnoreSslErrors ||
            !string.Equals((ServiceAccountUsername ?? "").Trim(), config.ServiceAccount.Username ?? "", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(ServiceAccountPassword);

        // Save visibility settings to user file
        var username = User.Identity?.Name ?? "";
        var selectedLanguage = SupportedLanguage.All.Any(l => l.Code == Language)
            ? Language
            : SupportedLanguage.DefaultCode;
        var userSettings = new UserSettings
        {
            AutoRefreshMinutes = AutoRefreshMinutes,
            KpiSettings = KpiSettings,
            Language = selectedLanguage
        };
        _userSettingsService.Save(username, userSettings);

        // Persist KPI configuration and WebInterfaceUrl to appsettings.json
        SaveAppSettings();

        // Also store in session for the dashboard to pick up immediately
        HttpContext.Session.SetInt32("AutoRefreshMinutes", AutoRefreshMinutes);
        HttpContext.Session.SetString("KpiSettings", JsonSerializer.Serialize(KpiSettings));

        // Clear cached dashboard data since settings changed
        _summaryCache.Clear(username);

        SettingsChanged = true;
        Message = _localizer["SavedSuccessfully"];

        return Page();
    }

    private void SaveAppSettings()
    {
        var appSettingsPath = Path.Combine(_env.ContentRootPath, "appsettings.json");
        var json = System.IO.File.ReadAllText(appSettingsPath);
        var jsonNode = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        if (jsonNode is JsonObject root)
        {
            var activeRoles = root["ActiveRoles"]?.AsObject();
            if (activeRoles != null)
            {
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

                // Entra membership tuning (stored in its own Entra section).
                var entra = activeRoles["Entra"]?.AsObject();
                if (entra is null)
                {
                    entra = new JsonObject();
                    activeRoles["Entra"] = entra;
                }
                entra["LargeGroupMemberThreshold"] = EntraLargeGroupMemberThreshold;

                // REST API Configuration (takes effect after a restart)
                activeRoles["ApiBaseUrl"] = ApiBaseUrl?.Trim() ?? "";
                activeRoles["RstsUrl"] = RstsUrl?.Trim() ?? "";
                activeRoles["Resource"] = Resource?.Trim() ?? "";
                activeRoles["IgnoreSslErrors"] = IgnoreSslErrors;

                // Default Filters (stored in their own DefaultFilters section).
                var defaultFilters = activeRoles["DefaultFilters"]?.AsObject();
                if (defaultFilters is null)
                {
                    defaultFilters = new JsonObject();
                    activeRoles["DefaultFilters"] = defaultFilters;
                }
                defaultFilters["NoGroupOwner"] = DefaultNoGroupOwnerFilter?.Trim() ?? "";
                defaultFilters["NoManagerUser"] = DefaultNoManagerUserFilter?.Trim() ?? "";
                defaultFilters["NoManagerServiceAccount"] = DefaultNoManagerServiceAccountFilter?.Trim() ?? "";
                defaultFilters["UserAccountExpired"] = DefaultUserAccountExpiredFilter?.Trim() ?? "";
                defaultFilters["UserAccountLockedOut"] = DefaultUserAccountLockedOutFilter?.Trim() ?? "";
                defaultFilters["EmptyGroups"] = DefaultEmptyGroupsFilter?.Trim() ?? "";
                defaultFilters["ActiveRolesAdmins"] = DefaultActiveRolesAdminsFilter?.Trim() ?? "";
                defaultFilters["ADUserAccounts"] = DefaultADUserAccountsFilter?.Trim() ?? "";
                defaultFilters["ADGroups"] = DefaultADGroupsFilter?.Trim() ?? "";

                // App-wide default language
                activeRoles["DefaultLanguage"] = DefaultLanguage?.Trim() ?? "";

                // KPI Configuration - custom AD user account attributes (stored as a JSON array)
                var attrArray = new JsonArray();
                foreach (var attr in (CustomADUserAccountAttributes ?? "")
                        .Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    attrArray.Add(attr);
                }
                activeRoles["CustomADUserAccountAttributes"] = attrArray;

                // Licensing Thresholds (stored in their own Licensing section).
                var licensing = activeRoles["Licensing"]?.AsObject();
                if (licensing is null)
                {
                    licensing = new JsonObject();
                    activeRoles["Licensing"] = licensing;
                }
                licensing["DomainObjects"] = Math.Max(0, LicensedDomainObjects);
                licensing["PartitionObjects"] = Math.Max(0, LicensedPartitionObjects);
                licensing["AzureObjects"] = Math.Max(0, LicensedAzureObjects);
                licensing["SaasObjects"] = Math.Max(0, LicensedSaasObjects);
                licensing["TotalObjects"] = Math.Max(0, LicensedTotalObjects);

                // Data Refresh schedule (stored in its own DataRefresh section).
                var dataRefresh = activeRoles["DataRefresh"]?.AsObject();
                if (dataRefresh is null)
                {
                    dataRefresh = new JsonObject();
                    activeRoles["DataRefresh"] = dataRefresh;
                }
                dataRefresh["DailyRefreshTime"] = DailyRefreshTime?.Trim() ?? "";
                dataRefresh["LoadOnStartup"] = LoadOnStartup;

                // Service Account Credentials. The username is stored as-is; the password is
                // encrypted via Data Protection and only overwritten when a new value is supplied
                // (a blank password field leaves the existing protected password unchanged).
                var serviceAccount = activeRoles["ServiceAccount"]?.AsObject();
                if (serviceAccount is null)
                {
                    serviceAccount = new JsonObject();
                    activeRoles["ServiceAccount"] = serviceAccount;
                }
                serviceAccount["Username"] = ServiceAccountUsername?.Trim() ?? "";
                if (!string.IsNullOrEmpty(ServiceAccountPassword))
                {
                    serviceAccount["ProtectedPassword"] = _secretProtector.Protect(ServiceAccountPassword);
                }
            }
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            System.IO.File.WriteAllText(appSettingsPath, jsonNode.ToJsonString(options));
        }
    }
}
