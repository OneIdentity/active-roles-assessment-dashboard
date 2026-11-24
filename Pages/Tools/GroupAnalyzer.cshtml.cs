using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Pages.Tools;

/// <summary>
/// Group Analyzer tool: on-demand exploration of group nesting (paths, cycles, multi-path nesting)
/// for one AD or Entra group. Works on the caller's visibility-scoped dashboard summary, so only
/// groups the user may already see are listed or traversed. Requires
/// <see cref="DashboardPermission.UseGroupAnalyzer"/>.
/// </summary>
public class GroupAnalyzerModel : DashboardPageModel
{
    public GroupAnalyzerModel(
        ActiveRolesService arService,
        UserSettingsService userSettingsService,
        IOptionsMonitor<ActiveRolesConfig> arConfig)
        : base(arService, userSettingsService, arConfig)
    {
    }

    public const int MaxPaths = 200;
    public const int MaxMatches = 50;

    [BindProperty(SupportsGet = true)]
    public string? Source { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Query { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Dn { get; set; }

    /// <summary>Optional user (name, CN or DN) whose membership routes into the group are checked.</summary>
    [BindProperty(SupportsGet = true, Name = "user")]
    public string? UserQuery { get; set; }

    /// <summary>DN of the resolved user; null when no user was given or it was not found/ambiguous.</summary>
    public string? UserDn { get; private set; }
    public bool UserNotFound { get; private set; }
    public bool UserAmbiguous { get; private set; }

    /// <summary>Paths from the analyzed group down to the groups the user is a direct member of.</summary>
    public GroupPathExplorer.PathSet? UserPaths { get; private set; }

    private Dictionary<string, List<string>> _rawMembers = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEntra => string.Equals(Source, "entra", StringComparison.OrdinalIgnoreCase);
    public bool EntraMembershipMissing { get; private set; }

    public List<(string Dn, string Name)> Matches { get; } = [];
    public bool MatchesTruncated { get; private set; }
    public bool NotFound { get; private set; }

    public GroupPathExplorer.Report? Report { get; private set; }
    public Dictionary<string, string> NameByDn { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string NameOf(string dn) => NameByDn.TryGetValue(dn, out var n) && !string.IsNullOrEmpty(n) ? n : dn;

    public sealed class PathNode(string dn)
    {
        public string Dn { get; } = dn;
        public List<PathNode> Children { get; } = [];
    }

    /// <summary>
    /// Merges root-first nesting paths into a tree (shared prefixes collapse into one branch).
    /// Returns the children of the analyzed group; siblings are ordered by display name.
    /// </summary>
    public List<PathNode> BuildTree(IEnumerable<List<string>> paths)
    {
        var root = new PathNode(string.Empty);
        foreach (var path in paths)
        {
            var node = root;
            foreach (var dn in path.Skip(1))
            {
                var child = node.Children.Find(c => string.Equals(c.Dn, dn, StringComparison.OrdinalIgnoreCase));
                if (child == null) { child = new PathNode(dn); node.Children.Add(child); }
                node = child;
            }
        }
        Sort(root);
        return root.Children;

        void Sort(PathNode n)
        {
            n.Children.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(NameOf(a.Dn), NameOf(b.Dn)));
            foreach (var c in n.Children) Sort(c);
        }
    }

    public override async Task<IActionResult> OnGetAsync([FromQuery] bool cached = false)
    {
        var redirect = await InitializePageAsync();
        if (redirect != null) return redirect;

        // Block direct navigation for users lacking the Group Analyzer permission.
        if (!CanUseGroupAnalyzer)
            return Redirect($"{Request.PathBase}/app/");

        if (string.IsNullOrWhiteSpace(Query) && string.IsNullOrWhiteSpace(Dn))
            return Page();

        var cachedSummary = UserSummaryCache.GetSummaryModel(UserCacheKey);
        if (cachedSummary != null)
        {
            Summary = cachedSummary;
            await ReconcileCachedMembershipWithSupersetAsync(GetAccessToken()!);
        }
        else
        {
            await LoadFullSummaryAsync(GetAccessToken()!);
        }

        var edges = IsEntra ? BuildEntraGraph() : BuildAdGraph();

        if (!string.IsNullOrWhiteSpace(Dn))
        {
            if (!NameByDn.ContainsKey(Dn))
                NotFound = true;
            else
                await AnalyzeAsync(edges, NameByDn.Keys.First(k => k.Equals(Dn, StringComparison.OrdinalIgnoreCase)));
            return Page();
        }

        var q = Query!.Trim();
        var hits = NameByDn
            .Where(kv => kv.Value.Contains(q, StringComparison.OrdinalIgnoreCase) || kv.Key.Equals(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (hits.Count == 1)
        {
            Dn = hits[0].Key;
            await AnalyzeAsync(edges, Dn);
        }
        else
        {
            NotFound = hits.Count == 0;
            MatchesTruncated = hits.Count > MaxMatches;
            Matches.AddRange(hits.Take(MaxMatches).Select(kv => (kv.Key, kv.Value)));
        }
        return Page();
    }

    private async Task AnalyzeAsync(Dictionary<string, List<string>> edges, string groupDn)
    {
        Report = GroupPathExplorer.Analyze(edges, groupDn, MaxPaths);
        if (string.IsNullOrWhiteSpace(UserQuery)) return;

        UserDn = await ResolveUserAsync(UserQuery.Trim());
        if (UserDn == null) return;

        var direct = _userDirectGroups ?? new HashSet<string>(
            _rawMembers.Where(kv => kv.Value.Contains(UserDn, StringComparer.OrdinalIgnoreCase)).Select(kv => kv.Key),
            StringComparer.OrdinalIgnoreCase);
        direct.IntersectWith(NameByDn.Keys);
        UserPaths = GroupPathExplorer.PathsTo(edges, groupDn, direct, MaxPaths);
    }

    private HashSet<string>? _userDirectGroups;

    /// <summary>
    /// AD: resolves the user in the directory (DN, sAMAccountName, UPN or name) and reads its direct
    /// groups from <c>memberOf</c>, because the cached summary strips user members from groups.
    /// Only groups visible to the caller are kept, and the user must belong to at least one, so no
    /// user outside the caller's scope is revealed. Entra: matches loaded group members by DN or CN.
    /// </summary>
    private async Task<string?> ResolveUserAsync(string input)
    {
        if (!IsEntra)
        {
            if (GetAccessToken() is not { } token) { UserNotFound = true; return null; }
            var (dn, memberOf, ambiguous) = await ArService.ResolveUserMembershipAsync(token, input);
            if (ambiguous) { UserAmbiguous = true; return null; }
            var visible = memberOf.Where(NameByDn.ContainsKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (dn == null || visible.Count == 0) { UserNotFound = true; return null; }
            _userDirectGroups = visible;
            return dn;
        }

        var candidates = _rawMembers.Values.SelectMany(v => v)
            .Where(m => !NameByDn.ContainsKey(m))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(m => m.Equals(input, StringComparison.OrdinalIgnoreCase)
                || m.StartsWith($"CN={input},", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 1) return candidates[0];
        UserAmbiguous = candidates.Count > 1;
        UserNotFound = candidates.Count == 0;
        return null;
    }

    private Dictionary<string, List<string>> BuildAdGraph()
    {
        var groups = Summary.ADGroups;
        _rawMembers = new(StringComparer.OrdinalIgnoreCase);
        foreach (var item in groups.Items)
        {
            var dn = SegmentAttributes.AttrOf(item, "distinguishedName");
            if (string.IsNullOrWhiteSpace(dn)) continue;
            NameByDn[dn] = SegmentAttributes.AttrOf(item, "name");
            var members = SegmentAttributes.MultiAttrOf(item, "member").ToList();
            if (members.Count == 0 && groups.DirectByDn.TryGetValue(dn, out var direct)) members = direct;
            _rawMembers[dn] = members;
        }
        var visible = new HashSet<string>(NameByDn.Keys, StringComparer.OrdinalIgnoreCase);
        return GroupPathExplorer.Restrict(groups.GroupMembersByDn, visible);
    }

    private Dictionary<string, List<string>> BuildEntraGraph()
    {
        var entra = Summary.EntraTotals;
        EntraMembershipMissing = !entra.MembershipLoaded;
        var raw = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in entra.Items.Where(i => ActiveRolesService.IsEntraGroupType(i.ObjectType) && !string.IsNullOrWhiteSpace(i.Dn)))
        {
            NameByDn[g.Dn] = g.Name;
            raw.TryAdd(g.Dn, SegmentAttributes.MultiAttrOf(g.Raw, "member").ToList());
        }
        _rawMembers = raw;
        var visible = new HashSet<string>(NameByDn.Keys, StringComparer.OrdinalIgnoreCase);
        return GroupPathExplorer.Restrict(raw, visible);
    }
}
