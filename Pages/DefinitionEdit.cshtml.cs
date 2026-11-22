using System.Text.Json;
using System.Text.Json.Nodes;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.Definitions;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Services.Definitions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace ActiveRolesDashboard.Pages;

/// <summary>
/// Creates, edits and clones custom dashboard definitions (Active Roles administrators only).
/// Common and type-specific properties are edited through typed fields, which are applied to the
/// JSON document; every other property (charts, drilldown, enrichments, function arguments) is
/// edited directly in the JSON.
/// </summary>
[Authorize]
public class DefinitionEditModel(
    DefinitionAdminService admin,
    DirectoryFactsResolver directoryFacts,
    IStringLocalizer<DefinitionEditModel> localizer) : PageModel
{
    public static readonly string[] KpiKinds = ["Search", "Objects", "Derived", "Composite"];

    [BindProperty(SupportsGet = true)]
    public DefinitionKind Kind { get; set; } = DefinitionKind.Kpi;

    /// <summary>Id of the item being edited or cloned; null when adding.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Clone { get; set; }

    /// <summary>KPI kind for a new KPI (Search, Objects, Derived, Composite).</summary>
    [BindProperty(SupportsGet = true)]
    public string KpiKind { get; set; } = "Search";

    [BindProperty]
    public string Json { get; set; } = string.Empty;

    [BindProperty]
    public DefinitionFields Fields { get; set; } = new();

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public IReadOnlyList<string> CategoryIds { get; private set; } = [];
    public IReadOnlyList<string> DatasetIds { get; private set; } = [];
    public IReadOnlyList<string> KpiIds { get; private set; } = [];
    public IReadOnlyList<DashboardInfo> Dashboards => DashboardInfo.All;

    /// <summary>Editing an existing custom item (as opposed to adding or cloning).</summary>
    public bool IsEdit => Id is not null && !Clone;

    public string Title => localizer[IsEdit ? "Title_Edit" : Clone ? "Title_Clone" : "Title_Add", Kind.ToString()];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await IsAdminAsync()) return Forbid();

        string? json;
        if (Id is null)
            json = Template(Kind, KpiKind);
        else
        {
            json = admin.GetJson(Kind, Id, system: Clone);
            if (json is null) return NotFound();
        }

        Json = json;
        Fields = DefinitionFields.From(JsonNode.Parse(json)!.AsObject());
        KpiKind = Fields.KpiKind ?? KpiKind;
        LoadLists();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await IsAdminAsync()) return Forbid();

        JsonObject doc;
        try
        {
            doc = JsonNode.Parse(Json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })?.AsObject()
                  ?? throw new JsonException("Definition is empty.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            Errors = [localizer["Error_Json", ex.Message]];
            LoadLists();
            return Page();
        }

        Fields.ApplyTo(doc, Kind);
        Json = doc.ToJsonString(DefinitionJson.Options);

        Errors = admin.Save(Kind, Json, IsEdit ? Id : null);
        if (Errors.Count > 0)
        {
            LoadLists();
            return Page();
        }

        TempData["DefinitionMessage"] = localizer["Msg_Saved", Fields.Id].Value;
        return RedirectToPage("/Settings", null, "definitions");
    }

    private void LoadLists()
    {
        CategoryIds = admin.CategoryIds();
        DatasetIds = admin.DatasetIds();
        KpiIds = admin.KpiIds();
    }

    private async Task<bool> IsAdminAsync()
    {
        var cached = HttpContext.Session.GetString("IsActiveRolesAdmin");
        if (HttpContext.Session.GetString("DirectoryFactsEpoch") == directoryFacts.CurrentEpoch.ToString() && cached is not null)
            return bool.TryParse(cached, out var v) && v;

        var token = HttpContext.Session.GetString("AccessToken");
        if (string.IsNullOrEmpty(token)) return false;
        var facts = await directoryFacts.ResolveAsync(token, User.Identity?.Name ?? "");
        HttpContext.Session.SetString("IsActiveRolesAdmin", facts.IsActiveRolesAdmin.ToString());
        HttpContext.Session.SetString("DashboardRole", facts.Role.ToString());
        HttpContext.Session.SetString("DirectoryFactsEpoch", facts.Epoch.ToString());
        return facts.IsActiveRolesAdmin;
    }

    /// <summary>Starting JSON for a new item.</summary>
    public static string Template(DefinitionKind kind, string kpiKind)
    {
        JsonObject doc = kind switch
        {
            DefinitionKind.Category => new() { ["id"] = "Custom_Category", ["dashboardId"] = DashboardInfo.ActiveDirectory.Key, ["fallbackText"] = "Custom category" },
            DefinitionKind.Dataset => new()
            {
                ["id"] = "Custom_Dataset",
                ["dataSource"] = "ActiveDirectory",
                ["retrieval"] = new JsonObject { ["base"] = "{DefaultADDN}", ["filter"] = "(objectClass=user)" },
                ["attributes"] = new JsonArray("name", "distinguishedName"),
            },
            _ => kpiKind switch
            {
                "Composite" => new() { ["kind"] = "Composite", ["id"] = "Custom_Kpi", ["categoryId"] = "", ["dataSource"] = "ActiveDirectory", ["fallbackText"] = "Custom KPI", ["sources"] = new JsonArray(), ["aggregate"] = "Sum" },
                "Derived" => new() { ["kind"] = "Derived", ["id"] = "Custom_Kpi", ["categoryId"] = "", ["dataSource"] = "ActiveDirectory", ["fallbackText"] = "Custom KPI", ["sourceDatasetId"] = "", ["evaluation"] = new JsonObject { ["filter"] = "" } },
                "Objects" => new() { ["kind"] = "Objects", ["id"] = "Custom_Kpi", ["categoryId"] = "", ["dataSource"] = "ActiveRoles", ["fallbackText"] = "Custom KPI", ["retrieval"] = new JsonObject { ["target"] = "Objects", ["objects"] = new JsonArray() } },
                _ => new()
                {
                    ["kind"] = "Search",
                    ["id"] = "Custom_Kpi",
                    ["categoryId"] = "",
                    ["dataSource"] = "ActiveDirectory",
                    ["fallbackText"] = "Custom KPI",
                    ["retrieval"] = new JsonObject { ["base"] = "{DefaultADDN}", ["filter"] = "(objectClass=user)" },
                    ["evaluation"] = new JsonObject { ["filter"] = "" },
                    ["drilldown"] = new JsonObject { ["columns"] = new JsonArray(new JsonObject { ["attribute"] = "name" }) },
                },
            }
        };
        return doc.ToJsonString(DefinitionJson.Options);
    }
}

