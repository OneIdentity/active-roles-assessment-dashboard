using System.Globalization;
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
/// REST API that backs the Angular Settings page. Replaces the former Razor
/// <c>Pages/Settings.cshtml(.cs)</c>, preserving its behaviour exactly:
/// <see cref="Get"/> surfaces the current per-user and application settings, the
/// supported languages/directory types, the KPI-visibility tree, the role/permission
/// matrix and the caller's effective capabilities; <see cref="Save"/> reproduces
/// <c>SettingsModel.OnPost</c> (permission re-checks, clamping/normalization,
/// restart-required detection, per-user persistence with session/cache side-effects,
/// and appsettings persistence including the admin-only role matrix).
/// </summary>
[ApiController]
[Route("api/settings")]
[Authorize]
public class SettingsController : ControllerBase
{
    private readonly IOptionsMonitor<ActiveRolesConfig> _arConfig;
    private readonly UserSettingsService _userSettingsService;
    private readonly IWebHostEnvironment _env;
    private readonly PerUserSummaryCache _summaryCache;
    private readonly ServiceAccountSecretProtector _secretProtector;
    private readonly RoleService _roleService;
    private readonly DirectoryFactsResolver _directoryFacts;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(
        IOptionsMonitor<ActiveRolesConfig> arConfig,
        UserSettingsService userSettingsService,
        IWebHostEnvironment env,
        PerUserSummaryCache summaryCache,
        ServiceAccountSecretProtector secretProtector,
        RoleService roleService,
        DirectoryFactsResolver directoryFacts,
        ILogger<SettingsController> logger)
    {
        _arConfig = arConfig;
        _userSettingsService = userSettingsService;
        _env = env;
        _summaryCache = summaryCache;
        _secretProtector = secretProtector;
        _roleService = roleService;
        _directoryFacts = directoryFacts;
        _logger = logger;
    }

    // ---------------------------------------------------------------------
    // Contracts
    // ---------------------------------------------------------------------

    /// <summary>The caller's effective settings capabilities, used by the client to show/hide/disable sections.</summary>
    public sealed class SettingsCapabilities
    {
        public bool CanAccessSettings { get; set; }
        public bool CanManageUserSettings { get; set; }
        public bool CanViewSystemSettings { get; set; }
        public bool CanManageSystemSettings { get; set; }
        public bool IsActiveRolesAdmin { get; set; }
    }

    public sealed class LanguageOption
    {
        public string Code { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string FlagImage { get; set; } = string.Empty;
    }

    public sealed class KpiVisibilityItem
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public bool Enabled { get; set; }
    }

