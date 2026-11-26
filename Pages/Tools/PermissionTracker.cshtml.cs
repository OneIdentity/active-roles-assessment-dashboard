using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Services.PermissionTracker;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Pages.Tools;

/// <summary>
/// Permission Tracker tool: runs a background scan of native AD permissions (object DACLs) and/or
/// Active Roles delegations (Access Template Links) and presents them per source as a tree or table.
/// Requires <see cref="DashboardPermission.UsePermissionTracker"/>.
/// </summary>
public class PermissionTrackerModel : DashboardPageModel
{
    private readonly PermissionScanJobManager _scans;
    private readonly ActiveRolesDashboard.Services.Reporting.ReportExporterFactory _exporters;

    private static readonly System.Resources.ResourceManager Text = new(
        "ActiveRolesDashboard.Resources.Pages.Tools.PermissionTracker", typeof(PermissionTrackerModel).Assembly);

    public PermissionTrackerModel(
        ActiveRolesService arService,
        UserSettingsService userSettingsService,
        IOptionsMonitor<ActiveRolesConfig> arConfig,
        PermissionScanJobManager scans,
        ActiveRolesDashboard.Services.Reporting.ReportExporterFactory exporters)
        : base(arService, userSettingsService, arConfig)
    {
        _scans = scans;
        _exporters = exporters;
    }

    public const int MaxTableRows = 2000;

    [BindProperty(SupportsGet = true)] public string? Job { get; set; }
    /// <summary>"ad" or "ar" result tab.</summary>
    [BindProperty(SupportsGet = true)] public string? Tab { get; set; }
    /// <summary>"tree" (default) or "table".</summary>
    [BindProperty(SupportsGet = true)] public string? View { get; set; }
    [BindProperty(SupportsGet = true)] public bool RiskOnly { get; set; }
    [BindProperty(SupportsGet = true)] public bool HideInherited { get; set; } = true;
    /// <summary>Hides ACEs granted to Exchange setup groups (on by default).</summary>
    [BindProperty(SupportsGet = true)] public bool HideExchange { get; set; } = true;
    [BindProperty(SupportsGet = true)] public bool HideBuiltIn { get; set; }
    /// <summary>Hides ACEs from the class's schema default security descriptor (on by default).</summary>
    [BindProperty(SupportsGet = true)] public bool HideDefault { get; set; } = true;
    /// <summary>Shows Active Roles delegated permissions alongside native AD permissions (on by default).</summary>
    [BindProperty(SupportsGet = true)] public bool ShowArDelegations { get; set; } = true;

    [BindProperty] public string Sources { get; set; } = "ad";
    [BindProperty] public string? BaseDn { get; set; }
    [BindProperty] public bool AllObjects { get; set; }

    public PermissionScanJob? Scan { get; private set; }
    public bool AlreadyRunning { get; private set; }

    /// <summary>"domain" (default naming context, default) or "config" (Configuration partition).</summary>
    [BindProperty(SupportsGet = true)] public string? Partition { get; set; }

    public bool IsTable => string.Equals(View, "table", StringComparison.OrdinalIgnoreCase);
    public bool IsConfigPartition => string.Equals(Partition, "config", StringComparison.OrdinalIgnoreCase);

    /// <summary>Effective result view: "ad" (native AD, optionally with AR delegations overlaid) or "ar".</summary>
    public string CurrentTab => Scan == null || Scan.Request.IncludeActiveDirectory ? "ad" : "ar";

    public bool ShowAd => CurrentTab == "ad";
    public bool ShowAr => CurrentTab == "ar";
    public bool IsArTab => ShowAr;

    /// <summary>Source value to preselect in the scan form (the latest scan's selection).</summary>
    public string SelectedSources => Scan == null ? Sources : CurrentTab;

