using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.Reporting;

namespace ActiveRolesDashboard.Services.Reporting;

/// <summary>
/// Builds a format-neutral <see cref="ReportModel"/> from a computed <see cref="DashboardSummary"/>
/// for dashboard, category, or KPI scope. Reuses the dashboard metadata (categories, KPIs, charts)
/// and settings so exports match what is shown on screen. No additional data queries are performed.
/// </summary>
public class ReportBuilder
{
    public ReportModel Build(ReportRequest request, DashboardSummary summary, KpiSettings settings, string generatedBy)
    {
        var dashboard = DashboardInfo.All.FirstOrDefault(d => d.Key == request.DashboardKey);
        var model = new ReportModel
        {
            GeneratedUtc = DateTime.UtcNow,
            GeneratedBy = generatedBy,
            IncludeDetails = request.IncludeDetails
        };

        switch (request.Scope)
        {
            case ReportScope.Kpi:
                BuildKpiScope(request, summary, settings, dashboard, model);
                break;
            case ReportScope.Category:
                BuildCategoryScope(request, summary, settings, dashboard, model);
                break;
            case ReportScope.SubDashboard:
                // Export a single child dashboard chosen from the aggregate main dashboard.
                var subKey = request.SubDashboardKey ?? string.Empty;
                var subDashboard = DashboardInfo.All.FirstOrDefault(d => d.Key == subKey);
                BuildDashboardScope(summary, settings, subDashboard, subKey, model);
                break;
            default:
                BuildDashboardScope(summary, settings, dashboard, request.DashboardKey, model);
                break;
        }

        return model;
    }

    private static void BuildDashboardScope(DashboardSummary summary, KpiSettings settings, DashboardInfo? dashboard, string dashboardKey, ReportModel model)
    {
        var isMain = dashboardKey == "Main";
        model.Title = isMain ? "Dashboard" : (dashboard?.Title ?? "Dashboard");
        model.Subtitle = isMain ? "Overview" : (dashboard?.Subtitle ?? string.Empty);

        // Honour the active segment selection: when a source resolves to no selected
        // segments, omit the whole dashboard section from the export.
        if (dashboardKey == DashboardInfo.ActiveDirectory.Key && !summary.AdVisible) return;
        if (dashboardKey == DashboardInfo.EntraId.Key && !summary.EntraVisible) return;
        if (dashboardKey == DashboardInfo.Licensing.Key && !summary.LicensingVisible) return;

        // The main dashboard is an aggregate hub: export its own Overview plus the
        // Active Directory and Entra ID dashboards. Other dashboards export only their own
        // categories.
        var categories = dashboard != null && !isMain
            ? CategoryInfo.ForDashboard(dashboard)
            : CategoryInfo.ForExport(dashboardKey);

        foreach (var category in categories)
        {
            // Respect segment visibility for the aggregate export: skip AD/Entra
            // categories when their source resolves to no selected segments.
            if (category.DashboardKey == DashboardInfo.ActiveDirectory.Key && !summary.AdVisible) continue;
            if (category.DashboardKey == DashboardInfo.EntraId.Key && !summary.EntraVisible) continue;
            if (category.DashboardKey == DashboardInfo.Licensing.Key && !summary.LicensingVisible) continue;

            if (!settings.IsCategoryEnabled(category.Key)) continue;
            var section = BuildCategorySection(category, summary, settings, model.IncludeDetails);
            if (section.Tiles.Count > 0 || section.Charts.Count > 0 || section.Tables.Count > 0)
                model.Sections.Add(section);
        }
    }

    private static void BuildCategoryScope(ReportRequest request, DashboardSummary summary, KpiSettings settings, DashboardInfo? dashboard, ReportModel model)
    {
        var category = CategoryInfo.All.FirstOrDefault(c => c.Key == request.CategoryKey);

        // When exporting from the aggregate main dashboard the request dashboard has no
        // matching DashboardInfo; resolve the owning dashboard from the category instead.
        var owningDashboard = category != null
            ? DashboardInfo.All.FirstOrDefault(d => d.Key == category.DashboardKey) ?? dashboard
            : dashboard;

        model.Title = owningDashboard?.Title ?? "Category";
        model.Subtitle = category?.DisplayName ?? string.Empty;

        if (category == null || !settings.IsCategoryEnabled(category.Key)) return;

        model.Sections.Add(BuildCategorySection(category, summary, settings, model.IncludeDetails));
    }

