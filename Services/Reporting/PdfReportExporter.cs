using ActiveRolesDashboard.Models.Reporting;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ActiveRolesDashboard.Services.Reporting;

/// <summary>
/// Renders a <see cref="ReportModel"/> to a styled PDF using QuestPDF.
/// Produces a One Identity header on every page, colored KPI tiles that mirror the
/// dashboard, native chart blocks, and optional detail tables (data columns only).
/// </summary>
public class PdfReportExporter : IReportExporter
{
    private readonly string _logoSvgPath;

    public PdfReportExporter(IWebHostEnvironment env)
    {
        _logoSvgPath = Path.Combine(env.WebRootPath ?? "wwwroot", "images", "oneidentity-logo-v2.svg");
    }

    public ReportFormat Format => ReportFormat.Pdf;
    public string ContentType => "application/pdf";
    public string FileExtension => ".pdf";

    // Brand palette approximating the dashboard's CssColor classes.
    private static readonly Dictionary<string, string> ColorMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["blue"] = "#2563eb",
        ["green"] = "#16a34a",
        ["teal"] = "#0d9488",
        ["red"] = "#dc2626",
        ["orange"] = "#ea580c",
        ["amber"] = "#d97706",
        ["purple"] = "#7c3aed",
        ["pink"] = "#db2777",
        ["slate"] = "#475569",
        ["indigo"] = "#4f46e5"
    };

    private const string Brand = "#e21a23"; // One Identity red
    private const string Ink = "#1f2937";
    private const string Muted = "#6b7280";
    private const string Line = "#e5e7eb";

    private static string Hex(string cssColor) =>
        ColorMap.TryGetValue(cssColor ?? string.Empty, out var hex) ? hex : "#475569";

    public byte[] Export(ReportModel model)
    {
        var svg = TryReadLogoSvg();

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(t => t.FontSize(10).FontColor(Ink));

                page.Header().Element(h => ComposeHeader(h, model, svg));
                page.Content().Element(c => ComposeContent(c, model));
                page.Footer().Element(ComposeFooter);
            });
        });

        return document.GeneratePdf();
    }

    private string? TryReadLogoSvg()
    {
        try
        {
            return File.Exists(_logoSvgPath) ? File.ReadAllText(_logoSvgPath) : null;
        }
        catch
        {
            return null;
        }
    }

    private void ComposeHeader(IContainer container, ReportModel model, string? svg)
    {
        container.BorderBottom(2).BorderColor(Brand).PaddingBottom(8).Row(row =>
        {
            if (svg != null)
            {
                row.ConstantItem(120).AlignMiddle().Svg(svg).FitArea();
            }

            row.RelativeItem().Column(col =>
            {
                col.Item().AlignRight().Text(model.Title).FontSize(18).SemiBold().FontColor(Ink);
                if (!string.IsNullOrWhiteSpace(model.Subtitle))
                    col.Item().AlignRight().Text(model.Subtitle).FontSize(11).FontColor(Muted);
                col.Item().AlignRight().Text($"Generated {model.GeneratedUtc.ToLocalTime():yyyy-MM-dd HH:mm}")
                    .FontSize(8).FontColor(Muted);
            });
        });
    }

    private void ComposeContent(IContainer container, ReportModel model)
    {
        container.PaddingVertical(10).Column(col =>
        {
            col.Spacing(16);

            if (model.Sections.Count == 0)
            {
                col.Item().Text("No data available for the selected scope.").FontColor(Muted).Italic();
                return;
            }

            foreach (var section in model.Sections)
            {
                col.Item().Element(e => ComposeSection(e, section, model.IncludeDetails));
            }
        });
    }

    private void ComposeSection(IContainer container, ReportSection section, bool includeDetails)
    {
        container.Column(col =>
        {
            col.Spacing(10);

            col.Item().Text(section.Heading).FontSize(14).SemiBold().FontColor(Ink);

            if (section.Tiles.Count > 0)
                col.Item().Element(e => ComposeTiles(e, section.Tiles));

            foreach (var chart in section.Charts)
                col.Item().Element(e => ComposeChart(e, chart));

            if (includeDetails)
            {
                foreach (var table in section.Tables)
                    col.Item().Element(e => ComposeTable(e, table));
            }
        });
    }

    private void ComposeTiles(IContainer container, List<ReportTile> tiles)
    {
        // 4 tiles per row grid mirroring the dashboard cards.
        const int perRow = 4;
        container.Column(col =>
        {
            col.Spacing(6);
            for (var i = 0; i < tiles.Count; i += perRow)
            {
                var rowTiles = tiles.Skip(i).Take(perRow).ToList();
                col.Item().Row(row =>
                {
                    row.Spacing(6);
                    foreach (var tile in rowTiles)
                    {
                        row.RelativeItem().Border(1).BorderColor(Line).Background("#ffffff")
                            .Column(tc =>
                            {
                                tc.Item().Height(4).Background(Hex(tile.CssColor));
                                tc.Item().Padding(8).Column(inner =>
                                {
                                    if (tile.Error != null)
                                        inner.Item().Text("—").FontSize(20).SemiBold().FontColor(Muted);
                                    else
                                        inner.Item().Text(tile.Value.ToString("N0")).FontSize(20).SemiBold().FontColor(Hex(tile.CssColor));
                                    inner.Item().Text(tile.Label).FontSize(9).FontColor(Muted);
                                });
                            });
                    }

                    // Pad the final row so tiles keep a consistent width.
                    for (var p = rowTiles.Count; p < perRow; p++)
                        row.RelativeItem().Element(x => x);
                });
            }
        });
    }

    private void ComposeChart(IContainer container, ReportChart chart)
    {
        var max = chart.Values.Count > 0 ? Math.Max(1, chart.Values.Max(v => v.Value)) : 1;
        var total = chart.Values.Sum(v => (long)v.Value);

        container.Column(col =>
        {
            col.Spacing(4);
            col.Item().Text(chart.Title).FontSize(11).SemiBold().FontColor(Ink);

            foreach (var v in chart.Values)
            {
                var fraction = (float)v.Value / max;
                var share = total > 0 ? (double)v.Value / total : 0d;
                col.Item().Row(row =>
                {
                    row.ConstantItem(150).Text(v.Label).FontSize(9).FontColor(Muted);
                    row.RelativeItem().Height(12).Background("#f3f4f6").Row(bar =>
                    {
                        var pct = Math.Clamp(fraction, 0f, 1f);
                        if (pct > 0)
                            bar.RelativeItem(pct).Background(Hex(v.CssColor));
                        if (pct < 1)
                            bar.RelativeItem(1 - pct);
                    });
                    row.ConstantItem(50).AlignRight().Text(v.Value.ToString("N0")).FontSize(9).SemiBold();
                    row.ConstantItem(42).AlignRight().Text($"{share:P0}").FontSize(9).FontColor(Muted);
                });
            }
        });
    }

    private void ComposeTable(IContainer container, ReportTable table)
    {
        container.Column(col =>
        {
            col.Spacing(4);
            col.Item().Text(table.Title).FontSize(11).SemiBold().FontColor(Ink);

            if (table.Error != null)
            {
                col.Item().Text(table.Error).FontSize(9).FontColor(Muted).Italic();
                return;
            }

            if (table.Rows.Count == 0)
            {
                col.Item().Text("No items.").FontSize(9).FontColor(Muted).Italic();
                return;
            }

            col.Item().Table(t =>
            {
                t.ColumnsDefinition(cd =>
                {
                    foreach (var _ in table.Columns)
                        cd.RelativeColumn();
                });

                t.Header(header =>
                {
                    foreach (var c in table.Columns)
                    {
                        header.Cell().Background("#f9fafb").BorderBottom(1).BorderColor(Line)
                            .Padding(4).Text(c).FontSize(9).SemiBold().FontColor(Ink);
                    }
                });

                foreach (var rowData in table.Rows)
                {
                    for (var i = 0; i < table.Columns.Count; i++)
                    {
                        var value = i < rowData.Count ? rowData[i] : string.Empty;
                        var cell = t.Cell().BorderBottom(1).BorderColor(Line).Padding(4)
                            .Text(value).FontSize(8).FontColor(Ink);
                        if (i == 0)
                            cell.SemiBold();
                    }
                }
            });
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.BorderTop(1).BorderColor(Line).PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text("One Identity Active Roles Dashboard").FontSize(8).FontColor(Muted);
            row.RelativeItem().AlignRight().Text(text =>
            {
                text.DefaultTextStyle(s => s.FontSize(8).FontColor(Muted));
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });
        });
    }
}