    /// <summary>DNS names of the domains (or forests) whose objects fall in the given partition.</summary>
    public string PartitionDomains(bool config) => string.Join(", ",
        (Scan?.Result.AdObjects ?? [])
            .Where(o => IsConfigurationDn(o.Object.Dn) == config)
            .Select(o => DnsNameOf(o.Object.Dn))
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase));

    /// <summary>Converts the trailing DC= components of a DN to a DNS name.</summary>
    public static string DnsNameOf(string dn) => string.Join(".",
        dn.Split(',').Select(p => p.Trim())
            .Where(p => p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[3..]));

    private ILookup<string, ArPermissionEntry>? _arByDn;

    /// <summary>Filtered AR delegations on <paramref name="dn"/>, shown in the AD view when enabled.</summary>
    public IEnumerable<ArPermissionEntry> ArFor(string dn)
    {
        if (!ShowArDelegations || Scan == null) return [];
        _arByDn ??= Filter(Scan.Result.ArEntries).ToLookup(e => e.SecuredObjectDn, StringComparer.OrdinalIgnoreCase);
        return _arByDn[dn];
    }

    /// <summary>True when <paramref name="dn"/> lies in the Configuration (or Schema) partition rather than a domain naming context.</summary>
    public static bool IsConfigurationDn(string dn) =>
        dn.Contains("CN=Configuration,DC=", StringComparison.OrdinalIgnoreCase)
        || dn.StartsWith("CN=Configuration,", StringComparison.OrdinalIgnoreCase) && dn.Contains("DC=", StringComparison.OrdinalIgnoreCase);

    /// <summary>AD objects in the currently selected partition.</summary>
    public IEnumerable<AdObjectPermissions> PartitionObjects =>
        Scan?.Result.AdObjects.Where(o => IsConfigurationDn(o.Object.Dn) == IsConfigPartition) ?? [];

    public int PartitionCount(bool config) => Scan?.Result.AdObjects.Count(o => IsConfigurationDn(o.Object.Dn) == config) ?? 0;

    private string Owner => UserCacheKey;

    public override async Task<IActionResult> OnGetAsync([FromQuery] bool cached = false)
    {
        var redirect = await InitializePageAsync();
        if (redirect != null) return redirect;
        if (!CanUsePermissionTracker)
            return Redirect($"{Request.PathBase}/app/");

        Scan = string.IsNullOrEmpty(Job) ? _scans.GetLatest(Owner) : _scans.Get(Job, Owner);
        Job = Scan?.Id;
        return Page();
    }

    public async Task<IActionResult> OnGetStatusAsync(string job)
    {
        if (await InitializePageAsync() != null) return Unauthorized();
        if (!CanUsePermissionTracker) return Forbid();
        var scan = _scans.Get(job, Owner);
        if (scan == null) return NotFound();
        return new JsonResult(new
        {
            status = scan.Status.ToString(),
            phase = scan.Phase,
            processed = scan.ProcessedObjects,
            total = scan.TotalObjects,
            active = scan.IsActive
        });
    }

    public async Task<IActionResult> OnPostStartAsync()
    {
        var redirect = await InitializePageAsync();
        if (redirect != null) return redirect;
        if (!CanUsePermissionTracker) return Forbid();

        var token = GetAccessToken();
        if (string.IsNullOrEmpty(token)) return Redirect($"{Request.PathBase}/app/");

        var request = new PermissionScanRequest
        {
            IncludeActiveDirectory = Sources != "ar",
            IncludeActiveRoles = true,
            IncludeAllObjects = AllObjects,
            BaseDn = string.IsNullOrWhiteSpace(BaseDn) ? null : BaseDn.Trim()
        };

        var job = _scans.Start(Owner, token, request);
        if (job == null)
        {
            AlreadyRunning = true;
            Scan = _scans.GetLatest(Owner);
            Job = Scan?.Id;
            return Page();
        }
        return RedirectToPage(new { job = job.Id });
    }

    public async Task<IActionResult> OnPostCancelAsync(string job)
    {
        var redirect = await InitializePageAsync();
        if (redirect != null) return redirect;
        if (!CanUsePermissionTracker) return Forbid();
        _scans.Cancel(job, Owner);
        return RedirectToPage(new { job });
    }

    public async Task<IActionResult> OnPostConfirmAsync(string job)
    {
        var redirect = await InitializePageAsync();
        if (redirect != null) return redirect;
        if (!CanUsePermissionTracker) return Forbid();
        _scans.Confirm(job, Owner);
        return RedirectToPage(new { job });
    }

    /// <summary>Exports the current scan (with the active filters) as PDF, Excel or Word.</summary>
    public async Task<IActionResult> OnPostExportAsync(string job, ActiveRolesDashboard.Models.Reporting.ReportFormat format)
    {
        var redirect = await InitializePageAsync();
        if (redirect != null) return redirect;
        if (!CanUsePermissionTracker) return Forbid();

        var scan = _scans.Get(job, Owner);
        if (scan == null || scan.IsActive) return RedirectToPage(new { job });
        if (!_exporters.TryGet(format, out var exporter)) return RedirectToPage(new { job });

        var model = PermissionTrackerReportBuilder.Build(scan,
            new PermissionTrackerReportBuilder.Filters(RiskOnly, HideInherited, HideBuiltIn, HideDefault, HideExchange),
            User.Identity?.Name ?? string.Empty,
            key => Text.GetString(key, System.Globalization.CultureInfo.CurrentUICulture) ?? key);
        var bytes = exporter.Export(model);
        return File(bytes, exporter.ContentType, $"PermissionTracker_{scan.StartedAt:yyyyMMdd_HHmm}{exporter.FileExtension}");
    }

    // ---------- view helpers ----------

    public string TrusteeName(string sid) => Scan?.Result.TrusteeName(sid) ?? sid;

    public IEnumerable<AdPermissionEntry> Filter(IEnumerable<AdPermissionEntry> entries) => entries.Where(e =>
        (!RiskOnly || e.IsHighRisk) && (!HideInherited || !e.IsInherited) && (!HideBuiltIn || !e.IsWellKnownTrustee) && (!HideDefault || !e.IsDefault)
        && (!HideExchange || !ExchangeDefaults.IsExchangeTrustee(TrusteeName(e.TrusteeSid))));

    public IEnumerable<ArPermissionEntry> Filter(IEnumerable<ArPermissionEntry> entries) =>
        entries.Where(e => !RiskOnly || e.IsHighRisk);

    public int AdHighRiskCount => Scan?.Result.AdObjects.Sum(o => o.Entries.Count(e => e.IsHighRisk)) ?? 0;
    public int ArHighRiskCount => Scan?.Result.ArEntries.Count(e => e.IsHighRisk) ?? 0;

    /// <summary>Route values preserving the current job and filters, overridden by <paramref name="changes"/>.</summary>
    public Dictionary<string, string> RouteWith(params (string Key, string? Value)[] changes)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["job"] = Job,
            ["tab"] = CurrentTab,
            ["partition"] = IsConfigPartition ? "config" : null,
            ["view"] = IsTable ? "table" : "tree",
            ["riskOnly"] = RiskOnly ? "true" : null,
            ["hideInherited"] = HideInherited ? null : "false",
            ["hideExchange"] = HideExchange ? null : "false",
            ["hideBuiltIn"] = HideBuiltIn ? "true" : null,
            ["hideDefault"] = HideDefault ? null : "false",
            ["showArDelegations"] = ShowArDelegations ? null : "false",
        };
        foreach (var (k, v) in changes) values[k] = v;
        return values.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.OrdinalIgnoreCase);
    }

    public string TabUrl(string tab) => Url.Page("PermissionTracker", RouteWith(("tab", tab)))!;

    public string PartitionUrl(string partition) =>
        Url.Page("PermissionTracker", RouteWith(("partition", partition == "config" ? "config" : null)))!;

    /// <summary>Icon category (CSS modifier) for an AD object class.</summary>
    public static string IconFor(string objectClass) => objectClass.ToLowerInvariant() switch
    {
        "domaindns" => "domain",
        "organizationalunit" => "ou",
        "container" or "builtindomain" or "lostandfound" => "container",
        "group" => "group",
        "computer" => "computer",
        "user" or "inetorgperson" or "msds-managedserviceaccount" or "msds-groupmanagedserviceaccount" => "user",
        "grouppolicycontainer" => "gpo",
        _ => "object"
    };

    public sealed class ObjectNode(AdObjectPermissions item)
    {
        public AdObjectPermissions Item { get; } = item;
        public List<ObjectNode> Children { get; } = [];
    }

    /// <summary>
    /// Arranges scanned AD objects into a tree by DN: each object hangs under its nearest scanned
    /// ancestor. Objects with no entries after filtering are kept only if a descendant has entries.
    /// </summary>
    public List<ObjectNode> BuildAdTree()
    {
        if (Scan == null) return [];
        var nodes = PartitionObjects
            .Where(o => o.Object.Dn.Length > 0)
            .GroupBy(o => o.Object.Dn, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => new ObjectNode(g.First()), StringComparer.OrdinalIgnoreCase);

        var roots = new List<ObjectNode>();
        foreach (var node in nodes.Values)
        {
            var parent = ParentDn(node.Item.Object.Dn);
            ObjectNode? parentNode = null;
            while (parent != null && !nodes.TryGetValue(parent, out parentNode)) parent = ParentDn(parent);
            (parentNode?.Children ?? roots).Add(node);
        }
        return Prune(roots);

        List<ObjectNode> Prune(List<ObjectNode> list)
        {
            var kept = new List<ObjectNode>();
            foreach (var n in list.OrderBy(n => n.Item.Object.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var children = Prune(n.Children);
                n.Children.Clear();
                n.Children.AddRange(children);
                if (children.Count > 0 || Filter(n.Item.Entries).Any() || ArFor(n.Item.Object.Dn).Any() || n.Item.Error != null) kept.Add(n);
            }
            return kept;
        }
    }

    public static string? ParentDn(string dn)
    {
        for (var i = 0; i < dn.Length; i++)
        {
            if (dn[i] == '\\') { i++; continue; }
            if (dn[i] == ',') return i + 1 < dn.Length ? dn[(i + 1)..] : null;
        }
        return null;
    }
}