/// <summary>Typed fields applied on top of the JSON document. Blank values remove the property.</summary>
public sealed class DefinitionFields
{
    public string Id { get; set; } = string.Empty;
    public string? FallbackText { get; set; }
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }

    // Category
    public string? DashboardId { get; set; }
    public string? CategoryKind { get; set; }
    public string? KpiSortOrder { get; set; }

    // Dataset and KPI
    public string? DataSource { get; set; }
    public string? RetrievalBase { get; set; }
    public string? RetrievalFilter { get; set; }
    public string? RetrievalScope { get; set; }
    public string? RetrievalObjects { get; set; }
    public string? Attributes { get; set; }

    // KPI
    public string? KpiKind { get; set; }
    public string? CategoryId { get; set; }
    public string? Severity { get; set; }
    public bool IsRiskKpi { get; set; }
    public string? Color { get; set; }
    public string? TileText { get; set; }
    public string? DatasetId { get; set; }
    public string? SourceDatasetId { get; set; }
    public string? SourceKpiId { get; set; }
    public string? EvaluationType { get; set; }
    public string? EvaluationFilter { get; set; }
    public string? EvaluationAggregate { get; set; }
    public string? EvaluationAggregateAttribute { get; set; }
    public string? EvaluationFunction { get; set; }
    public string? Sources { get; set; }
    public string? CompositeAggregate { get; set; }

    public static DefinitionFields From(JsonObject o) => new()
    {
        Id = Str(o, "id") ?? string.Empty,
        FallbackText = Str(o, "fallbackText"),
        Enabled = o["enabled"]?.GetValue<bool>() ?? true,
        SortOrder = o["sortOrder"]?.GetValue<int>() ?? 0,
        DashboardId = Str(o, "dashboardId"),
        CategoryKind = Str(o, "kind") is { } k && !EditKinds.Contains(k) ? k : null,
        KpiSortOrder = Str(o, "kpiSortOrder"),
        DataSource = Str(o, "dataSource"),
        RetrievalBase = Str(o["retrieval"], "base"),
        RetrievalFilter = Str(o["retrieval"], "filter"),
        RetrievalScope = Str(o["retrieval"], "scope"),
        RetrievalObjects = List(o["retrieval"]?["objects"], "\n"),
        Attributes = List(o["attributes"]),
        KpiKind = Str(o, "kind") is { } kk && EditKinds.Contains(kk) ? kk : null,
        CategoryId = Str(o, "categoryId"),
        Severity = Str(o, "severity"),
        IsRiskKpi = o["isRiskKpi"]?.GetValue<bool>() ?? false,
        Color = Str(o["display"], "color"),
        TileText = Str(o["display"], "tileFallbackText"),
        DatasetId = Str(o, "datasetId"),
        SourceDatasetId = Str(o, "sourceDatasetId"),
        SourceKpiId = Str(o, "sourceKpiId"),
        EvaluationType = Str(o["evaluation"], "type"),
        EvaluationFilter = Str(o["evaluation"], "filter"),
        EvaluationAggregate = Str(o["evaluation"], "aggregate"),
        EvaluationAggregateAttribute = Str(o["evaluation"], "aggregateAttribute"),
        EvaluationFunction = Str(o["evaluation"], "function"),
        Sources = List(o["sources"]),
        CompositeAggregate = Str(o, "aggregate"),
    };

    private static readonly HashSet<string> EditKinds = new(DefinitionEditModel.KpiKinds, StringComparer.OrdinalIgnoreCase);

    public void ApplyTo(JsonObject o, DefinitionKind kind)
    {
        Set(o, "id", Id.Trim());
        if (kind != DefinitionKind.Dataset)
        {
            Set(o, "fallbackText", FallbackText);
            o["enabled"] = Enabled;
            if (SortOrder == 0) o.Remove("sortOrder"); else o["sortOrder"] = SortOrder;
        }

        switch (kind)
        {
            case DefinitionKind.Category:
                Set(o, "dashboardId", DashboardId);
                Set(o, "kind", CategoryKind);
                Set(o, "kpiSortOrder", KpiSortOrder);
                break;

            case DefinitionKind.Dataset:
                Set(o, "dataSource", DataSource);
                ApplyRetrieval(o, force: true);
                SetList(o, "attributes", Attributes);
                break;

            default:
                Set(o, "categoryId", CategoryId);
                Set(o, "dataSource", DataSource);
                Set(o, "severity", Severity);
                if (IsRiskKpi) o["isRiskKpi"] = true; else o.Remove("isRiskKpi");
                var display = Child(o, "display");
                Set(display, "color", Color);
                Set(display, "tileFallbackText", TileText);
                Prune(o, "display");
                SetList(o, "attributes", Attributes);

                switch (KpiKind ?? Str(o, "kind"))
                {
                    case "Search":
                    case "Objects":
                        Set(o, "datasetId", DatasetId);
                        if (string.IsNullOrWhiteSpace(DatasetId)) ApplyRetrieval(o, force: false);
                        else o.Remove("retrieval");
                        ApplyEvaluation(o);
                        break;
                    case "Derived":
                        Set(o, "sourceDatasetId", SourceDatasetId);
                        Set(o, "sourceKpiId", SourceKpiId);
                        ApplyEvaluation(o);
                        break;
                    case "Composite":
                        SetList(o, "sources", Sources);
                        Set(o, "aggregate", CompositeAggregate);
                        break;
                }
                break;
        }
    }

    private void ApplyRetrieval(JsonObject o, bool force)
    {
        var r = Child(o, "retrieval");
        Set(r, "base", RetrievalBase);
        Set(r, "filter", RetrievalFilter);
        Set(r, "scope", RetrievalScope);
        SetList(r, "objects", RetrievalObjects, lines: true);
        if (!force) Prune(o, "retrieval");
    }

    private void ApplyEvaluation(JsonObject o)
    {
        var e = Child(o, "evaluation");
        Set(e, "type", EvaluationType);
        Set(e, "filter", EvaluationFilter);
        Set(e, "aggregate", EvaluationAggregate);
        Set(e, "aggregateAttribute", EvaluationAggregateAttribute);
        Set(e, "function", EvaluationFunction);
        Prune(o, "evaluation");
    }

    private static JsonObject Child(JsonObject o, string name)
    {
        if (o[name] is JsonObject c) return c;
        var created = new JsonObject();
        o[name] = created;
        return created;
    }

    private static void Prune(JsonObject o, string name)
    {
        if (o[name] is JsonObject { Count: 0 }) o.Remove(name);
    }

    private static void Set(JsonObject o, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) o.Remove(name);
        else o[name] = value.Trim();
    }

    private static void SetList(JsonObject o, string name, string? csv, bool lines = false)
    {
        var items = (csv ?? string.Empty)
            .Split(lines ? ['\n'] : ['\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (items.Length == 0) o.Remove(name);
        else o[name] = new JsonArray([.. items.Select(i => (JsonNode)JsonValue.Create(i))]);
    }

    private static string? Str(JsonNode? node, string name) =>
        node is JsonObject o && o[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static string? List(JsonNode? node, string separator = ", ") =>
        node is JsonArray a ? string.Join(separator, a.Select(x => x?.ToString())) : null;
}