    public sealed class KpiVisibilityCategory
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public bool AdminOnly { get; set; }
        public List<KpiVisibilityItem> Items { get; set; } = new();
    }

    public sealed class RoleMatrixPermission
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }

    public sealed class RoleMatrixRole
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public bool Fixed { get; set; }
    }

    /// <summary>The full GET response consumed by the Angular settings page.</summary>
    public sealed class SettingsOptions
    {
        public SettingsCapabilities Capabilities { get; set; } = new();
        public List<LanguageOption> Languages { get; set; } = new();
        public List<string> DirectoryTypes { get; set; } = new();

        // User settings
        public int AutoRefreshMinutes { get; set; }
        public string Language { get; set; } = SupportedLanguage.DefaultCode;
        public List<KpiVisibilityCategory> KpiVisibility { get; set; } = new();

        // System settings
        public string WebInterfaceUrl { get; set; } = string.Empty;
        public string CustomNoManagerUserFilter { get; set; } = string.Empty;
        public string CustomNoManagerServiceAccountFilter { get; set; } = string.Empty;
        public int EntraLargeGroupMemberThreshold { get; set; }
        public int DynamicGroupExpensiveRuleThreshold { get; set; }
        public string CustomADUserAccountAttributes { get; set; } = string.Empty;

        public string ApiBaseUrl { get; set; } = string.Empty;
        public string RstsUrl { get; set; } = string.Empty;
        public string Resource { get; set; } = string.Empty;
        public bool IgnoreSslErrors { get; set; }

        public bool AnalyticsEnabled { get; set; }
        public string AnalyticsMeasurementId { get; set; } = string.Empty;

        public string DefaultNoGroupOwnerFilter { get; set; } = string.Empty;
        public string DefaultNoManagerUserFilter { get; set; } = string.Empty;
        public string DefaultNoManagerServiceAccountFilter { get; set; } = string.Empty;
        public string DefaultUserAccountExpiredFilter { get; set; } = string.Empty;
        public string DefaultUserAccountLockedOutFilter { get; set; } = string.Empty;
        public string DefaultEmptyGroupsFilter { get; set; } = string.Empty;
        public string DefaultADUserAccountsFilter { get; set; } = string.Empty;
        public string DefaultADGroupsFilter { get; set; } = string.Empty;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public RoleGroupDirectoryType RoleGroupsDirectoryType { get; set; } = RoleGroupDirectoryType.ActiveDirectory;
        public string ActiveRolesAdminsGroup { get; set; } = string.Empty;
        public string DashboardAdminsGroup { get; set; } = string.Empty;
        public string AuditorsGroup { get; set; } = string.Empty;
        public string PowerUsersGroup { get; set; } = string.Empty;

        public string DefaultLanguage { get; set; } = string.Empty;

        public string ServiceAccountUsername { get; set; } = string.Empty;

        public string DailyRefreshTime { get; set; } = string.Empty;
        public bool LoadOnStartup { get; set; }

        public bool PerfTrendingEnabled { get; set; }
        public int PerfTrendingIntervalMinutes { get; set; }
        public int PerfTrendingRetentionHours { get; set; }
        public int PerfTrendingMinimumInterval { get; set; }

        public int LicensedDomainObjects { get; set; }
        public int LicensedPartitionObjects { get; set; }
        public int LicensedAzureObjects { get; set; }
        public int LicensedSaasObjects { get; set; }
        public int LicensedTotalObjects { get; set; }

        // Role/permission matrix (admin-only edit)
        public List<RoleMatrixRole> MatrixRoles { get; set; } = new();
        public List<RoleMatrixPermission> MatrixPermissions { get; set; } = new();
        /// <summary>Currently-granted "Role:Permission" tokens.</summary>
        public List<string> MatrixGrants { get; set; } = new();

        /// <summary>Placeholder/default values used as input hints (mirrors Razor @Model.Defaults).</summary>
        public SettingsDefaults Defaults { get; set; } = new();
    }

    public sealed class SettingsDefaults
    {
        public string NoManagerUser { get; set; } = string.Empty;
        public string NoManagerServiceAccount { get; set; } = string.Empty;
        public string RoleGroupActiveRolesAdmins { get; set; } = string.Empty;
        public string RoleGroupDashboardAdmins { get; set; } = string.Empty;
        public string RoleGroupAuditors { get; set; } = string.Empty;
        public string RoleGroupPowerUsers { get; set; } = string.Empty;
    }

    /// <summary>Full payload posted by the Angular settings page. Mirrors the former SettingsModel bind properties.</summary>
    public sealed class SettingsRequest
    {
        // User settings
        public int AutoRefreshMinutes { get; set; }
        public string Language { get; set; } = SupportedLanguage.DefaultCode;
        /// <summary>The whole KpiSettings bag is round-tripped so flags not surfaced in the UI are preserved.</summary>
        public KpiSettings KpiSettings { get; set; } = new();

        // System settings
        public string WebInterfaceUrl { get; set; } = string.Empty;
        public string CustomNoManagerUserFilter { get; set; } = string.Empty;
        public string CustomNoManagerServiceAccountFilter { get; set; } = string.Empty;
        public int EntraLargeGroupMemberThreshold { get; set; }
        public int DynamicGroupExpensiveRuleThreshold { get; set; }
        public string CustomADUserAccountAttributes { get; set; } = string.Empty;

        public string ApiBaseUrl { get; set; } = string.Empty;
        public string RstsUrl { get; set; } = string.Empty;
        public string Resource { get; set; } = string.Empty;
        public bool IgnoreSslErrors { get; set; }

        public bool AnalyticsEnabled { get; set; }
        public string AnalyticsMeasurementId { get; set; } = string.Empty;

        public string DefaultNoGroupOwnerFilter { get; set; } = string.Empty;
        public string DefaultNoManagerUserFilter { get; set; } = string.Empty;
        public string DefaultNoManagerServiceAccountFilter { get; set; } = string.Empty;
        public string DefaultUserAccountExpiredFilter { get; set; } = string.Empty;
        public string DefaultUserAccountLockedOutFilter { get; set; } = string.Empty;
        public string DefaultEmptyGroupsFilter { get; set; } = string.Empty;
        public string DefaultADUserAccountsFilter { get; set; } = string.Empty;
        public string DefaultADGroupsFilter { get; set; } = string.Empty;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public RoleGroupDirectoryType RoleGroupsDirectoryType { get; set; } = RoleGroupDirectoryType.ActiveDirectory;
        public string ActiveRolesAdminsGroup { get; set; } = string.Empty;
        public string DashboardAdminsGroup { get; set; } = string.Empty;
        public string AuditorsGroup { get; set; } = string.Empty;
        public string PowerUsersGroup { get; set; } = string.Empty;

        public string DefaultLanguage { get; set; } = string.Empty;

        public string ServiceAccountUsername { get; set; } = string.Empty;
        /// <summary>Blank leaves the existing protected password unchanged (write-only).</summary>
        public string ServiceAccountPassword { get; set; } = string.Empty;

        public string DailyRefreshTime { get; set; } = string.Empty;
        public bool LoadOnStartup { get; set; }

        public bool PerfTrendingEnabled { get; set; }
        public int PerfTrendingIntervalMinutes { get; set; }
        public int PerfTrendingRetentionHours { get; set; }

        public int LicensedDomainObjects { get; set; }
        public int LicensedPartitionObjects { get; set; }
        public int LicensedAzureObjects { get; set; }
        public int LicensedSaasObjects { get; set; }
        public int LicensedTotalObjects { get; set; }

        /// <summary>Granted "Role:Permission" tokens (one per selected checkbox). DashboardAdministrator is ignored.</summary>
        public List<string> RolePermissionGrants { get; set; } = new();
    }

    public sealed class SettingsSaveResult
    {
        public bool SettingsChanged { get; set; }
        public bool RestartRequired { get; set; }
        public string? Error { get; set; }
    }

    // ---------------------------------------------------------------------
    // GET
    // ---------------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var permissions = await ResolvePermissionsAsync();

        if (!RolePermissionRegistry.CanAccessSettings(permissions.Permissions))
            return Forbid();

        var config = _arConfig.CurrentValue;
        var username = User.Identity?.Name ?? "";
        var userSettings = _userSettingsService.Load(username);

        var language = SupportedLanguage.All.Any(l => l.Code == userSettings.Language)
            ? userSettings.Language
            : SupportedLanguage.DefaultCode;

        var matrix = _roleService.GetMatrix();

        var options = new SettingsOptions
        {
            Capabilities = new SettingsCapabilities
            {
                CanAccessSettings = RolePermissionRegistry.CanAccessSettings(permissions.Permissions),
                CanManageUserSettings = RolePermissionRegistry.CanManageUserSettings(permissions.Permissions),
                CanViewSystemSettings = RolePermissionRegistry.CanViewSystemSettings(permissions.Permissions),
                CanManageSystemSettings = RolePermissionRegistry.CanManageSystemSettings(permissions.Permissions),
                IsActiveRolesAdmin = permissions.IsActiveRolesAdmin
            },
            Languages = SupportedLanguage.All
                .Select(l => new LanguageOption { Code = l.Code, DisplayName = l.DisplayName, FlagImage = l.FlagImage })
                .ToList(),
            DirectoryTypes = Enum.GetNames<RoleGroupDirectoryType>().ToList(),

            AutoRefreshMinutes = userSettings.AutoRefreshMinutes,
            Language = language,
            KpiVisibility = BuildKpiVisibility(userSettings.KpiSettings, permissions.IsActiveRolesAdmin),

            WebInterfaceUrl = config.WebInterfaceUrl,
            CustomNoManagerUserFilter = config.CustomNoManagerUserFilter,
            CustomNoManagerServiceAccountFilter = config.CustomNoManagerServiceAccountFilter,
            EntraLargeGroupMemberThreshold = config.Entra.LargeGroupMemberThreshold,
            DynamicGroupExpensiveRuleThreshold = config.DynamicGroupExpensiveRuleThreshold,
            CustomADUserAccountAttributes = string.Join("\n", config.CustomADUserAccountAttributes),

            ApiBaseUrl = config.ApiBaseUrl,
            RstsUrl = config.RstsUrl,
            Resource = config.Resource,
            IgnoreSslErrors = config.IgnoreSslErrors,

            AnalyticsEnabled = config.Analytics.Enabled,
            AnalyticsMeasurementId = config.Analytics.MeasurementId,

            DefaultNoGroupOwnerFilter = config.DefaultFilters.NoGroupOwner,
            DefaultNoManagerUserFilter = config.DefaultFilters.NoManagerUser,
            DefaultNoManagerServiceAccountFilter = config.DefaultFilters.NoManagerServiceAccount,
            DefaultUserAccountExpiredFilter = config.DefaultFilters.UserAccountExpired,
            DefaultUserAccountLockedOutFilter = config.DefaultFilters.UserAccountLockedOut,
            DefaultEmptyGroupsFilter = config.DefaultFilters.EmptyGroups,
            DefaultADUserAccountsFilter = config.DefaultFilters.ADUserAccounts,
            DefaultADGroupsFilter = config.DefaultFilters.ADGroups,

            RoleGroupsDirectoryType = config.RoleGroups.DirectoryType,
            ActiveRolesAdminsGroup = config.RoleGroups.ActiveRolesAdmins,
            DashboardAdminsGroup = config.RoleGroups.DashboardAdmins,
            AuditorsGroup = config.RoleGroups.Auditors,
            PowerUsersGroup = config.RoleGroups.PowerUsers,

            DefaultLanguage = config.DefaultLanguage,

            ServiceAccountUsername = config.ServiceAccount.Username,

            DailyRefreshTime = config.DataRefresh.DailyRefreshTime,
            LoadOnStartup = config.DataRefresh.LoadOnStartup,

            PerfTrendingEnabled = config.PerformanceTrending.Enabled,
            PerfTrendingIntervalMinutes = config.PerformanceTrending.SnapshotIntervalMinutes,
            PerfTrendingRetentionHours = config.PerformanceTrending.RetentionHours,
            PerfTrendingMinimumInterval = PerformanceTrendingConfig.MinimumIntervalMinutes,

            LicensedDomainObjects = config.Licensing.DomainObjects,
            LicensedPartitionObjects = config.Licensing.PartitionObjects,
            LicensedAzureObjects = config.Licensing.AzureObjects,
            LicensedSaasObjects = config.Licensing.SaasObjects,
            LicensedTotalObjects = config.Licensing.TotalObjects,

            MatrixRoles = RolePermissionRegistry.AllRoles
                .Select(r => new RoleMatrixRole
                {
                    Key = r.ToString(),
                    Label = RolePermissionRegistry.RoleDisplay[r].DefaultName,
                    Fixed = r == DashboardRole.DashboardAdministrator
                })
                .ToList(),
            MatrixPermissions = RolePermissionRegistry.AllPermissions
                .Select(p => new RoleMatrixPermission
                {
                    Key = p.ToString(),
                    Label = RolePermissionRegistry.PermissionDisplay[p].DefaultName
                })
                .ToList(),
            MatrixGrants = matrix
                .SelectMany(kvp => kvp.Value.Select(perm => $"{kvp.Key}:{perm}"))
                .ToList(),

            Defaults = new SettingsDefaults
            {
                NoManagerUser = config.DefaultFilters.NoManagerUser,
                NoManagerServiceAccount = config.DefaultFilters.NoManagerServiceAccount,
                RoleGroupActiveRolesAdmins = config.RoleGroups.ActiveRolesAdmins,
                RoleGroupDashboardAdmins = config.RoleGroups.DashboardAdmins,
                RoleGroupAuditors = config.RoleGroups.Auditors,
                RoleGroupPowerUsers = config.RoleGroups.PowerUsers
            }
        };

        return Ok(options);
    }

    // ---------------------------------------------------------------------
    // POST
    // ---------------------------------------------------------------------

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SettingsRequest request)
    {
        var permissions = await ResolvePermissionsAsync();

        // Server-side authorization: a user with no settings permission cannot change anything.
        if (!RolePermissionRegistry.CanAccessSettings(permissions.Permissions))
            return Forbid();

        var canManageUser = RolePermissionRegistry.CanManageUserSettings(permissions.Permissions);
        var canManageSystem = RolePermissionRegistry.CanManageSystemSettings(permissions.Permissions);

        if (request.AutoRefreshMinutes < 0)
            request.AutoRefreshMinutes = 0;

        if (request.EntraLargeGroupMemberThreshold < 1)
            request.EntraLargeGroupMemberThreshold = 1;

        if (request.DynamicGroupExpensiveRuleThreshold < 1)
            request.DynamicGroupExpensiveRuleThreshold = 1;

        request.LicensedDomainObjects = Math.Max(0, request.LicensedDomainObjects);
        request.LicensedPartitionObjects = Math.Max(0, request.LicensedPartitionObjects);
        request.LicensedAzureObjects = Math.Max(0, request.LicensedAzureObjects);
        request.LicensedSaasObjects = Math.Max(0, request.LicensedSaasObjects);
        request.LicensedTotalObjects = Math.Max(0, request.LicensedTotalObjects);

        request.PerfTrendingIntervalMinutes = Math.Max(PerformanceTrendingConfig.MinimumIntervalMinutes, request.PerfTrendingIntervalMinutes);
        request.PerfTrendingRetentionHours = Math.Max(1, request.PerfTrendingRetentionHours);

        // Normalize the daily refresh time (HH:mm, 24-hour); fall back to the current value if invalid.
        if (!TimeSpan.TryParseExact((request.DailyRefreshTime ?? "").Trim(),
                new[] { @"hh\:mm", @"h\:mm" }, CultureInfo.InvariantCulture, out var refreshTime))
        {
            request.DailyRefreshTime = _arConfig.CurrentValue.DataRefresh.DailyRefreshTime;
        }
        else
        {
            request.DailyRefreshTime = refreshTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        }

        // Detect changes to connection settings that only take effect after a restart.
        var config = _arConfig.CurrentValue;
        var restartRequired = canManageSystem && (
            !string.Equals((request.ApiBaseUrl ?? "").Trim(), config.ApiBaseUrl ?? "", StringComparison.Ordinal) ||
            !string.Equals((request.RstsUrl ?? "").Trim(), config.RstsUrl ?? "", StringComparison.Ordinal) ||
            !string.Equals((request.Resource ?? "").Trim(), config.Resource ?? "", StringComparison.Ordinal) ||
            request.IgnoreSslErrors != config.IgnoreSslErrors ||
            !string.Equals((request.ServiceAccountUsername ?? "").Trim(), config.ServiceAccount.Username ?? "", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(request.ServiceAccountPassword));

        var username = User.Identity?.Name ?? "";

        try
        {
            // User settings (Language, KPI visibility, auto-refresh) require ManageUserSettings.
            if (canManageUser)
            {
                var selectedLanguage = SupportedLanguage.All.Any(l => l.Code == request.Language)
                    ? request.Language
                    : SupportedLanguage.DefaultCode;
                var userSettings = new UserSettings
                {
                    AutoRefreshMinutes = request.AutoRefreshMinutes,
                    KpiSettings = request.KpiSettings ?? new KpiSettings(),
                    Language = selectedLanguage
                };
                _userSettingsService.Save(username, userSettings);

                // Also store in session for the dashboard to pick up immediately.
                HttpContext.Session.SetInt32("AutoRefreshMinutes", request.AutoRefreshMinutes);
                HttpContext.Session.SetString("KpiSettings", JsonSerializer.Serialize(userSettings.KpiSettings));

                // Clear cached dashboard data since settings changed.
                _summaryCache.Clear(username);
            }

            // System settings require ManageSystemSettings. The role matrix is stricter: only
            // Dashboard/Active Roles admins may persist it.
            if (canManageSystem)
            {
                SaveAppSettings(request, persistRoleMatrix: permissions.IsActiveRolesAdmin);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings for '{Username}'.", username);
            return StatusCode(500, new SettingsSaveResult { Error = "Settings could not be saved. Please review your entries and try again." });
        }

        return Ok(new SettingsSaveResult { SettingsChanged = true, RestartRequired = restartRequired });
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private sealed record ResolvedPermissions(IReadOnlySet<DashboardPermission> Permissions, bool IsActiveRolesAdmin);

    /// <summary>
    /// Resolves the caller's Active Roles admin flag and effective permission set, mirroring
    /// <c>SettingsModel.ResolvePermissionsAsync</c> (epoch-aware session cache + service-token fallback).
    /// </summary>
    private async Task<ResolvedPermissions> ResolvePermissionsAsync()
    {
        var username = User.Identity?.Name ?? "";
        var token = HttpContext.Session.GetString("AccessToken");

        var currentEpoch = _directoryFacts.CurrentEpoch;
        var sessionEpoch = HttpContext.Session.GetString("DirectoryFactsEpoch");
        var sessionAdmin = HttpContext.Session.GetString("IsActiveRolesAdmin");
        var sessionRole = HttpContext.Session.GetString("DashboardRole");

        bool isActiveRolesAdmin;
        DashboardRole role;
        if (sessionEpoch == currentEpoch.ToString()
            && sessionAdmin != null
            && sessionRole != null
            && Enum.TryParse(sessionRole, out DashboardRole parsedRole))
        {
            isActiveRolesAdmin = bool.TryParse(sessionAdmin, out var val) && val;
            role = parsedRole;
        }
        else if (!string.IsNullOrEmpty(token))
        {
            var facts = await _directoryFacts.ResolveAsync(token, username);
            isActiveRolesAdmin = facts.IsActiveRolesAdmin;
            role = facts.Role;

            HttpContext.Session.SetString("IsActiveRolesAdmin", facts.IsActiveRolesAdmin.ToString());
            HttpContext.Session.SetString("DashboardRole", facts.Role.ToString());
            HttpContext.Session.SetString("DirectoryFactsEpoch", facts.Epoch.ToString());
        }
        else
        {
            isActiveRolesAdmin = false;
            role = DashboardRole.User;
        }

        return new ResolvedPermissions(_roleService.GetPermissions(role), isActiveRolesAdmin);
    }

    private void SaveAppSettings(SettingsRequest request, bool persistRoleMatrix)
    {
        var appSettingsPath = Path.Combine(_env.ContentRootPath, "appsettings.json");
        var json = System.IO.File.ReadAllText(appSettingsPath);
        var jsonNode = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        if (jsonNode is JsonObject root)
        {
            var activeRoles = root["ActiveRoles"]?.AsObject();
            if (activeRoles != null)
            {
                activeRoles["WebInterfaceUrl"] = request.WebInterfaceUrl?.Trim() ?? "";

                var customFilters = activeRoles["CustomFilters"]?.AsObject();
                if (customFilters is null)
                {
                    customFilters = new JsonObject();
                    activeRoles["CustomFilters"] = customFilters;
                }
                customFilters["NoManagerUser"] = request.CustomNoManagerUserFilter?.Trim() ?? "";
                customFilters["NoManagerServiceAccount"] = request.CustomNoManagerServiceAccountFilter?.Trim() ?? "";

                var entra = activeRoles["Entra"]?.AsObject();
                if (entra is null)
                {
                    entra = new JsonObject();
                    activeRoles["Entra"] = entra;
                }
                entra["LargeGroupMemberThreshold"] = request.EntraLargeGroupMemberThreshold;

                activeRoles["DynamicGroupExpensiveRuleThreshold"] = request.DynamicGroupExpensiveRuleThreshold;

                activeRoles["ApiBaseUrl"] = request.ApiBaseUrl?.Trim() ?? "";
                activeRoles["RstsUrl"] = request.RstsUrl?.Trim() ?? "";
                activeRoles["Resource"] = request.Resource?.Trim() ?? "";
                activeRoles["IgnoreSslErrors"] = request.IgnoreSslErrors;

                var analytics = activeRoles["Analytics"]?.AsObject();
                if (analytics is null)
                {
                    analytics = new JsonObject();
                    activeRoles["Analytics"] = analytics;
                }
                analytics["Enabled"] = request.AnalyticsEnabled;
                analytics["MeasurementId"] = request.AnalyticsMeasurementId?.Trim() ?? "";
                analytics["ConsentGiven"] = request.AnalyticsEnabled;

                var defaultFilters = activeRoles["DefaultFilters"]?.AsObject();
                if (defaultFilters is null)
                {
                    defaultFilters = new JsonObject();
                    activeRoles["DefaultFilters"] = defaultFilters;
                }
                defaultFilters["NoGroupOwner"] = request.DefaultNoGroupOwnerFilter?.Trim() ?? "";
                defaultFilters["NoManagerUser"] = request.DefaultNoManagerUserFilter?.Trim() ?? "";
                defaultFilters["NoManagerServiceAccount"] = request.DefaultNoManagerServiceAccountFilter?.Trim() ?? "";
                defaultFilters["UserAccountExpired"] = request.DefaultUserAccountExpiredFilter?.Trim() ?? "";
                defaultFilters["UserAccountLockedOut"] = request.DefaultUserAccountLockedOutFilter?.Trim() ?? "";
                defaultFilters["EmptyGroups"] = request.DefaultEmptyGroupsFilter?.Trim() ?? "";
                defaultFilters["ADUserAccounts"] = request.DefaultADUserAccountsFilter?.Trim() ?? "";
                defaultFilters["ADGroups"] = request.DefaultADGroupsFilter?.Trim() ?? "";

                var roleGroups = activeRoles["RoleGroups"]?.AsObject();
                if (roleGroups is null)
                {
                    roleGroups = new JsonObject();
                    activeRoles["RoleGroups"] = roleGroups;
                }
                roleGroups["DirectoryType"] = request.RoleGroupsDirectoryType.ToString();
                roleGroups["ActiveRolesAdmins"] = request.ActiveRolesAdminsGroup?.Trim() ?? "";
                roleGroups["DashboardAdmins"] = request.DashboardAdminsGroup?.Trim() ?? "";
                roleGroups["Auditors"] = request.AuditorsGroup?.Trim() ?? "";
                roleGroups["PowerUsers"] = request.PowerUsersGroup?.Trim() ?? "";

                if (persistRoleMatrix)
                {
                    var newMatrix = new Dictionary<DashboardRole, IReadOnlySet<DashboardPermission>>();
                    foreach (var role in RolePermissionRegistry.AllRoles)
                    {
                        newMatrix[role] = new HashSet<DashboardPermission>();
                    }
                    foreach (var grant in request.RolePermissionGrants ?? new List<string>())
                    {
                        if (string.IsNullOrWhiteSpace(grant)) continue;
                        var parts = grant.Split(':', 2);
                        if (parts.Length != 2) continue;
                        if (Enum.TryParse<DashboardRole>(parts[0], out var role) &&
                            Enum.TryParse<DashboardPermission>(parts[1], out var permission) &&
                            newMatrix.TryGetValue(role, out var set) && set is HashSet<DashboardPermission> hs)
                        {
                            hs.Add(permission);
                        }
                    }
                    var rolesSection = activeRoles["Roles"]?.AsObject();
                    if (rolesSection is null)
                    {
                        rolesSection = new JsonObject();
                        activeRoles["Roles"] = rolesSection;
                    }
                    rolesSection["ProtectedMatrix"] = _roleService.ProtectMatrix(newMatrix);
                }

                activeRoles["DefaultLanguage"] = request.DefaultLanguage?.Trim() ?? "";

                var attrArray = new JsonArray();
                foreach (var attr in (request.CustomADUserAccountAttributes ?? "")
                        .Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    attrArray.Add(attr);
                }
                activeRoles["CustomADUserAccountAttributes"] = attrArray;

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

                var dataRefresh = activeRoles["DataRefresh"]?.AsObject();
                if (dataRefresh is null)
                {
                    dataRefresh = new JsonObject();
                    activeRoles["DataRefresh"] = dataRefresh;
                }
                dataRefresh["DailyRefreshTime"] = request.DailyRefreshTime?.Trim() ?? "";
                dataRefresh["LoadOnStartup"] = request.LoadOnStartup;

                var perfTrending = activeRoles["PerformanceTrending"]?.AsObject();
                if (perfTrending is null)
                {
                    perfTrending = new JsonObject();
                    activeRoles["PerformanceTrending"] = perfTrending;
                }
                perfTrending["Enabled"] = request.PerfTrendingEnabled;
                perfTrending["SnapshotIntervalMinutes"] = Math.Max(PerformanceTrendingConfig.MinimumIntervalMinutes, request.PerfTrendingIntervalMinutes);
                perfTrending["RetentionHours"] = Math.Max(1, request.PerfTrendingRetentionHours);

                var serviceAccount = activeRoles["ServiceAccount"]?.AsObject();
                if (serviceAccount is null)
                {
                    serviceAccount = new JsonObject();
                    activeRoles["ServiceAccount"] = serviceAccount;
                }
                serviceAccount["Username"] = request.ServiceAccountUsername?.Trim() ?? "";
                if (!string.IsNullOrEmpty(request.ServiceAccountPassword))
                {
                    serviceAccount["ProtectedPassword"] = _secretProtector.Protect(request.ServiceAccountPassword);
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

    /// <summary>
    /// Builds the structured KPI-visibility tree (categories -> items) consumed by the Angular UI,
    /// mirroring the toggles and labels in the former Razor Settings view. The ARConfiguration group
    /// is admin-only. The whole KpiSettings bag is still round-tripped on save, so items not listed
    /// here keep their stored values.
    /// </summary>
    private static List<KpiVisibilityCategory> BuildKpiVisibility(KpiSettings k, bool isActiveRolesAdmin)
    {
        var categories = new List<KpiVisibilityCategory>
        {
            new()
            {
                Key = "Overview", Label = "Overview", Enabled = k.OverviewEnabled, AdminOnly = false,
                Items = new()
            }
        };

        if (isActiveRolesAdmin)
        {
            categories.Add(new KpiVisibilityCategory
            {
                Key = "ARConfiguration", Label = "Active Roles Configuration", Enabled = k.ARConfigurationEnabled, AdminOnly = true,
                Items = new()
                {
                    new() { Key = "ActiveRolesAdminsEnabled", Label = "Active Roles Admins", Enabled = k.ActiveRolesAdminsEnabled },
                    new() { Key = "DomainsEnabled", Label = "Managed Domains", Enabled = k.DomainsEnabled },
                    new() { Key = "ServersEnabled", Label = "AR Servers", Enabled = k.ServersEnabled },
                    new() { Key = "AccessTemplatesEnabled", Label = "Access Templates", Enabled = k.AccessTemplatesEnabled },
                    new() { Key = "AccessTemplateLinksEnabled", Label = "Access Template Links", Enabled = k.AccessTemplateLinksEnabled },
                    new() { Key = "DynamicGroupsEnabled", Label = "Dynamic Groups", Enabled = k.DynamicGroupsEnabled },
                    new() { Key = "GroupFamiliesEnabled", Label = "Group Families", Enabled = k.GroupFamiliesEnabled },
                    new() { Key = "ManagedUnitsEnabled", Label = "Managed Units", Enabled = k.ManagedUnitsEnabled },
                    new() { Key = "PolicyObjectLinksEnabled", Label = "Policy Object Links", Enabled = k.PolicyObjectLinksEnabled },
                    new() { Key = "PolicyObjectsEnabled", Label = "Policy Objects", Enabled = k.PolicyObjectsEnabled },
                    new() { Key = "VirtualAttributesEnabled", Label = "Virtual Attributes", Enabled = k.VirtualAttributesEnabled },
                    new() { Key = "ConfigDatabasesEnabled", Label = "Config Databases", Enabled = k.ConfigDatabasesEnabled },
                    new() { Key = "HistoryDatabasesEnabled", Label = "History Databases", Enabled = k.HistoryDatabasesEnabled },
                    new() { Key = "ScheduledTasksEnabled", Label = "Scheduled Tasks", Enabled = k.ScheduledTasksEnabled },
                    new() { Key = "EmptyAccessTemplatesEnabled", Label = "Empty Access Templates", Enabled = k.EmptyAccessTemplatesEnabled },
                    new() { Key = "PolicyObjectsNoRulesEnabled", Label = "Policy Objects With No Rules", Enabled = k.PolicyObjectsNoRulesEnabled },
                    new() { Key = "UnlinkedAccessTemplatesEnabled", Label = "Unlinked User-Created Access Templates", Enabled = k.UnlinkedAccessTemplatesEnabled },
                    new() { Key = "DenyAccessTemplatesEnabled", Label = "Access Templates With Deny Permissions", Enabled = k.DenyAccessTemplatesEnabled },
                    new() { Key = "UnlinkedPolicyObjectsEnabled", Label = "Unlinked User-Created Policy Objects", Enabled = k.UnlinkedPolicyObjectsEnabled },
                    new() { Key = "OrphanAccessTemplateLinksEnabled", Label = "Orphaned Access Template Links", Enabled = k.OrphanAccessTemplateLinksEnabled },
                    new() { Key = "OrphanPolicyObjectLinksEnabled", Label = "Orphaned Policy Object Links", Enabled = k.OrphanPolicyObjectLinksEnabled },
                    new() { Key = "DynamicGroupsBrokenRulesEnabled", Label = "Dynamic Groups With Broken Rules", Enabled = k.DynamicGroupsBrokenRulesEnabled },
                    new() { Key = "ManagedUnitsBrokenRulesEnabled", Label = "Managed Units With Broken Rules", Enabled = k.ManagedUnitsBrokenRulesEnabled },
                    new() { Key = "ScriptModulesEnabled", Label = "Script Modules", Enabled = k.ScriptModulesEnabled },
                    new() { Key = "WorkflowsEnabled", Label = "Workflows", Enabled = k.WorkflowsEnabled }
                }
            });
        }

        categories.Add(new KpiVisibilityCategory
        {
            Key = "ADGovernance", Label = "AD Governance", Enabled = k.ADGovernanceEnabled, AdminOnly = false,
            Items = new()
            {
                new() { Key = "NoGroupOwnerEnabled", Label = "No Group Owner", Enabled = k.NoGroupOwnerEnabled },
                new() { Key = "NeverLoggedInEnabled", Label = "Never Logged In", Enabled = k.NeverLoggedInEnabled },
                new() { Key = "NoManagerUserEnabled", Label = "No Manager (User)", Enabled = k.NoManagerUserEnabled },
                new() { Key = "NoManagerServiceAccountEnabled", Label = "No Manager (Service Account)", Enabled = k.NoManagerServiceAccountEnabled },
                new() { Key = "ServiceAccountsEnabled", Label = "Service Accounts", Enabled = k.ServiceAccountsEnabled },
                new() { Key = "GmsaServiceAccountsEnabled", Label = "gMSA Service Accounts", Enabled = k.GmsaServiceAccountsEnabled },
                new() { Key = "SmsaServiceAccountsEnabled", Label = "sMSA Service Accounts", Enabled = k.SmsaServiceAccountsEnabled },
                new() { Key = "ExpiredUsersEnabled", Label = "Expired Users", Enabled = k.ExpiredUsersEnabled },
                new() { Key = "ReversibleEncryptionEnabled", Label = "Reversible Encryption", Enabled = k.ReversibleEncryptionEnabled },
                new() { Key = "UserAccountLockedOutEnabled", Label = "Locked Out", Enabled = k.UserAccountLockedOutEnabled },
                new() { Key = "EmptyGroupsEnabled", Label = "Empty Groups", Enabled = k.EmptyGroupsEnabled },
                new() { Key = "CircularGroupNestingEnabled", Label = "Circular Group Nesting", Enabled = k.CircularGroupNestingEnabled }
            }
        });

        categories.Add(new KpiVisibilityCategory
        {
            Key = "PrivilegedGroups", Label = "Privileged Groups", Enabled = k.PrivilegedGroupsEnabled, AdminOnly = false,
            Items = new()
            {
                new() { Key = "AccountOperatorsEnabled", Label = "Account Operators", Enabled = k.AccountOperatorsEnabled },
                new() { Key = "AdministratorsEnabled", Label = "Administrators", Enabled = k.AdministratorsEnabled },
                new() { Key = "BackupOperatorsEnabled", Label = "Backup Operators", Enabled = k.BackupOperatorsEnabled },
                new() { Key = "DomainAdminsEnabled", Label = "Domain Admins", Enabled = k.DomainAdminsEnabled },
                new() { Key = "ServerOperatorsEnabled", Label = "Server Operators", Enabled = k.ServerOperatorsEnabled },
                new() { Key = "EnterpriseAdminsEnabled", Label = "Enterprise Admins", Enabled = k.EnterpriseAdminsEnabled },
                new() { Key = "SchemaAdminsEnabled", Label = "Schema Admins", Enabled = k.SchemaAdminsEnabled }
            }
        });

        categories.Add(new KpiVisibilityCategory
        {
            Key = "ADUserAccountsCategory", Label = "AD User Accounts", Enabled = k.ADUserAccountsCategoryEnabled, AdminOnly = false,
            Items = new()
            {
                new() { Key = "ADUserAccountsEnabled", Label = "AD User Accounts", Enabled = k.ADUserAccountsEnabled },
                new() { Key = "EnabledUsersEnabled", Label = "Enabled Users", Enabled = k.EnabledUsersEnabled },
                new() { Key = "DisabledUsersEnabled", Label = "Disabled Users", Enabled = k.DisabledUsersEnabled },
                new() { Key = "ExpiringUsersEnabled", Label = "Expiring Users", Enabled = k.ExpiringUsersEnabled },
                new() { Key = "PasswordNeverExpiresEnabled", Label = "Password Never Expires", Enabled = k.PasswordNeverExpiresEnabled },
                new() { Key = "MustChangePasswordEnabled", Label = "Must Change Password", Enabled = k.MustChangePasswordEnabled },
                new() { Key = "PasswordNotRequiredEnabled", Label = "Password Not Required", Enabled = k.PasswordNotRequiredEnabled },
                new() { Key = "SmartCardRequiredEnabled", Label = "Smart Card Required", Enabled = k.SmartCardRequiredEnabled },
                new() { Key = "CannotChangePasswordEnabled", Label = "Cannot Change Password", Enabled = k.CannotChangePasswordEnabled },
                new() { Key = "NoKerberosPreauthEnabled", Label = "No Kerberos Pre-Auth", Enabled = k.NoKerberosPreauthEnabled },
                new() { Key = "UserReversibleEncryptionEnabled", Label = "Reversible Encryption", Enabled = k.UserReversibleEncryptionEnabled },
                new() { Key = "SensitiveCannotDelegateEnabled", Label = "Sensitive / Cannot Delegate", Enabled = k.SensitiveCannotDelegateEnabled },
                new() { Key = "TrustedForDelegationEnabled", Label = "Trusted For Delegation", Enabled = k.TrustedForDelegationEnabled },
                new() { Key = "UseDesEncryptionEnabled", Label = "Uses DES Encryption", Enabled = k.UseDesEncryptionEnabled },
                new() { Key = "DeprovisionedUsersEnabled", Label = "Deprovisioned Users", Enabled = k.DeprovisionedUsersEnabled },
                new() { Key = "SpnUserAccountsEnabled", Label = "SPN User Accounts", Enabled = k.SpnUserAccountsEnabled },
                new() { Key = "StaleUsersEnabled", Label = "Stale Users", Enabled = k.StaleUsersEnabled }
            }
        });

        categories.Add(new KpiVisibilityCategory
        {
            Key = "ADGroupsCategory", Label = "AD Groups", Enabled = k.ADGroupsCategoryEnabled, AdminOnly = false,
            Items = new()
            {
                new() { Key = "ADGroupsEnabled", Label = "AD Groups", Enabled = k.ADGroupsEnabled },
                new() { Key = "DistributionGroupsEnabled", Label = "Distribution Groups", Enabled = k.DistributionGroupsEnabled },
                new() { Key = "DomainLocalGroupsEnabled", Label = "Domain Local Groups", Enabled = k.DomainLocalGroupsEnabled },
                new() { Key = "GlobalGroupsEnabled", Label = "Global Groups", Enabled = k.GlobalGroupsEnabled },
                new() { Key = "MailEnabledSecurityGroupsEnabled", Label = "Mail-Enabled Security Groups", Enabled = k.MailEnabledSecurityGroupsEnabled },
                new() { Key = "SecurityGroupsEnabled", Label = "Security Groups", Enabled = k.SecurityGroupsEnabled },
                new() { Key = "UniversalGroupsEnabled", Label = "Universal Groups", Enabled = k.UniversalGroupsEnabled },
                new() { Key = "AdminCountEnabled", Label = "AdminCount", Enabled = k.AdminCountEnabled }
            }
        });

        categories.Add(new KpiVisibilityCategory
        {
            Key = "Licensing", Label = "Licensing", Enabled = k.LicensingEnabled, AdminOnly = false,
            Items = new()
        });

        return categories;
    }
}
