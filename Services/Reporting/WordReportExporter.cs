using System.Globalization;
using System.IO.Compression;
using System.Text;
using ActiveRolesDashboard.Models.Reporting;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ActiveRolesDashboard.Services.Reporting;

/// <summary>
/// Renders a <see cref="ReportModel"/> to a Microsoft Word (.docx) document using the
/// Open XML SDK. Mirrors the appearance/content of <see cref="PdfReportExporter"/>:
/// a One Identity branded header, colored KPI tiles, native bar charts (with percentages),
/// and optional detail tables (data columns only).
/// </summary>
public class WordReportExporter : IReportExporter
{
    public ReportFormat Format => ReportFormat.Word;
    public string ContentType => "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    public string FileExtension => ".docx";

    // Brand palette approximating the dashboard's CssColor classes (hex without leading '#').
    private static readonly Dictionary<string, string> ColorMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["blue"] = "2563eb",
        ["green"] = "16a34a",
        ["teal"] = "0d9488",
        ["red"] = "dc2626",
        ["orange"] = "ea580c",
        ["amber"] = "d97706",
        ["purple"] = "7c3aed",
        ["pink"] = "db2777",
        ["slate"] = "475569",
        ["indigo"] = "4f46e5"
    };

    private const string Brand = "e21a23"; // One Identity red
    private const string Ink = "1f2937";
    private const string Muted = "6b7280";
    private const string Line = "e5e7eb";
    private const string TileHeaderBg = "f9fafb";

    private static string Hex(string? cssColor) =>
        ColorMap.TryGetValue(cssColor ?? string.Empty, out var hex) ? hex : "475569";

    public byte[] Export(ReportModel model)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var body = main.Document.Body!;

            // Add the standard parts Word expects in a normalized DOCX. Providing these
            // (styles, settings, font table, doc properties) ensures the SDK emits a proper
            // [Content_Types].xml with Override entries and avoids Word's "repair" prompt.
            AddStylesPart(main);
            AddSettingsPart(main);
            AddDocProperties(doc);

            ComposeHeader(body, model);
            ComposeContent(body, model);
            ComposeFooter(body);

            body.AppendChild(SectionProperties());
            main.Document.Save();
        }

        // DocumentFormat.OpenXml 3.5.1 registers the main document part via a
        // DocumentFormat.OpenXml 3.5.1 registers the main document part via a
        // <Default Extension="xml"> mapped to the WordprocessingML main type and omits the
        // required <Override PartName="/word/document.xml">. It also writes UTF-8 BOMs and
        // absolute relationship targets, all of which trigger Word's "repair" prompt.
        // Rebuild the package with a normalized, Word-friendly structure.
        return NormalizePackage(stream.ToArray());
    }

    private static byte[] NormalizePackage(byte[] docx)
    {
        const string ctName = "[Content_Types].xml";
        const string docType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
        const string overrideElement =
            "<Override PartName=\"/word/document.xml\" ContentType=\"" + docType + "\" />";

        // Read all entries from the SDK-produced package.
        var entries = new List<(string Name, byte[] Data)>();
        string? contentTypesXml = null;

        using (var input = new MemoryStream(docx))
        using (var archive = new ZipArchive(input, ZipArchiveMode.Read))
        {
            foreach (var entry in archive.Entries)
            {
                using var es = entry.Open();
                using var buffer = new MemoryStream();
                es.CopyTo(buffer);
                var data = StripBom(buffer.ToArray());

                if (string.Equals(entry.FullName, ctName, StringComparison.OrdinalIgnoreCase))
                {
                    contentTypesXml = Encoding.UTF8.GetString(data);
                }
                else if (entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                {
                    // Convert absolute relationship targets (e.g. "/word/styles.xml") to the
                    // relative form Word expects (e.g. "styles.xml", "../docProps/core.xml").
                    var rels = MakeRelationshipTargetsRelative(Encoding.UTF8.GetString(data), entry.FullName);
                    entries.Add((entry.FullName, new UTF8Encoding(false).GetBytes(rels)));
                }
                else
                {
                    entries.Add((entry.FullName, data));
                }
            }
        }

        if (contentTypesXml == null)
            return docx; // Nothing to fix.

        // Ensure the main document part has a proper Override rather than a blanket xml default.
        if (!contentTypesXml.Contains("PartName=\"/word/document.xml\"", StringComparison.OrdinalIgnoreCase))
        {
            contentTypesXml = System.Text.RegularExpressions.Regex.Replace(
                contentTypesXml,
                "<Default\\s+Extension=\"xml\"\\s+ContentType=\"" +
                System.Text.RegularExpressions.Regex.Escape(docType) + "\"\\s*/>",
                "<Default Extension=\"xml\" ContentType=\"application/xml\" />",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            contentTypesXml = contentTypesXml.Replace(
                "</Types>", overrideElement + "</Types>", StringComparison.OrdinalIgnoreCase);
        }

        // Rebuild the package from scratch (no BOM) so [Content_Types].xml is the first entry.
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var ctEntry = archive.CreateEntry(ctName, CompressionLevel.Optimal);
            using (var ctStream = ctEntry.Open())
            {
                var bytes = new UTF8Encoding(false).GetBytes(contentTypesXml);
                ctStream.Write(bytes, 0, bytes.Length);
            }

            foreach (var (name, data) in entries)
            {
                var e = archive.CreateEntry(name, CompressionLevel.Optimal);
                using var s = e.Open();
                s.Write(data, 0, data.Length);
            }
        }

        return output.ToArray();
    }

    private static byte[] StripBom(byte[] data) =>
        data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF
            ? data[3..]
            : data;

    private static string MakeRelationshipTargetsRelative(string relsXml, string relsPartName)
    {
        // The source directory of the .rels part (e.g. "_rels/.rels" -> "", "word/_rels/document.xml.rels" -> "word").
        var relsDir = relsPartName.Contains("/_rels/", StringComparison.OrdinalIgnoreCase)
            ? relsPartName[..relsPartName.IndexOf("/_rels/", StringComparison.OrdinalIgnoreCase)]
            : string.Empty;
        var baseSegments = relsDir.Length == 0
            ? Array.Empty<string>()
            : relsDir.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return System.Text.RegularExpressions.Regex.Replace(
            relsXml,
            "Target=\"(/[^\"]+)\"",
            match =>
            {
                var absolute = match.Groups[1].Value.TrimStart('/');
                var relative = ToRelativePath(baseSegments, absolute);
                return $"Target=\"{relative}\"";
            });
    }

    private static string ToRelativePath(string[] baseSegments, string absoluteTarget)
    {
        var targetSegments = absoluteTarget.Split('/', StringSplitOptions.RemoveEmptyEntries);

        var common = 0;
        while (common < baseSegments.Length &&
               common < targetSegments.Length - 1 &&
               string.Equals(baseSegments[common], targetSegments[common], StringComparison.OrdinalIgnoreCase))
        {
            common++;
        }

        var parts = new List<string>();
        for (var i = common; i < baseSegments.Length; i++)
            parts.Add("..");
        for (var i = common; i < targetSegments.Length; i++)
            parts.Add(targetSegments[i]);

        return string.Join('/', parts);
    }

    private static void AddStylesPart(MainDocumentPart main)
    {
        var stylesPart = main.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = new Styles(
            new DocDefaults(
                new RunPropertiesDefault(
                    new RunPropertiesBaseStyle(
                        new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri", ComplexScript = "Calibri" },
                        new FontSize { Val = "20" })),
                new ParagraphPropertiesDefault(
                    new ParagraphPropertiesBaseStyle(
                        new SpacingBetweenLines { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto }))),
            new Style(
                new StyleName { Val = "Normal" },
                new PrimaryStyle())
            {
                Type = StyleValues.Paragraph,
                StyleId = "Normal",
                Default = true
            });
        stylesPart.Styles.Save();
    }

    private static void AddSettingsPart(MainDocumentPart main)
    {
        var settingsPart = main.AddNewPart<DocumentSettingsPart>();
        settingsPart.Settings = new Settings(
            new Compatibility(
                new CompatibilitySetting
                {
                    Name = CompatSettingNameValues.CompatibilityMode,
                    Uri = "http://schemas.microsoft.com/office/word",
                    Val = "15"
                }));
        settingsPart.Settings.Save();
    }

    private static void AddDocProperties(WordprocessingDocument doc)
    {
        var core = doc.AddCoreFilePropertiesPart();
        using (var writer = new StreamWriter(core.GetStream(FileMode.Create), Encoding.UTF8))
        {
            writer.Write(
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<cp:coreProperties " +
                "xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
                "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" " +
                "xmlns:dcterms=\"http://purl.org/dc/terms/\" " +
                "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                "<dc:creator>One Identity Active Roles Dashboard</dc:creator>" +
                "<cp:lastModifiedBy>One Identity Active Roles Dashboard</cp:lastModifiedBy>" +
                $"<dcterms:created xsi:type=\"dcterms:W3CDTF\">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</dcterms:created>" +
                $"<dcterms:modified xsi:type=\"dcterms:W3CDTF\">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</dcterms:modified>" +
                "</cp:coreProperties>");
        }

        var app = doc.AddExtendedFilePropertiesPart();
        using var appWriter = new StreamWriter(app.GetStream(FileMode.Create), Encoding.UTF8);
        appWriter.Write(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Properties " +
            "xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\" " +
            "xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\">" +
            "<Application>One Identity Active Roles Dashboard</Application>" +
            "</Properties>");
    }

    private void ComposeHeader(Body body, ReportModel model)
    {
        // Two-column header row: brand mark on the left, title block right-aligned.
        var table = BorderlessTable();
        var grid = new TableGrid(
            new GridColumn { Width = "3000" },
            new GridColumn { Width = "6600" });
        table.AppendChild(grid);

        var row = new TableRow();

        var left = Cell("4680");
        left.AppendChild(Para(
            new[] { Run("One Identity", bold: true, sizeHalfPt: 28, color: Brand) },
            justification: JustificationValues.Left,
            spaceAfter: 0));
        left.AppendChild(Para(
            new[] { Run("Active Roles Dashboard", bold: false, sizeHalfPt: 16, color: Muted) },
            justification: JustificationValues.Left,
            spaceAfter: 0));
        row.AppendChild(left);

        var right = Cell("6600");
        right.AppendChild(Para(
            new[] { Run(model.Title, bold: true, sizeHalfPt: 36, color: Ink) },
            justification: JustificationValues.Right,
            spaceAfter: 0));
        if (!string.IsNullOrWhiteSpace(model.Subtitle))
        {
            right.AppendChild(Para(
                new[] { Run(model.Subtitle, bold: false, sizeHalfPt: 22, color: Muted) },
                justification: JustificationValues.Right,
                spaceAfter: 0));
        }
        right.AppendChild(Para(
            new[] { Run($"Generated {model.GeneratedUtc.ToLocalTime():yyyy-MM-dd HH:mm}", bold: false, sizeHalfPt: 16, color: Muted) },
            justification: JustificationValues.Right,
            spaceAfter: 0));
        row.AppendChild(right);

        table.AppendChild(row);
        body.AppendChild(table);

        // Brand-colored divider under the header.
        body.AppendChild(Divider(Brand, sizeEighthPt: 16));
        body.AppendChild(Spacer());
    }

    private void ComposeContent(Body body, ReportModel model)
    {
        if (model.Sections.Count == 0)
        {
            body.AppendChild(Para(
                new[] { Run("No data available for the selected scope.", italic: true, color: Muted) }));
            return;
        }

        foreach (var section in model.Sections)
        {
            ComposeSection(body, section, model.IncludeDetails);
        }
    }

    private void ComposeSection(Body body, ReportSection section, bool includeDetails)
    {
        body.AppendChild(Para(
            new[] { Run(section.Heading, bold: true, sizeHalfPt: 28, color: Ink) },
            spaceAfter: 120));

        if (section.Tiles.Count > 0)
            ComposeTiles(body, section.Tiles);

        foreach (var chart in section.Charts)
            ComposeChart(body, chart);

        if (includeDetails)
        {
            foreach (var table in section.Tables)
                ComposeTable(body, table);
        }

        body.AppendChild(Spacer());
    }

    private void ComposeTiles(Body body, List<ReportTile> tiles)
    {
        const int perRow = 4;
        for (var i = 0; i < tiles.Count; i += perRow)
        {
            var rowTiles = tiles.Skip(i).Take(perRow).ToList();

            var table = BorderedTable(Line);
            var grid = new TableGrid();
            for (var c = 0; c < perRow; c++)
                grid.AppendChild(new GridColumn { Width = "2400" });
            table.AppendChild(grid);

            var row = new TableRow();
            foreach (var tile in rowTiles)
            {
                var color = Hex(tile.CssColor);
                var cell = Cell("2400");
                // Colored accent bar (mirrors the tile's top border).
                SetCellBorders(cell, new TableCellBorders(
                    new TopBorder { Val = BorderValues.Single, Color = color, Size = 24 },
                    new BottomBorder { Val = BorderValues.Single, Color = Line, Size = 4 }));

                var valueText = tile.Error != null ? "\u2014" : tile.Value.ToString("N0", CultureInfo.InvariantCulture);
                var valueColor = tile.Error != null ? Muted : color;
                cell.AppendChild(Para(
                    new[] { Run(valueText, bold: true, sizeHalfPt: 40, color: valueColor) },
                    spaceAfter: 0));
                cell.AppendChild(Para(
                    new[] { Run(tile.Label, sizeHalfPt: 18, color: Muted) },
                    spaceAfter: 0));
                row.AppendChild(cell);
            }

            // Pad the final row so tiles keep a consistent width.
            // Every table cell must contain at least one block-level element (a paragraph),
            // otherwise Word treats the document as corrupt and prompts to repair.
            for (var p = rowTiles.Count; p < perRow; p++)
            {
                var padCell = Cell("2400");
                padCell.AppendChild(new Paragraph());
                row.AppendChild(padCell);
            }

            table.AppendChild(row);
            body.AppendChild(table);
            body.AppendChild(Spacer(60));
        }
    }

    private void ComposeChart(Body body, ReportChart chart)
    {
        var max = chart.Values.Count > 0 ? Math.Max(1, chart.Values.Max(v => v.Value)) : 1;
        var total = chart.Values.Sum(v => (long)v.Value);

        body.AppendChild(Para(
            new[] { Run(chart.Title, bold: true, sizeHalfPt: 22, color: Ink) },
            spaceAfter: 60));

        const int barCells = 20; // resolution of the text-based bar
        var table = BorderlessTable();
        table.AppendChild(new TableGrid(
            new GridColumn { Width = "3000" },
            new GridColumn { Width = "4600" },
            new GridColumn { Width = "1000" },
            new GridColumn { Width = "1000" }));

        foreach (var v in chart.Values)
        {
            var color = Hex(v.CssColor);
            var fraction = (double)v.Value / max;
            var share = total > 0 ? (double)v.Value / total : 0d;
            var filled = (int)Math.Round(Math.Clamp(fraction, 0d, 1d) * barCells);

            var row = new TableRow();

            var labelCell = Cell("3000");
            labelCell.AppendChild(Para(new[] { Run(v.Label, sizeHalfPt: 18, color: Muted) }, spaceAfter: 0));
            row.AppendChild(labelCell);

            // Bar: filled block characters colored to the series, remainder muted.
            var barCell = Cell("4600");
            var runs = new List<Run>();
            if (filled > 0)
                runs.Add(Run(new string('\u2588', filled), sizeHalfPt: 18, color: color));
            if (filled < barCells)
                runs.Add(Run(new string('\u2588', barCells - filled), sizeHalfPt: 18, color: Line));
            barCell.AppendChild(Para(runs, spaceAfter: 0));
            row.AppendChild(barCell);

            var valueCell = Cell("1000");
            valueCell.AppendChild(Para(
                new[] { Run(v.Value.ToString("N0", CultureInfo.InvariantCulture), bold: true, sizeHalfPt: 18, color: Ink) },
                justification: JustificationValues.Right, spaceAfter: 0));
            row.AppendChild(valueCell);

            var pctCell = Cell("1000");
            pctCell.AppendChild(Para(
                new[] { Run(share.ToString("P0", CultureInfo.InvariantCulture), sizeHalfPt: 18, color: Muted) },
                justification: JustificationValues.Right, spaceAfter: 0));
            row.AppendChild(pctCell);

            table.AppendChild(row);
        }

        body.AppendChild(table);
        body.AppendChild(Spacer(80));
    }

    private void ComposeTable(Body body, ReportTable table)
    {
        body.AppendChild(Para(
            new[] { Run(table.Title, bold: true, sizeHalfPt: 22, color: Ink) },
            spaceAfter: 60));

        if (table.Error != null)
        {
            body.AppendChild(Para(new[] { Run(table.Error, italic: true, sizeHalfPt: 18, color: Muted) }));
            return;
        }

        if (table.Rows.Count == 0)
        {
            body.AppendChild(Para(new[] { Run("No items.", italic: true, sizeHalfPt: 18, color: Muted) }));
            return;
        }

        var t = BorderedTable(Line);
        var grid = new TableGrid();
        foreach (var _ in table.Columns)
            grid.AppendChild(new GridColumn());
        t.AppendChild(grid);

        // Header row.
        var header = new TableRow();
        foreach (var c in table.Columns)
        {
            var cell = Cell();
            SetCellShading(cell, new Shading { Val = ShadingPatternValues.Clear, Fill = TileHeaderBg });
            cell.AppendChild(Para(new[] { Run(c, bold: true, sizeHalfPt: 18, color: Ink) }, spaceAfter: 0));
            header.AppendChild(cell);
        }
        t.AppendChild(header);

        foreach (var rowData in table.Rows)
        {
            var row = new TableRow();
            for (var i = 0; i < table.Columns.Count; i++)
            {
                var value = i < rowData.Count ? rowData[i] : string.Empty;
                var cell = Cell();
                cell.AppendChild(Para(new[] { Run(value, bold: i == 0, sizeHalfPt: 16, color: Ink) }, spaceAfter: 0));
                row.AppendChild(cell);
            }
            t.AppendChild(row);
        }

        body.AppendChild(t);
        body.AppendChild(Spacer(80));
    }

    private void ComposeFooter(Body body)
    {
        body.AppendChild(Divider(Line, sizeEighthPt: 8));
        var table = BorderlessTable();
        table.AppendChild(new TableGrid(
            new GridColumn { Width = "4800" },
            new GridColumn { Width = "4800" }));
        var row = new TableRow();

        var left = Cell("4800");
        left.AppendChild(Para(
            new[] { Run("One Identity Active Roles Dashboard", sizeHalfPt: 16, color: Muted) },
            justification: JustificationValues.Left, spaceAfter: 0));
        row.AppendChild(left);

        var right = Cell("4800");
        right.AppendChild(Para(
            new[] { Run($"Generated {DateTime.Now:yyyy-MM-dd HH:mm}", sizeHalfPt: 16, color: Muted) },
            justification: JustificationValues.Right, spaceAfter: 0));
        row.AppendChild(right);

        table.AppendChild(row);
        body.AppendChild(table);
    }

    // ---- Open XML helpers ---------------------------------------------------

    private static Run Run(string text, bool bold = false, bool italic = false, int sizeHalfPt = 20, string color = Ink)
    {
        // NOTE: CT_RPr requires a strict child order: b, i, ... color, ... sz.
        var props = new RunProperties();
        if (bold) props.AppendChild(new Bold());
        if (italic) props.AppendChild(new Italic());
        props.AppendChild(new Color { Val = color });
        props.AppendChild(new FontSize { Val = sizeHalfPt.ToString(CultureInfo.InvariantCulture) });

        return new Run(props, new Text(text ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve });
    }

    private static Paragraph Para(IEnumerable<Run> runs, JustificationValues? justification = null, int spaceAfter = 120)
    {
        // NOTE: CT_PPr requires order: pBdr ... spacing ... jc.
        var props = new ParagraphProperties();
        props.AppendChild(new SpacingBetweenLines { After = spaceAfter.ToString(CultureInfo.InvariantCulture), Before = "0" });
        if (justification.HasValue)
            props.AppendChild(new Justification { Val = justification.Value });

        var para = new Paragraph(props);
        foreach (var run in runs)
            para.AppendChild(run);
        return para;
    }

    private static Table BorderlessTable()
    {
        var props = new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableLayout { Type = TableLayoutValues.Fixed });
        return new Table(props);
    }

    private static Table BorderedTable(string borderColor)
    {
        // NOTE: CT_TblPr requires order: tblW, ..., tblBorders, shd, tblLayout, tblCellMar.
        var props = new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Color = borderColor, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Color = borderColor, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Color = borderColor, Size = 4 },
                new InsideVerticalBorder { Val = BorderValues.Single, Color = borderColor, Size = 4 }),
            new TableLayout { Type = TableLayoutValues.Fixed });
        return new Table(props);
    }

    private static TableCell Cell(string? width = null)
    {
        // NOTE: CT_TcPr requires order: tcW, tcBorders, shd, tcMar, vAlign.
        // Callers that add tcBorders/shd must insert them in that position (see helpers below).
        var cellProps = new TableCellProperties();
        if (width != null)
            cellProps.AppendChild(new TableCellWidth { Width = width, Type = TableWidthUnitValues.Dxa });
        cellProps.AppendChild(new TableCellMargin(
            new TopMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
            new BottomMargin { Width = "60", Type = TableWidthUnitValues.Dxa }));
        cellProps.AppendChild(new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center });

        return new TableCell(cellProps);
    }

    // Inserts tcBorders in the schema-correct position (after tcW, before tcMar).
    private static void SetCellBorders(TableCell cell, TableCellBorders borders)
    {
        var props = cell.GetFirstChild<TableCellProperties>()!;
        var margin = props.GetFirstChild<TableCellMargin>();
        props.InsertBefore(borders, margin);
    }

    // Inserts shd in the schema-correct position (after tcBorders, before tcMar).
    private static void SetCellShading(TableCell cell, Shading shading)
    {
        var props = cell.GetFirstChild<TableCellProperties>()!;
        var margin = props.GetFirstChild<TableCellMargin>();
        props.InsertBefore(shading, margin);
    }

    private static Paragraph Divider(string color, int sizeEighthPt)
    {
        var props = new ParagraphProperties();
        props.AppendChild(new ParagraphBorders(
            new BottomBorder { Val = BorderValues.Single, Color = color, Size = (uint)sizeEighthPt }));
        props.AppendChild(new SpacingBetweenLines { After = "40", Before = "40" });
        return new Paragraph(props);
    }

    private static Paragraph Spacer(int after = 160) =>
        new Paragraph(new ParagraphProperties(
            new SpacingBetweenLines { After = after.ToString(CultureInfo.InvariantCulture), Before = "0" }));

    private static SectionProperties SectionProperties() =>
        new SectionProperties(
            new PageSize { Width = 11906, Height = 16838 }, // A4 portrait in twips
            new PageMargin { Top = 720, Bottom = 720, Left = 720, Right = 720, Header = 0, Footer = 0, Gutter = 0 });
}