    private static void BuildKpiScope(ReportRequest request, DashboardSummary summary, KpiSettings settings, DashboardInfo? dashboard, ReportModel model)
    {
        var kpi = KpiInfo.All.FirstOrDefault(k => k.Key == request.KpiKey);

        // Resolve the owning dashboard from the KPI's category so aggregate main-dashboard
        // exports still carry a meaningful subtitle.
        var kpiCategory = kpi != null ? CategoryInfo.All.FirstOrDefault(c => KpiInfo.ForCategory(c).Any(k => k.Key == kpi.Key)) : null;
        var owningDashboard = kpiCategory != null
            ? DashboardInfo.All.FirstOrDefault(d => d.Key == kpiCategory.DashboardKey) ?? dashboard
            : dashboard;

        model.Title = kpi?.DisplayName ?? "KPI";
        model.Subtitle = owningDashboard?.Title ?? string.Empty;

        if (kpi == null) return;

        var section = new ReportSection { Heading = kpi.DisplayName };

        var (count, error) = summary.GetKpiResult(kpi.Key);
        section.Tiles.Add(new ReportTile { Label = kpi.Label, Value = count, Error = error, CssColor = kpi.CssColor });

        // Include any chart associated with this KPI (e.g. an Overview summary KPI's
        // AD/Entra source split, or a category chart that plots this KPI as a series).
        foreach (var reportChart in BuildChartsForKpi(kpi, summary))
            section.Charts.Add(reportChart);

        if (model.IncludeDetails)
        {
            var table = summary.GetKpiDetailTable(kpi.Key);
            if (table != null)
            {
                table.Title = kpi.DisplayName;
                section.Tables.Add(table);
            }
        }

        model.Sections.Add(section);
    }

    /// <summary>
    /// Builds the report chart(s) that belong to a single KPI: any category chart whose
    /// source-split key matches the KPI or that plots the KPI as one of its series, plus the
    /// special Managed Objects breakdown which is derived from its own time-series data.
    /// </summary>
    private static IEnumerable<ReportChart> BuildChartsForKpi(KpiInfo kpi, DashboardSummary summary)
    {
        // Managed Objects renders a time-series chart from its own data points; represent it
        // in the report as the latest snapshot's counts per object type.
        if (kpi.Key == "ManagedObjects")
        {
            var latest = summary.ManagedObjects.DataPoints.LastOrDefault();
            if (latest != null && latest.Items.Count > 0)
            {
                var chart = new ReportChart { Title = kpi.DisplayName };
                var palette = new[] { "blue", "green", "amber", "red", "purple", "teal", "pink", "slate", "orange" };
                var i = 0;
                foreach (var item in latest.Items)
                {
                    var label = string.IsNullOrEmpty(item.Category) ? item.DisplayName : $"{item.Category}: {item.DisplayName}";
                    chart.Values.Add(new ReportChartValue
                    {
                        Label = label,
                        Value = item.Count,
                        CssColor = palette[i++ % palette.Length]
                    });
                }
                yield return chart;
            }
            yield break;
        }

        foreach (var chart in ChartInfo.All)
        {
            var matches = chart.SourceSplitKpiKey == kpi.Key
                || chart.Series.Any(s => s.KpiKey == kpi.Key);
            if (!matches) continue;

            var reportChart = BuildReportChart(chart, summary);
            if (reportChart.Values.Count > 0)
                yield return reportChart;
        }
    }

    private static ReportSection BuildCategorySection(CategoryInfo category, DashboardSummary summary, KpiSettings settings, bool includeDetails)
    {
        var section = new ReportSection { Heading = category.DisplayName };

        var kpis = KpiInfo.ForCategory(category)
            .Where(k => settings.IsKpiEnabled(category.Key, k.Key))
            .ToList();

        // Tiles (in the category's KPI order).
        foreach (var kpi in kpis)
        {
            var (count, error) = summary.GetKpiResult(kpi.Key);
            section.Tiles.Add(new ReportTile { Label = kpi.Label, Value = count, Error = error, CssColor = kpi.CssColor });
        }

        // Charts declared for this category.
        foreach (var chart in ChartInfo.ForCategory(category))
        {
            var reportChart = BuildReportChart(chart, summary);
            if (reportChart.Values.Count > 0)
                section.Charts.Add(reportChart);
        }

        // Detail tables (optional), following KPI order.
        if (includeDetails)
        {
            foreach (var kpi in kpis)
            {
                var table = summary.GetKpiDetailTable(kpi.Key);
                if (table == null) continue;
                table.Title = kpi.DisplayName;
                section.Tables.Add(table);
            }
        }

        return section;
    }

    /// <summary>
    /// Builds a <see cref="ReportChart"/> from a <see cref="ChartInfo"/>, using either the
    /// AD/Entra source split or the chart's KPI series.
    /// </summary>
    private static ReportChart BuildReportChart(ChartInfo chart, DashboardSummary summary)
    {
        var reportChart = new ReportChart { Title = chart.Title };

        if (!string.IsNullOrEmpty(chart.SourceSplitKpiKey))
        {
            var split = summary.GetSourceSplit(chart.SourceSplitKpiKey);
            for (var i = 0; i < split.Count; i++)
            {
                reportChart.Values.Add(new ReportChartValue
                {
                    Label = split[i].Source,
                    Value = split[i].Count,
                    CssColor = i < ChartInfo.SourceSplitColors.Count ? ChartInfo.SourceSplitColors[i] : "slate"
                });
            }
        }
        else
        {
            foreach (var s in chart.Series)
            {
                var kpi = KpiInfo.All.FirstOrDefault(k => k.Key == s.KpiKey);
                var (count, _) = summary.GetKpiResult(s.KpiKey);
                reportChart.Values.Add(new ReportChartValue
                {
                    Label = !string.IsNullOrEmpty(s.Label) ? s.Label : kpi?.Label ?? s.KpiKey,
                    Value = count,
                    CssColor = !string.IsNullOrEmpty(s.CssColor) ? s.CssColor : kpi?.CssColor ?? "slate"
                });
            }
        }

        return reportChart;
    }
}
