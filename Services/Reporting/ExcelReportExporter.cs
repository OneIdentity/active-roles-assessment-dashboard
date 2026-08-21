using System.Globalization;
using ActiveRolesDashboard.Models.Reporting;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ActiveRolesDashboard.Services.Reporting;

/// <summary>
/// Renders a <see cref="ReportModel"/> to a Microsoft Excel (.xlsx) workbook using the
/// Open XML SDK. Mirrors the content of <see cref="PdfReportExporter"/>: a branded title,
/// KPI summaries, chart data, and detail tables. Data elements are emitted as native Excel
/// Tables (with a table style, banded rows, and filter buttons) so the output is filterable
/// and visually polished. The first data column is bold to match the on-screen/PDF/Word styling.
/// </summary>
public class ExcelReportExporter : IReportExporter
{
    public ReportFormat Format => ReportFormat.Excel;
    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string FileExtension => ".xlsx";

    // Cell style indices into the stylesheet built in BuildStylesheet().
    // Colors mirror the dashboard header (dark navy) for brand consistency.
    private const uint StyleDefault = 0;      // plain text
    private const uint StyleTitle = 1;        // brand-red bold 16
    private const uint StyleHeading = 2;      // dark bold 12 section heading
    private const uint StyleBoldCell = 3;     // bold text (no fill)
    private const uint StyleMuted = 4;        // gray text
    private const uint StyleHeaderCell = 5;   // navy fill, white bold text (table header)
    private const uint StyleNumber = 6;       // plain number
    private const uint StyleStripe = 7;       // light banding fill, text
    private const uint StyleStripeBold = 8;   // light banding fill, bold text
    private const uint StyleNumberStripe = 9; // light banding fill, number

    // Excel is unreliable at honoring custom TableStyles defined in the file, so colors are
    // painted directly onto cells; the native Table is kept only for filter dropdowns.
    private const string TableStyleName = "TableStyleLight1";

    /// <summary>Mutable state threaded through the compose methods while writing one sheet.</summary>
    private sealed class SheetContext
    {
        public SheetData SheetData { get; } = new();
        public uint Row { get; set; } = 1;
        public List<TableSpec> Tables { get; } = new();
        /// <summary>Shared across all sheets so native table ids are unique workbook-wide.</summary>
        public TableIdCounter TableIds { get; init; } = new();
        /// <summary>Internal navigation links to add to this sheet, keyed by cell reference.</summary>
        public List<SheetHyperlink> Hyperlinks { get; } = new();
    }

    /// <summary>A monotonic counter shared by every sheet to keep native table ids unique.</summary>
    private sealed class TableIdCounter
    {
        public uint Next { get; set; } = 1;
    }

    /// <summary>An internal hyperlink from a cell to another sheet.</summary>
    private sealed record SheetHyperlink(string CellReference, string TargetSheet);

    /// <summary>A pending native Excel Table to attach to the worksheet after data is written.</summary>
    private sealed record TableSpec(uint Id, string Name, string Reference, IReadOnlyList<string> Columns);

