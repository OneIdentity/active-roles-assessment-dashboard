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

    public SettingsModel(IOptionsMonitor<ActiveRolesConfig> arConfig, UserSettingsService userSettingsService, IWebHostEnvironment env, IStringLocalizer<SettingsModel> localizer, PerUserSummaryCache summaryCache, ServiceAccountSecretProtector secretProtector, RoleService roleService, DirectoryFactsResolver directoryFacts)
    {
        _arConfig = arConfig;
        _userSettingsService = userSettingsService;
        _env = env;
        _localizer = localizer;
        _summaryCache = summaryCache;
        _secretProtector = secretProtector;
        _roleService = roleService;
        _directoryFacts = directoryFacts;
    }

    private readonly RoleService _roleService;
    private readonly DirectoryFactsResolver _directoryFacts;

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
    public int DynamicGroupExpensiveRuleThreshold { get; set; }

    /// <summary>Editable per-KPI Medium/High thresholds (ActiveRoles:KpiThresholds).</summary>
    [BindProperty]
    public List<KpiThresholdRow> KpiThresholdRows { get; set; } = new();

    /// <summary>Validation errors for KPI threshold rows (rows with errors are not saved).</summary>
    public List<string> KpiThresholdErrors { get; } = new();

    public class KpiThresholdRow
    {
        public string Key { get; set; } = string.Empty;
        public KpiThresholdMode Mode { get; set; } = KpiThresholdMode.Count;
        public double Medium { get; set; }
        public double High { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public int DefaultMedium { get; set; }
        public int DefaultHigh { get; set; }
        public bool IsCustomized { get; set; }
    }

    private void LoadKpiThresholdRows(ActiveRolesConfig config)
    {
        KpiThresholdRows = KpiThresholdDefaults.BuiltIn
            .Select(kv =>
            {
                config.KpiThresholds.TryGetValue(kv.Key, out var configured);
                if (configured?.Validate() != null) configured = null;
                return new KpiThresholdRow
                {
                    Key = kv.Key,
                    DisplayName = KpiInfo.All.FirstOrDefault(k => string.Equals(k.Key, kv.Key, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? kv.Key,
                    DefaultMedium = kv.Value.Medium,
                    DefaultHigh = kv.Value.High,
                    Mode = configured?.Mode ?? KpiThresholdMode.Count,
                    Medium = configured?.Medium ?? kv.Value.Medium,
                    High = configured?.High ?? kv.Value.High,
                    IsCustomized = configured != null
                };
            })
            .OrderBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Builds the KpiThresholds JSON section. Only rows that differ from the built-in Count defaults
    /// are persisted; invalid rows are reported and keep their previously saved value.
    /// </summary>
    private JsonObject BuildKpiThresholdsJson(ActiveRolesConfig config)
    {
        var section = new JsonObject();
        foreach (var (key, value) in config.KpiThresholds.Where(kv => !KpiThresholdDefaults.BuiltIn.ContainsKey(kv.Key)))
        {
            var preserved = new JsonObject { ["Mode"] = value.Mode.ToString(), ["Medium"] = value.Medium, ["High"] = value.High };
            if (!string.IsNullOrWhiteSpace(value.TotalKpiKey)) preserved["TotalKpiKey"] = value.TotalKpiKey;
            section[key] = preserved;
        }
        foreach (var row in KpiThresholdRows)
        {
            if (!KpiThresholdDefaults.BuiltIn.TryGetValue(row.Key, out var builtIn)) continue;
            var candidate = new KpiThresholdConfig { Mode = row.Mode, Medium = row.Medium, High = row.High };
            if (config.KpiThresholds.TryGetValue(row.Key, out var existing))
                candidate.TotalKpiKey = existing.TotalKpiKey;
            var error = candidate.Validate();
            if (error != null)
            {
                var name = KpiInfo.All.FirstOrDefault(k => string.Equals(k.Key, row.Key, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? row.Key;
                KpiThresholdErrors.Add($"{name}: {error}");
                if (config.KpiThresholds.TryGetValue(row.Key, out var previous) && previous.Validate() == null)
                    candidate = previous;
                else
                    continue;
            }
            if (candidate.Mode == KpiThresholdMode.Count
                && candidate.Medium == builtIn.Medium && candidate.High == builtIn.High)
                continue;
            var entry = new JsonObject
            {
                ["Mode"] = candidate.Mode.ToString(),
                ["Medium"] = candidate.Medium,
                ["High"] = candidate.High
            };
            if (!string.IsNullOrWhiteSpace(candidate.TotalKpiKey))
                entry["TotalKpiKey"] = candidate.TotalKpiKey;
            section[row.Key] = entry;
        }
        return section;
    }

    /// <summary>
    /// Writes KpiThresholds into appsettings.&lt;Environment&gt;.json when that file already defines
    /// the section (it would otherwise override the base file).
    /// </summary>
    private void SaveEnvironmentKpiThresholds(JsonObject section) => SaveEnvironmentSection("KpiThresholds", section);

    /// <summary>Global exclusion patterns, one per line (ActiveRoles:KpiExclusions:Global).</summary>
    [BindProperty]
    public string GlobalKpiExclusions { get; set; } = string.Empty;

    /// <summary>Per-KPI exclusion patterns (ActiveRoles:KpiExclusions:PerKpi).</summary>
    [BindProperty]
    public List<KpiExclusionRow> KpiExclusionRows { get; set; } = new();

    /// <summary>Validation messages for KPI exclusions (duplicates of global patterns are dropped).</summary>
    public List<string> KpiExclusionErrors { get; } = new();

    public class KpiExclusionRow
    {
        public string Key { get; set; } = string.Empty;
        public string Patterns { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    private static List<string> SplitPatterns(string? text) =>
        KpiExclusionConfig.Normalize((text ?? string.Empty).Split('\n'));

    private void LoadKpiExclusionRows(ActiveRolesConfig config)
    {
        var cfg = config.KpiExclusions ?? new KpiExclusionConfig();
        GlobalKpiExclusions = string.Join("\n", KpiExclusionConfig.Normalize(cfg.Global));
        KpiExclusionRows = KpiInfo.All
            .Where(k => k.HasDrilldown)
            .Select(k =>
            {
                var perKpi = cfg.PerKpi?.FirstOrDefault(kv => string.Equals(kv.Key, k.Key, StringComparison.OrdinalIgnoreCase)).Value;
                return new KpiExclusionRow
                {
                    Key = k.Key,
                    DisplayName = k.DisplayName,
                    Patterns = string.Join("\n", KpiExclusionConfig.Normalize(perKpi))
                };
            })
            .OrderBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Builds the KpiExclusions JSON section from the posted values. Per-KPI patterns that duplicate a
    /// global pattern are dropped and reported. Per-KPI entries for KPIs not shown in the UI are preserved.
    /// </summary>
    private JsonObject BuildKpiExclusionsJson(ActiveRolesConfig config)
    {
        var global = SplitPatterns(GlobalKpiExclusions);
        var globalSet = new HashSet<string>(global, StringComparer.OrdinalIgnoreCase);
        var perKpi = new JsonObject();

        var shownKeys = new HashSet<string>(KpiExclusionRows.Select(r => r.Key), StringComparer.OrdinalIgnoreCase);
        foreach (var (key, patterns) in config.KpiExclusions?.PerKpi ?? new())
        {
            if (shownKeys.Contains(key)) continue;
            var kept = KpiExclusionConfig.Normalize(patterns).Where(p => !globalSet.Contains(p)).ToList();
            if (kept.Count > 0) perKpi[key] = new JsonArray(kept.Select(p => (JsonNode)JsonValue.Create(p)!).ToArray());
        }

        foreach (var row in KpiExclusionRows)
        {
            if (string.IsNullOrWhiteSpace(row.Key)) continue;
            var patterns = SplitPatterns(row.Patterns);
            var duplicates = patterns.Where(globalSet.Contains).ToList();
            if (duplicates.Count > 0)
            {
                var name = KpiInfo.All.FirstOrDefault(k => string.Equals(k.Key, row.Key, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? row.Key;
                KpiExclusionErrors.Add(string.Format(_localizer["Error_ExclusionDuplicatesGlobal"], name, string.Join(", ", duplicates)));
                patterns = patterns.Where(p => !globalSet.Contains(p)).ToList();
            }
            if (patterns.Count > 0)
                perKpi[row.Key] = new JsonArray(patterns.Select(p => (JsonNode)JsonValue.Create(p)!).ToArray());
        }

        return new JsonObject
        {
            ["Global"] = new JsonArray(global.Select(p => (JsonNode)JsonValue.Create(p)!).ToArray()),
            ["PerKpi"] = perKpi
        };
    }

    /// <summary>
    /// Writes an ActiveRoles sub-section into appsettings.&lt;Environment&gt;.json when that file already
    /// defines it (it would otherwise override the base file).
    /// </summary>
    private void SaveEnvironmentSection(string sectionName, JsonObject section)
    {
        var envPath = Path.Combine(_env.ContentRootPath, $"appsettings.{_env.EnvironmentName}.json");
        if (!System.IO.File.Exists(envPath)) return;
        var node = JsonNode.Parse(System.IO.File.ReadAllText(envPath), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var activeRoles = (node as JsonObject)?["ActiveRoles"] as JsonObject;
        if (activeRoles == null || !activeRoles.ContainsKey(sectionName)) return;
        activeRoles[sectionName] = section.DeepClone();
        System.IO.File.WriteAllText(envPath, node!.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }));
    }

    [BindProperty]
    public KpiSettings KpiSettings { get; set; } = new();

    [BindProperty]
    public string CustomNoManagerUserFilter { get; set; } = string.Empty;
    [BindProperty]
    public string CustomNoManagerServiceAccountFilter { get; set; } = string.Empty;

    // REST API Configuration (restart required)
    [BindProperty]
    public string ApiBaseUrl { get; set; } = string.Empty;
    [BindProperty]
    public string RstsUrl { get; set; } = string.Empty;
    [BindProperty]
    public string Resource { get; set; } = string.Empty;
    [BindProperty]
    public bool IgnoreSslErrors { get; set; }
    [BindProperty]
    public int PageSize { get; set; } = ActiveRolesConfig.DefaultPageSize;

    // Read-only upper bound for PageSize (edited only in appsettings.json).
    public int MaxPageSize => _arConfig.CurrentValue.EffectiveMaxPageSize;

    public string? PageSizeError { get; set; }

    // Google Analytics (GA4) usage tracking (admin-only).
    [BindProperty]
    public bool AnalyticsEnabled { get; set; }
    [BindProperty]
    public string AnalyticsMeasurementId { get; set; } = string.Empty;

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
    public string DefaultADUserAccountsFilter { get; set; } = string.Empty;
    [BindProperty]
    public string DefaultADGroupsFilter { get; set; } = string.Empty;

    // Role/admin groups (stored by NAME under the RoleGroups section) plus the AD/Entra flag.
    [BindProperty]
    public RoleGroupDirectoryType RoleGroupsDirectoryType { get; set; } = RoleGroupDirectoryType.ActiveDirectory;
    [BindProperty]
    public string ActiveRolesAdminsGroup { get; set; } = string.Empty;
    [BindProperty]
    public string DashboardAdminsGroup { get; set; } = string.Empty;
    [BindProperty]
    public string AuditorsGroup { get; set; } = string.Empty;
    [BindProperty]
    public string PowerUsersGroup { get; set; } = string.Empty;

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

    // Performance Tests (automatic performance-trending sampler)
    [BindProperty]
    public bool PerfTrendingEnabled { get; set; }
    [BindProperty]
    public int PerfTrendingIntervalMinutes { get; set; }
    [BindProperty]
    public int PerfTrendingRetentionHours { get; set; }

    /// <summary>Lowest sampling interval permitted, surfaced as the input minimum and hint.</summary>
    public int PerfTrendingMinimumInterval => PerformanceTrendingConfig.MinimumIntervalMinutes;

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

    /// <summary>
    /// Posted permission grants encoded as "Role:Permission" tokens (one per selected checkbox).
    /// The Dashboard Administrator role is fixed to full permissions and is ignored on save.
    /// </summary>
    [BindProperty]
    public List<string> RolePermissionGrants { get; set; } = new();

    /// <summary>Current effective role/permission matrix (loaded from encrypted config, merged with defaults).</summary>
    public IReadOnlyDictionary<DashboardRole, IReadOnlySet<DashboardPermission>> RoleMatrix { get; private set; }
        = new Dictionary<DashboardRole, IReadOnlySet<DashboardPermission>>();

    public IReadOnlyList<DashboardRole> DisplayRoles => RolePermissionRegistry.AllRoles;
    public IReadOnlyList<DashboardPermission> DisplayPermissions => RolePermissionRegistry.AllPermissions;

    public string RoleName(DashboardRole role) => RolePermissionRegistry.RoleDisplay[role].DefaultName;
    public string PermissionName(DashboardPermission permission) => RolePermissionRegistry.PermissionDisplay[permission].DefaultName;

    public bool IsRoleFixed(DashboardRole role) => role == DashboardRole.DashboardAdministrator;

    public ActiveRolesConfig Defaults => _arConfig.CurrentValue;

    public bool SettingsChanged { get; set; }
    public bool IsActiveRolesAdmin { get; set; }
    public bool RestartRequired { get; set; }

    /// <summary>The current user's resolved effective permission set (from their dashboard role).</summary>
    public IReadOnlySet<DashboardPermission> Permissions { get; private set; } =
        new HashSet<DashboardPermission>();

    /// <summary>True when the user may open the Settings page at all (any settings permission).</summary>
    public bool CanAccessSettings => RolePermissionRegistry.CanAccessSettings(Permissions);

    /// <summary>True when the user may view/change the User settings category.</summary>
    public bool CanManageUserSettings => RolePermissionRegistry.CanManageUserSettings(Permissions);

    /// <summary>True when the user may view the System settings category (view or manage).</summary>
    public bool CanViewSystemSettings => RolePermissionRegistry.CanViewSystemSettings(Permissions);

    /// <summary>True when the user may modify the System settings category.</summary>
    public bool CanManageSystemSettings => RolePermissionRegistry.CanManageSystemSettings(Permissions);

    /// <summary>
    /// Resolves the current user's Active Roles admin flag, dashboard role, and effective
    /// permission set, populating <see cref="IsActiveRolesAdmin"/> and <see cref="Permissions"/>.
    /// Mirrors the epoch-aware session logic in <c>DashboardPageModel.InitializePageAsync</c> so
    /// the Settings page enforces the same role facts server-side.
    /// </summary>
    private async Task ResolvePermissionsAsync()
    {
        var username = User.Identity?.Name ?? "";
        var token = HttpContext.Session.GetString("AccessToken");

        var currentEpoch = _directoryFacts.CurrentEpoch;
        var sessionEpoch = HttpContext.Session.GetString("DirectoryFactsEpoch");
        var sessionAdmin = HttpContext.Session.GetString("IsActiveRolesAdmin");
        var sessionRole = HttpContext.Session.GetString("DashboardRole");

        DashboardRole role;
        if (sessionEpoch == currentEpoch.ToString()
            && sessionAdmin != null
            && sessionRole != null
            && Enum.TryParse(sessionRole, out DashboardRole parsedRole))
        {
            IsActiveRolesAdmin = bool.TryParse(sessionAdmin, out var val) && val;
            role = parsedRole;
        }
        else if (!string.IsNullOrEmpty(token))
        {
            var facts = await _directoryFacts.ResolveAsync(token, username);
            IsActiveRolesAdmin = facts.IsActiveRolesAdmin;
            role = facts.Role;

            HttpContext.Session.SetString("IsActiveRolesAdmin", facts.IsActiveRolesAdmin.ToString());
            HttpContext.Session.SetString("DashboardRole", facts.Role.ToString());
            HttpContext.Session.SetString("DirectoryFactsEpoch", facts.Epoch.ToString());
        }
        else
        {
            // No token available; fall back to the least-privileged base role.
            IsActiveRolesAdmin = false;
            role = DashboardRole.User;
        }

        Permissions = _roleService.GetPermissions(role);
    }

    public string? Message { get; set; }

    public async Task OnGet()
    {
        await ResolvePermissionsAsync();
        var config = _arConfig.CurrentValue;
        WebInterfaceUrl = config.WebInterfaceUrl;
        RoleMatrix = _roleService.GetMatrix();
        var username = User.Identity?.Name ?? "";
        var userSettings = _userSettingsService.Load(username);

        AutoRefreshMinutes = userSettings.AutoRefreshMinutes;
        KpiSettings = userSettings.KpiSettings;
        Language = SupportedLanguage.All.Any(l => l.Code == userSettings.Language)
            ? userSettings.Language
            : SupportedLanguage.DefaultCode;

        // Load KPI configuration from appsettings
        CustomNoManagerUserFilter = config.CustomNoManagerUserFilter;
        CustomNoManagerServiceAccountFilter = config.CustomNoManagerServiceAccountFilter;
        EntraLargeGroupMemberThreshold = config.Entra.LargeGroupMemberThreshold;
        DynamicGroupExpensiveRuleThreshold = config.DynamicGroupExpensiveRuleThreshold;
        LoadKpiThresholdRows(config);
        LoadKpiExclusionRows(config);

        // REST API Configuration
        ApiBaseUrl = config.ApiBaseUrl;
        RstsUrl = config.RstsUrl;
        Resource = config.Resource;
        IgnoreSslErrors = config.IgnoreSslErrors;
        PageSize = config.EffectivePageSize;

        // Google Analytics (GA4) usage tracking
        AnalyticsEnabled = config.Analytics.Enabled;
        AnalyticsMeasurementId = config.Analytics.MeasurementId;

        // Default Filters
        DefaultNoGroupOwnerFilter = config.DefaultFilters.NoGroupOwner;
        DefaultNoManagerUserFilter = config.DefaultFilters.NoManagerUser;
        DefaultNoManagerServiceAccountFilter = config.DefaultFilters.NoManagerServiceAccount;
        DefaultUserAccountExpiredFilter = config.DefaultFilters.UserAccountExpired;
        DefaultUserAccountLockedOutFilter = config.DefaultFilters.UserAccountLockedOut;
        DefaultEmptyGroupsFilter = config.DefaultFilters.EmptyGroups;
        DefaultADUserAccountsFilter = config.DefaultFilters.ADUserAccounts;
        DefaultADGroupsFilter = config.DefaultFilters.ADGroups;

        // Role/admin groups (by name) + directory-type flag
        RoleGroupsDirectoryType = config.RoleGroups.DirectoryType;
        ActiveRolesAdminsGroup = config.RoleGroups.ActiveRolesAdmins;
        DashboardAdminsGroup = config.RoleGroups.DashboardAdmins;
        AuditorsGroup = config.RoleGroups.Auditors;
        PowerUsersGroup = config.RoleGroups.PowerUsers;

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

        // Performance Tests (automatic performance-trending sampler)
        PerfTrendingEnabled = config.PerformanceTrending.Enabled;
        PerfTrendingIntervalMinutes = config.PerformanceTrending.SnapshotIntervalMinutes;
        PerfTrendingRetentionHours = config.PerformanceTrending.RetentionHours;

        // Licensing Thresholds
        LicensedDomainObjects = config.Licensing.DomainObjects;
        LicensedPartitionObjects = config.Licensing.PartitionObjects;
        LicensedAzureObjects = config.Licensing.AzureObjects;
        LicensedSaasObjects = config.Licensing.SaasObjects;
        LicensedTotalObjects = config.Licensing.TotalObjects;
    }

    public async Task<IActionResult> OnPost()
    {
        await ResolvePermissionsAsync();

        // Server-side authorization: a user with no settings permission cannot change anything,
        // even by POSTing directly to the page. Re-render with the access-denied state.
        if (!CanAccessSettings)
        {
            Message = _localizer["AccessDenied"];
            return Page();
        }

        if (AutoRefreshMinutes < 0)
            AutoRefreshMinutes = 0;

        if (EntraLargeGroupMemberThreshold < 1)
            EntraLargeGroupMemberThreshold = 1;

        if (DynamicGroupExpensiveRuleThreshold < 1)
            DynamicGroupExpensiveRuleThreshold = 1;

        // REST API page size must be within 1..MaxPageSize; keep the current value when invalid.
        if (PageSize < 1 || PageSize > MaxPageSize)
        {
            PageSizeError = string.Format(_localizer["Error_PageSizeRange"].Value, MaxPageSize);
            PageSize = _arConfig.CurrentValue.EffectivePageSize;
        }

        // Clamp licensing thresholds to non-negative values.
        LicensedDomainObjects = Math.Max(0, LicensedDomainObjects);
        LicensedPartitionObjects = Math.Max(0, LicensedPartitionObjects);
        LicensedAzureObjects = Math.Max(0, LicensedAzureObjects);
        LicensedSaasObjects = Math.Max(0, LicensedSaasObjects);
        LicensedTotalObjects = Math.Max(0, LicensedTotalObjects);

        // Clamp performance-trending settings. The sampler enforces a hard minimum interval
        // (EffectiveIntervalMinutes) regardless; retention must be at least one hour.
        PerfTrendingIntervalMinutes = Math.Max(PerformanceTrendingConfig.MinimumIntervalMinutes, PerfTrendingIntervalMinutes);
        PerfTrendingRetentionHours = Math.Max(1, PerfTrendingRetentionHours);

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
        RestartRequired = CanManageSystemSettings && (
            !string.Equals((ApiBaseUrl ?? "").Trim(), config.ApiBaseUrl ?? "", StringComparison.Ordinal) ||
            !string.Equals((RstsUrl ?? "").Trim(), config.RstsUrl ?? "", StringComparison.Ordinal) ||
            !string.Equals((Resource ?? "").Trim(), config.Resource ?? "", StringComparison.Ordinal) ||
            IgnoreSslErrors != config.IgnoreSslErrors ||
            !string.Equals((ServiceAccountUsername ?? "").Trim(), config.ServiceAccount.Username ?? "", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(ServiceAccountPassword));

        var username = User.Identity?.Name ?? "";
        var postedThresholds = KpiThresholdRows.ToList();
        var postedGlobalExclusions = GlobalKpiExclusions;
        var postedExclusions = KpiExclusionRows.ToList();

        // User settings (Language
        if (CanManageUserSettings)
        {
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

            // Also store in session for the dashboard to pick up immediately
            HttpContext.Session.SetInt32("AutoRefreshMinutes", AutoRefreshMinutes);
            HttpContext.Session.SetString("KpiSettings", JsonSerializer.Serialize(KpiSettings));

            // Clear cached dashboard data since settings changed
            _summaryCache.Clear(username);
        }

        // System settings (REST API, filters, licensing, etc.) require ManageSystemSettings. The
        // Role/permission matrix is stricter still: only Dashboard/Active Roles admins may persist
        // it, so pass that flag through to SaveAppSettings.
        if (CanManageSystemSettings)
        {
            SaveAppSettings(persistRoleMatrix: IsActiveRolesAdmin);
        }

        LoadKpiThresholdRows(_arConfig.CurrentValue);
        foreach (var row in KpiThresholdRows)
        {
            var posted = postedThresholds.FirstOrDefault(p => string.Equals(p.Key, row.Key, StringComparison.OrdinalIgnoreCase));
            if (posted == null) continue;
            row.Mode = posted.Mode;
            row.Medium = posted.Medium;
            row.High = posted.High;
            row.IsCustomized = posted.Mode != KpiThresholdMode.Count || posted.Medium != row.DefaultMedium || posted.High != row.DefaultHigh;
        }

        if (CanManageSystemSettings)
        {
            var globalSet = new HashSet<string>(SplitPatterns(postedGlobalExclusions), StringComparer.OrdinalIgnoreCase);
            LoadKpiExclusionRows(_arConfig.CurrentValue);
            GlobalKpiExclusions = string.Join("\n", globalSet);
            foreach (var row in KpiExclusionRows)
            {
                var posted = postedExclusions.FirstOrDefault(p => string.Equals(p.Key, row.Key, StringComparison.OrdinalIgnoreCase));
                if (posted == null) continue;
                row.Patterns = string.Join("\n", SplitPatterns(posted.Patterns).Where(p => !globalSet.Contains(p)));
            }
        }
        else
        {
            LoadKpiExclusionRows(_arConfig.CurrentValue);
        }

        SettingsChanged = true;
        Message = _localizer["SavedSuccessfully"];
        if (KpiThresholdErrors.Count > 0)
            Message += " " + string.Join(" ", KpiThresholdErrors);
        if (KpiExclusionErrors.Count > 0)
            Message += " " + string.Join(" ", KpiExclusionErrors);
        if (PageSizeError != null)
            Message += " " + PageSizeError;

        return Page();
    }

    private void SaveAppSettings(bool persistRoleMatrix)
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

                // Custom LDAP filter overrides live under the nested CustomFilters section.
                var customFilters = activeRoles["CustomFilters"]?.AsObject();
                if (customFilters is null)
                {
                    customFilters = new JsonObject();
                    activeRoles["CustomFilters"] = customFilters;
                }
                customFilters["NoManagerUser"] = CustomNoManagerUserFilter?.Trim() ?? "";
                customFilters["NoManagerServiceAccount"] = CustomNoManagerServiceAccountFilter?.Trim() ?? "";

                // Entra membership tuning (stored in its own Entra section).
                var entra = activeRoles["Entra"]?.AsObject();
                if (entra is null)
                {
                    entra = new JsonObject();
                    activeRoles["Entra"] = entra;
                }
                entra["LargeGroupMemberThreshold"] = EntraLargeGroupMemberThreshold;

                // Dynamic Groups "expensive" rule-count threshold (flat ActiveRoles property).
                activeRoles["DynamicGroupExpensiveRuleThreshold"] = DynamicGroupExpensiveRuleThreshold;

                // Per-KPI Medium/High thresholds (only values differing from built-in defaults).
                var kpiThresholds = BuildKpiThresholdsJson(_arConfig.CurrentValue);
                activeRoles["KpiThresholds"] = kpiThresholds;
                SaveEnvironmentKpiThresholds(kpiThresholds);

                // KPI exclusion patterns (global + per-KPI, no per-KPI duplicates of global).
                var kpiExclusions = BuildKpiExclusionsJson(_arConfig.CurrentValue);
                activeRoles["KpiExclusions"] = kpiExclusions;
                SaveEnvironmentSection("KpiExclusions", kpiExclusions);

                // REST API Configuration (takes effect after a restart)
                activeRoles["ApiBaseUrl"] = ApiBaseUrl?.Trim() ?? "";
                activeRoles["RstsUrl"] = RstsUrl?.Trim() ?? "";
                activeRoles["Resource"] = Resource?.Trim() ?? "";
                activeRoles["IgnoreSslErrors"] = IgnoreSslErrors;
                activeRoles["PageSize"] = PageSize;
                activeRoles["MaxPageSize"] ??= MaxPageSize;

                // Google Analytics (GA4) usage tracking. ConsentGiven mirrors the admin's
                // enable decision here (the toggle is the organizational consent action).
                var analytics = activeRoles["Analytics"]?.AsObject();
                if (analytics is null)
                {
                    analytics = new JsonObject();
                    activeRoles["Analytics"] = analytics;
                }
                analytics["Enabled"] = AnalyticsEnabled;
                analytics["MeasurementId"] = AnalyticsMeasurementId?.Trim() ?? "";
                analytics["ConsentGiven"] = AnalyticsEnabled;

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
                defaultFilters["ADUserAccounts"] = DefaultADUserAccountsFilter?.Trim() ?? "";
                defaultFilters["ADGroups"] = DefaultADGroupsFilter?.Trim() ?? "";

                // Role/admin groups stored by NAME under the RoleGroups section, plus the AD/Entra flag.
                var roleGroups = activeRoles["RoleGroups"]?.AsObject();
                if (roleGroups is null)
                {
                    roleGroups = new JsonObject();
                    activeRoles["RoleGroups"] = roleGroups;
                }
                roleGroups["DirectoryType"] = RoleGroupsDirectoryType.ToString();
                roleGroups["ActiveRolesAdmins"] = ActiveRolesAdminsGroup?.Trim() ?? "";
                roleGroups["DashboardAdmins"] = DashboardAdminsGroup?.Trim() ?? "";
                roleGroups["Auditors"] = AuditorsGroup?.Trim() ?? "";
                roleGroups["PowerUsers"] = PowerUsersGroup?.Trim() ?? "";

                // Role/permission matrix. Rebuild from posted "Role:Permission" grants, ignoring
                // unknown tokens. The Dashboard Administrator role is fixed to full permissions
                // and cannot be edited; ProtectMatrix re-forces it regardless of posted values.
                // Only Dashboard/Active Roles admins may persist the matrix, so skip this write
                // entirely for non-admins (their POST cannot alter role assignments).
                if (persistRoleMatrix)
                {
                    var newMatrix = new Dictionary<DashboardRole, IReadOnlySet<DashboardPermission>>();
                    foreach (var role in RolePermissionRegistry.AllRoles)
                    {
                        newMatrix[role] = new HashSet<DashboardPermission>();
                    }
                    foreach (var grant in RolePermissionGrants ?? new List<string>())
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

                // Performance Tests (automatic performance-trending sampler) in its own section.
                var perfTrending = activeRoles["PerformanceTrending"]?.AsObject();
                if (perfTrending is null)
                {
                    perfTrending = new JsonObject();
                    activeRoles["PerformanceTrending"] = perfTrending;
                }
                perfTrending["Enabled"] = PerfTrendingEnabled;
                perfTrending["SnapshotIntervalMinutes"] = Math.Max(PerformanceTrendingConfig.MinimumIntervalMinutes, PerfTrendingIntervalMinutes);
                perfTrending["RetentionHours"] = Math.Max(1, PerfTrendingRetentionHours);

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
