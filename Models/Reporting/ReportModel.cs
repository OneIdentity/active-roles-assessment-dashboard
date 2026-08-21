namespace ActiveRolesDashboard.Models.Reporting;

/// <summary>The scope of an export request.</summary>
public enum ReportScope
{
    Dashboard,
    SubDashboard,
    Category,
    Kpi
}

/// <summary>The output format of an export. PDF is the initial implementation; Excel/Word may follow.</summary>
public enum ReportFormat
{
    Pdf,
    Excel,
    Word
}

/// <summary>
/// Describes an export request coming from the UI: what to export, in which format,
/// and whether to include the detail tables.
/// </summary>
public class ReportRequest
{
    public ReportScope Scope { get; set; } = ReportScope.Dashboard;
    public ReportFormat Format { get; set; } = ReportFormat.Pdf;
    public string DashboardKey { get; set; } = string.Empty;
    public string? SubDashboardKey { get; set; }
    public string? CategoryKey { get; set; }
    public string? KpiKey { get; set; }
    public bool IncludeDetails { get; set; } = true;
}

/// <summary>
/// Format-neutral description of a report. Built once by <c>ReportBuilder</c> and
/// consumed by any <c>IReportExporter</c>, guaranteeing consistency across scopes and formats.
/// </summary>
public class ReportModel
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    public string GeneratedBy { get; set; } = string.Empty;
    public bool IncludeDetails { get; set; } = true;
    public List<ReportSection> Sections { get; set; } = new();
}

/// <summary>A single section of a report (typically one category, or a single KPI).</summary>
public class ReportSection
{
    public string Heading { get; set; } = string.Empty;
    public List<ReportTile> Tiles { get; set; } = new();
    public List<ReportChart> Charts { get; set; } = new();
    public List<ReportTable> Tables { get; set; } = new();
}

/// <summary>A KPI summary tile: a colored number card mirroring the dashboard.</summary>
public class ReportTile
{
    public string Label { get; set; } = string.Empty;
    public int Value { get; set; }
    public string? Error { get; set; }
    /// <summary>Named color from the dashboard palette (e.g. "blue", "red"); maps to a hex in the exporter.</summary>
    public string CssColor { get; set; } = string.Empty;
}

/// <summary>A chart rendered natively (labelled values), not a Chart.js image.</summary>
public class ReportChart
{
    public string Title { get; set; } = string.Empty;
    public List<ReportChartValue> Values { get; set; } = new();
}

public class ReportChartValue
{
    public string Label { get; set; } = string.Empty;
    public int Value { get; set; }
    public string CssColor { get; set; } = string.Empty;
}

/// <summary>A detail table. Action/button columns are intentionally excluded upstream.</summary>
public class ReportTable
{
    public string Title { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = new();
    public List<IReadOnlyList<string>> Rows { get; set; } = new();
    public string? Error { get; set; }
}