    public byte[] Export(ReportModel model)
    {
        using var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = BuildStylesheet();
            stylesPart.Stylesheet.Save();

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            uint sheetId = 1;

            // Shared workbook-wide native-table id counter.
            var tableIds = new TableIdCounter();

            // Reserve unique, sanitized sheet names up front so the Summary tab can
            // hyperlink to the detail tabs by name.
            var usedSheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Map (section, table) -> generated sheet name for detail tabs.
            var detailNames = new Dictionary<(ReportSection, ReportTable), string>();
            if (model.IncludeDetails)
            {
                foreach (var section in model.Sections)
                    foreach (var table in section.Tables)
                        detailNames[(section, table)] =
                            MakeSheetName(section.Heading, table.Title, usedSheetNames);
            }

            // ---- Summary sheet -------------------------------------------------
            var summaryPart = workbookPart.AddNewPart<WorksheetPart>();
            var summaryCtx = new SheetContext { TableIds = tableIds };

            AddRow(summaryCtx, new[] { Cell(model.Title, StyleTitle) });
            if (!string.IsNullOrWhiteSpace(model.Subtitle))
                AddRow(summaryCtx, new[] { Cell(model.Subtitle, StyleDefault) });
            AddRow(summaryCtx, new[]
            {
                Cell($"Generated {model.GeneratedUtc.ToLocalTime():yyyy-MM-dd HH:mm}", StyleMuted)
            });
            summaryCtx.Row++; // blank spacer row

            foreach (var section in model.Sections)
            {
                if (!string.IsNullOrWhiteSpace(section.Heading))
                    AddRow(summaryCtx, new[] { Cell(section.Heading, StyleHeading) });

                // KPI tiles only on the Summary sheet; link each label to its detail tab.
                ComposeTiles(summaryCtx, section.Heading, section.Tiles, tile =>
                {
                    var match = section.Tables.FirstOrDefault(t =>
                        string.Equals(t.Title, tile.Label, StringComparison.OrdinalIgnoreCase));
                    return match != null && detailNames.TryGetValue((section, match), out var name)
                        ? name
                        : null;
                });

                summaryCtx.Row++; // blank spacer between sections
            }

            FinalizeSheet(workbookPart, summaryPart, summaryCtx);
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(summaryPart),
                SheetId = sheetId++,
                Name = "Summary"
            });

            // ---- One detail sheet per table -----------------------------------
            if (model.IncludeDetails)
            {
                foreach (var section in model.Sections)
                {
                    foreach (var table in section.Tables)
                    {
                        var name = detailNames[(section, table)];

                        var part = workbookPart.AddNewPart<WorksheetPart>();
                        var ctx = new SheetContext { TableIds = tableIds };

                        if (!string.IsNullOrWhiteSpace(section.Heading))
                            AddRow(ctx, new[] { Cell(section.Heading, StyleHeading) });

                        foreach (var chart in section.Charts.Where(c =>
                            string.Equals(c.Title, table.Title, StringComparison.OrdinalIgnoreCase)))
                            ComposeChart(ctx, chart);

                        ComposeTable(ctx, table);

                        FinalizeSheet(workbookPart, part, ctx);
                        sheets.Append(new Sheet
                        {
                            Id = workbookPart.GetIdOfPart(part),
                            SheetId = sheetId++,
                            Name = name
                        });
                    }
                }
            }

            workbookPart.Workbook.Save();
        }

        return stream.ToArray();
    }

    /// <summary>Assembles the worksheet from a completed <see cref="SheetContext"/>: columns,
    /// data, internal hyperlinks, and any native tables.</summary>
    private static void FinalizeSheet(WorkbookPart workbookPart, WorksheetPart worksheetPart,
        SheetContext ctx)
    {
        var columns = new Columns(
            new Column { Min = 1, Max = 1, Width = 46, CustomWidth = true },
            new Column { Min = 2, Max = 8, Width = 22, CustomWidth = true });

        var worksheet = new Worksheet();
        worksheet.Append(columns);
        worksheet.Append(ctx.SheetData);

        // Internal navigation links (Summary -> detail tabs).
        if (ctx.Hyperlinks.Count > 0)
        {
            var hyperlinks = new Hyperlinks();
            foreach (var link in ctx.Hyperlinks)
            {
                hyperlinks.Append(new Hyperlink
                {
                    Reference = link.CellReference,
                    Location = $"'{link.TargetSheet}'!A1",
                    Display = link.TargetSheet
                });
            }
            worksheet.Append(hyperlinks);
        }

        // Attach native table definition parts and reference them from the worksheet.
        if (ctx.Tables.Count > 0)
        {
            var tableParts = new TableParts { Count = (uint)ctx.Tables.Count };
            foreach (var spec in ctx.Tables)
            {
                var tablePart = worksheetPart.AddNewPart<TableDefinitionPart>();
                tablePart.Table = BuildTable(spec);
                tablePart.Table.Save();
                tableParts.Append(new TablePart { Id = worksheetPart.GetIdOfPart(tablePart) });
            }
            worksheet.Append(tableParts);
        }

        worksheetPart.Worksheet = worksheet;
    }

    private void ComposeTiles(SheetContext ctx, string sectionHeading, List<ReportTile> tiles,
        Func<ReportTile, string?>? linkResolver = null)
    {
        if (tiles.Count == 0) return;

        var startRow = ctx.Row;

        // Header row with the branded navy fill.
        AddRow(ctx, new[] { Cell("KPI", StyleHeaderCell), Cell("Count", StyleHeaderCell) });

        var band = false;
        foreach (var tile in tiles)
        {
            var labelStyle = band ? StyleStripeBold : StyleBoldCell;
            var valueCell = tile.Error != null
                ? Cell("\u2014", band ? StyleStripe : StyleDefault)
                : NumberCell(tile.Value, band ? StyleNumberStripe : StyleNumber);

            // Link the label to its detail tab when one exists.
            var target = linkResolver?.Invoke(tile);
            if (target != null)
                ctx.Hyperlinks.Add(new SheetHyperlink("A" + ctx.Row, target));

            AddRow(ctx, new[] { Cell(tile.Label, labelStyle), valueCell });
            band = !band;
        }

        RegisterTable(ctx, $"Summary {sectionHeading}", startRow, colCount: 2,
            new[] { "KPI", "Count" });
        ctx.Row++; // spacer
    }

    private void ComposeChart(SheetContext ctx, ReportChart chart)
    {
        if (!string.IsNullOrWhiteSpace(chart.Title))
            AddRow(ctx, new[] { Cell(chart.Title, StyleHeading) });

        var startRow = ctx.Row;
        AddRow(ctx, new[]
        {
            Cell("Item", StyleHeaderCell),
            Cell("Value", StyleHeaderCell),
            Cell("Share", StyleHeaderCell)
        });

        var total = chart.Values.Sum(v => (long)v.Value);
        if (chart.Values.Count == 0)
        {
            AddRow(ctx, new[]
            {
                Cell("No data.", StyleMuted), Cell(string.Empty, StyleDefault), Cell(string.Empty, StyleDefault)
            });
        }
        else
        {
            var band = false;
            foreach (var v in chart.Values)
            {
                var share = total > 0 ? (double)v.Value / total : 0d;
                AddRow(ctx, new[]
                {
                    Cell(v.Label, band ? StyleStripeBold : StyleBoldCell),
                    NumberCell(v.Value, band ? StyleNumberStripe : StyleNumber),
                    Cell(share.ToString("P0", CultureInfo.InvariantCulture), band ? StyleStripe : StyleDefault)
                });
                band = !band;
            }
        }

        RegisterTable(ctx, chart.Title, startRow, colCount: 3, new[] { "Item", "Value", "Share" });
        ctx.Row++; // spacer
    }

    private void ComposeTable(SheetContext ctx, ReportTable table)
    {
        if (!string.IsNullOrWhiteSpace(table.Title))
            AddRow(ctx, new[] { Cell(table.Title, StyleHeading) });

        if (table.Error != null)
        {
            AddRow(ctx, new[] { Cell(table.Error, StyleMuted) });
            ctx.Row++;
            return;
        }

        if (table.Rows.Count == 0)
        {
            // A native table requires at least one body row; show a simple message instead.
            AddRow(ctx, table.Columns.Select(c => Cell(c, StyleHeaderCell)).ToArray());
            AddRow(ctx, new[] { Cell("No items.", StyleMuted) });
            ctx.Row++;
            return;
        }

        var startRow = ctx.Row;

        // Header row with the branded navy fill.
        AddRow(ctx, table.Columns.Select(c => Cell(c, StyleHeaderCell)).ToArray());

        var band = false;
        foreach (var rowData in table.Rows)
        {
            var cells = new List<Cell>();
            for (var i = 0; i < table.Columns.Count; i++)
            {
                var value = i < rowData.Count ? rowData[i] : string.Empty;
                // First column bold to match the on-screen/PDF/Word styling; band alternate rows.
                uint style = i == 0
                    ? (band ? StyleStripeBold : StyleBoldCell)
                    : (band ? StyleStripe : StyleDefault);
                cells.Add(Cell(value, style));
            }
            AddRow(ctx, cells.ToArray());
            band = !band;
        }

        RegisterTable(ctx, table.Title, startRow, table.Columns.Count, table.Columns);
        ctx.Row++; // spacer
    }

    // ---- Table registration -------------------------------------------------

    private void RegisterTable(SheetContext ctx, string displayName, uint startRow, int colCount,
        IReadOnlyList<string> columns)
    {
        // ctx.Row currently points just past the last written row, so the last data row is Row-1.
        var endRow = ctx.Row - 1;
        var reference = $"A{startRow}:{ColumnName(colCount)}{endRow}";

        var id = ctx.TableIds.Next++;
        // Table names must be unique and cannot contain spaces or start with a digit.
        var safeName = SafeTableName(displayName, id);

        // Excel requires unique, non-empty column names within a table.
        var uniqueColumns = MakeUniqueColumnNames(columns);

        ctx.Tables.Add(new TableSpec(id, safeName, reference, uniqueColumns));
    }

    private static Table BuildTable(TableSpec spec)
    {
        var tableColumns = new TableColumns { Count = (uint)spec.Columns.Count };
        for (var i = 0; i < spec.Columns.Count; i++)
        {
            tableColumns.Append(new TableColumn
            {
                Id = (uint)(i + 1),
                Name = spec.Columns[i]
            });
        }

        return new Table
        {
            Id = spec.Id,
            Name = spec.Name,
            DisplayName = spec.Name,
            Reference = spec.Reference,
            TotalsRowShown = false,
            AutoFilter = new AutoFilter { Reference = spec.Reference },
            TableColumns = tableColumns,
            TableStyleInfo = new TableStyleInfo
            {
                Name = TableStyleName,
                ShowFirstColumn = false,
                ShowLastColumn = false,
                ShowRowStripes = false,
                ShowColumnStripes = false
            }
        };
    }

    private static IReadOnlyList<string> MakeUniqueColumnNames(IReadOnlyList<string> columns)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(columns.Count);
        foreach (var raw in columns)
        {
            var name = string.IsNullOrWhiteSpace(raw) ? "Column" : raw.Trim();
            if (seen.TryGetValue(name, out var count))
            {
                count++;
                seen[name] = count;
                name = $"{name} {count}";
            }
            else
            {
                seen[name] = 1;
            }
            result.Add(name);
        }
        return result;
    }

    private static string SafeTableName(string displayName, uint id)
    {
        var chars = (displayName ?? string.Empty)
            .Where(c => char.IsLetterOrDigit(c) || c == '_')
            .ToArray();
        var baseName = new string(chars);
        if (string.IsNullOrEmpty(baseName) || char.IsDigit(baseName[0]))
            baseName = "Tbl" + baseName;
        return $"{baseName}_{id}";
    }

    /// <summary>
    /// Builds a valid, unique Excel worksheet name of the form '<category> - <kpi>'.
    /// Excel limits sheet names to 31 chars, forbids <c>\ / ? * [ ] :</c>, and requires
    /// uniqueness. When the full name is too long the category is abbreviated to its first
    /// word; collisions are resolved with a numeric suffix.
    /// </summary>
    private static string MakeSheetName(string category, string kpi, HashSet<string> used)
    {
        static string Clean(string s) => new(
            (s ?? string.Empty).Where(c => c is not ('\\' or '/' or '?' or '*' or '[' or ']' or ':'))
            .ToArray());

        var cat = Clean(category).Trim();
        var name = Clean(kpi).Trim();
        if (string.IsNullOrEmpty(name)) name = "KPI";

        var full = string.IsNullOrEmpty(cat) ? name : $"{cat} - {name}";
        if (full.Length > 31 && !string.IsNullOrEmpty(cat))
        {
            // Abbreviate the category to its first word.
            var firstWord = cat.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? cat;
            full = $"{firstWord} - {name}";
        }
        if (full.Length > 31)
            full = full[..31];

        // De-duplicate, reserving room for a " (n)" suffix within the 31-char cap.
        var candidate = full;
        var n = 1;
        while (!used.Add(candidate))
        {
            n++;
            var suffix = $" ({n})";
            var keep = Math.Max(0, 31 - suffix.Length);
            candidate = (full.Length > keep ? full[..keep] : full) + suffix;
        }
        return candidate;
    }

    // ---- Open XML helpers ---------------------------------------------------

    private static void AddRow(SheetContext ctx, Cell[] cells)
    {
        var r = new Row { RowIndex = ctx.Row };
        var col = 1;
        foreach (var cell in cells)
        {
            cell.CellReference = ColumnName(col) + ctx.Row;
            r.Append(cell);
            col++;
        }
        ctx.SheetData.Append(r);
        ctx.Row++;
    }

    private static Cell Cell(string text, uint styleIndex) => new()
    {
        DataType = CellValues.InlineString,
        StyleIndex = styleIndex,
        InlineString = new InlineString(new Text(text ?? string.Empty))
    };

    private static Cell NumberCell(int value, uint styleIndex = StyleNumber) => new()
    {
        DataType = CellValues.Number,
        StyleIndex = styleIndex,
        CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture))
    };

    private static string ColumnName(int index)
    {
        var name = string.Empty;
        while (index > 0)
        {
            var rem = (index - 1) % 26;
            name = (char)('A' + rem) + name;
            index = (index - 1) / 26;
        }
        return name;
    }

    private static Stylesheet BuildStylesheet()
    {
        // Fonts: 0=default, 1=title (brand red bold 16), 2=heading (bold 12),
        //        3=bold, 4=muted, 5=white bold (header).
        var fonts = new Fonts(
            new Font(new FontSize { Val = 11 }, new Color { Rgb = "FF1F2937" },
                new FontName { Val = "Calibri" }),
            new Font(new Bold(), new FontSize { Val = 16 }, new Color { Rgb = "FFE21A23" },
                new FontName { Val = "Calibri" }),
            new Font(new Bold(), new FontSize { Val = 12 }, new Color { Rgb = "FF1F2937" },
                new FontName { Val = "Calibri" }),
            new Font(new Bold(), new FontSize { Val = 11 }, new Color { Rgb = "FF1F2937" },
                new FontName { Val = "Calibri" }),
            new Font(new FontSize { Val = 11 }, new Color { Rgb = "FF6B7280" },
                new FontName { Val = "Calibri" }),
            new Font(new Bold(), new FontSize { Val = 11 }, new Color { Rgb = "FFFFFFFF" },
                new FontName { Val = "Calibri" }))
        { Count = 6 };

        // Fills: 0 and 1 are reserved by the spec; 2 = navy header (dashboard header color);
        //        3 = light banding stripe.
        var fills = new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            new Fill(new PatternFill(new ForegroundColor { Rgb = "FF16213E" })
            { PatternType = PatternValues.Solid }),
            new Fill(new PatternFill(new ForegroundColor { Rgb = "FFEDF0F7" })
            { PatternType = PatternValues.Solid }))
        { Count = 4 };

        var borders = new Borders(new Border(new LeftBorder(), new RightBorder(),
            new TopBorder(), new BottomBorder(), new DiagonalBorder())) { Count = 1 };

        // CellFormats (styleIndex order must match the Style* constants above).
        var cellFormats = new CellFormats(
            new CellFormat { FontId = 0, FillId = 0, BorderId = 0 },                                        // 0 default
            new CellFormat { FontId = 1, FillId = 0, BorderId = 0, ApplyFont = true },                      // 1 title
            new CellFormat { FontId = 2, FillId = 0, BorderId = 0, ApplyFont = true },                      // 2 heading
            new CellFormat { FontId = 3, FillId = 0, BorderId = 0, ApplyFont = true },                      // 3 bold cell
            new CellFormat { FontId = 4, FillId = 0, BorderId = 0, ApplyFont = true },                      // 4 muted
            new CellFormat { FontId = 5, FillId = 2, BorderId = 0, ApplyFont = true, ApplyFill = true },    // 5 header (navy/white)
            new CellFormat { FontId = 0, FillId = 0, BorderId = 0 },                                        // 6 number
            new CellFormat { FontId = 0, FillId = 3, BorderId = 0, ApplyFill = true },                      // 7 stripe text
            new CellFormat { FontId = 3, FillId = 3, BorderId = 0, ApplyFont = true, ApplyFill = true },    // 8 stripe bold
            new CellFormat { FontId = 0, FillId = 3, BorderId = 0, ApplyFill = true })                      // 9 stripe number
        { Count = 10 };

        return new Stylesheet(fonts, fills, borders, cellFormats);
    }
}
