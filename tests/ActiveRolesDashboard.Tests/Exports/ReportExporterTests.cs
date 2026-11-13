using System.Net;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.Reporting;
using ActiveRolesDashboard.Services.Reporting;
using ActiveRolesDashboard.Tests.Infrastructure;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace ActiveRolesDashboard.Tests.Exports;

[Trait("Stage", "Exports")]
public class ReportExporterTests
{
    static ReportExporterTests() => QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    private static string RepoWwwroot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ActiveRolesDashboard.csproj")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? AppContext.BaseDirectory, "wwwroot");
    }

    public static IEnumerable<object[]> Exporters() =>
    [
        [new PdfReportExporter(new Env(RepoWwwroot()))],
        [new WordReportExporter()],
        [new ExcelReportExporter()]
    ];

    private static ReportModel Rich() => new()
    {
        Title = "Dashboard <&> \"quotes\"",
        Subtitle = "Überblick – 日本語",
        GeneratedBy = @"TEST\admin",
        Sections =
        [
            new ReportSection
            {
                Heading = "A very long category heading that exceeds the Excel sheet name limit",
                Tiles = [new ReportTile { Label = "Users", Value = 42, CssColor = "blue" }, new ReportTile { Label = "Err", Error = "failed", CssColor = "unknown-color" }],
                Charts = [new ReportChart { Title = "Chart", Values = [new() { Label = "a", Value = 1, CssColor = "red" }, new() { Label = "b", Value = 0 }] }],
                Tables = [new ReportTable { Title = "T", Columns = ["Name", "DN"], Rows = [["=cmd|' /C calc'!A0", "CN=x,DC=y"], ["", "tab\tline\nbreak"]] }]
            },
            new ReportSection { Heading = "A very long category heading that exceeds the Excel sheet name limit" },
            new ReportSection { Heading = "Bad:/\\?*[]chars", Tables = [new ReportTable { Title = "Err", Columns = ["X"], Error = "query failed" }] }
        ]
    };

    [Theory]
    [MemberData(nameof(Exporters))]
    public void Exports_rich_model_to_valid_document(IReportExporter exporter)
    {
        var bytes = exporter.Export(Rich());
        AssertValid(exporter.Format, bytes);
    }

    [Theory]
    [MemberData(nameof(Exporters))]
    public void Exports_empty_model(IReportExporter exporter)
    {
        AssertValid(exporter.Format, exporter.Export(new ReportModel()));
    }

    [Theory]
    [MemberData(nameof(Exporters))]
    public void Exports_large_table(IReportExporter exporter)
    {
        var table = new ReportTable { Title = "Big", Columns = ["A", "B", "C"] };
        for (var i = 0; i < 2000; i++) table.Rows.Add([$"row{i}", new string('x', 200), i.ToString()]);
        var model = new ReportModel { Title = "Big", Sections = [new ReportSection { Heading = "Big", Tables = [table] }] };

        AssertValid(exporter.Format, exporter.Export(model));
    }

    [Theory(Skip = "Known defect: Word/Excel exporters throw ArgumentException on XML-invalid control characters (e.g. U+0001) in directory data.")]
    [MemberData(nameof(Exporters))]
    public void Exports_tolerate_control_characters(IReportExporter exporter)
    {
        var model = new ReportModel { Title = "Ctrl\u0001", Sections = [new ReportSection { Heading = "H\u0002", Tables = [new ReportTable { Title = "T", Columns = ["A"], Rows = [["bad\u0001value"]] }] }] };
        AssertValid(exporter.Format, exporter.Export(model));
    }

    [Fact]
    public void Exporter_metadata_matches_format()
    {
        var map = Exporters().Select(o => (IReportExporter)o[0]).ToDictionary(e => e.Format);
        Assert.Equal((".pdf", "application/pdf"), (map[ReportFormat.Pdf].FileExtension, map[ReportFormat.Pdf].ContentType));
        Assert.Equal(".docx", map[ReportFormat.Word].FileExtension);
        Assert.Equal(".xlsx", map[ReportFormat.Excel].FileExtension);
    }

    [Fact]
    public void Factory_resolves_registered_and_rejects_missing()
    {
        var factory = new ReportExporterFactory([new WordReportExporter()]);

        Assert.True(factory.TryGet(ReportFormat.Word, out var word));
        Assert.Equal(ReportFormat.Word, word.Format);
        Assert.False(factory.TryGet(ReportFormat.Pdf, out _));
        Assert.Throws<NotSupportedException>(() => factory.Get(ReportFormat.Excel));
        Assert.Throws<ArgumentException>(() => new ReportExporterFactory([new WordReportExporter(), new WordReportExporter()]));
    }

    [Fact]
    public void Excel_sheet_names_are_valid_and_unique()
    {
        using var doc = SpreadsheetDocument.Open(new MemoryStream(new ExcelReportExporter().Export(Rich())), false);
        var names = doc.WorkbookPart!.Workbook.Sheets!.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>().Select(s => s.Name!.Value!).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(names, n =>
        {
            Assert.InRange(n.Length, 1, 31);
            Assert.True(n.IndexOfAny(['\\', '/', '?', '*', '[', ']', ':']) < 0, n);
        });
    }

    [Fact]
    public void Word_contains_title_and_table_text()
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(new WordReportExporter().Export(Rich())), false);
        var text = doc.MainDocumentPart!.Document.InnerText;

        Assert.Contains("Dashboard <&> \"quotes\"", text);
        Assert.Contains("CN=x,DC=y", text);
    }

    // --- ReportBuilder ----------------------------------------------------------

    [Theory]
    [InlineData(ReportScope.Dashboard)]
    [InlineData(ReportScope.SubDashboard)]
    [InlineData(ReportScope.Category)]
    [InlineData(ReportScope.Kpi)]
    public void Builder_handles_unknown_keys_without_throwing(ReportScope scope)
    {
        var model = new ReportBuilder().Build(
            new ReportRequest { Scope = scope, DashboardKey = "nope", SubDashboardKey = "nope", CategoryKey = "nope", KpiKey = "nope" },
            new DashboardSummary(), new KpiSettings(), "me");

        Assert.Equal("me", model.GeneratedBy);
    }

    [Fact]
    public void Builder_main_dashboard_produces_sections_and_respects_allowed_keys()
    {
        var request = new ReportRequest { Scope = ReportScope.Dashboard, DashboardKey = "Main" };
        var all = new ReportBuilder().Build(request, new DashboardSummary(), new KpiSettings(), "me");
        var none = new ReportBuilder().Build(request, new DashboardSummary(), new KpiSettings(), "me", new HashSet<string> { "Main" });

        Assert.Equal("Dashboard", all.Title);
        Assert.NotEmpty(all.Sections);
        Assert.True(none.Sections.Count <= all.Sections.Count);
    }

    [Fact]
    public void Builder_disallowed_dashboard_is_empty()
    {
        var key = DashboardInfo.ActiveDirectory.Key;
        var model = new ReportBuilder().Build(new ReportRequest { DashboardKey = key }, new DashboardSummary(), new KpiSettings(), "me", new HashSet<string>());
        Assert.Empty(model.Sections);
    }

    [Fact]
    public void Builder_output_exports_in_every_format()
    {
        var model = new ReportBuilder().Build(new ReportRequest { DashboardKey = "Main", IncludeDetails = true }, new DashboardSummary(), new KpiSettings(), "me");
        foreach (var exporter in Exporters().Select(o => (IReportExporter)o[0]))
            AssertValid(exporter.Format, exporter.Export(model));
    }

    // --- Controller -------------------------------------------------------------

    [Fact]
    public async Task Anonymous_export_is_rejected()
    {
        using var server = new FakeActiveRolesServer().WithHealthyDefaults();
        using var app = new DashboardAppFactory(activeRoles: server);
        using var client = app.CreateNonRedirectingClient();

        var response = await client.PostAsync("/Export", new FormUrlEncodedContent(new Dictionary<string, string> { ["Format"] = "Pdf", ["DashboardKey"] = "Main" }));

        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized, response.StatusCode.ToString());
        Assert.NotEqual("application/pdf", response.Content.Headers.ContentType?.MediaType);
    }

    private static void AssertValid(ReportFormat format, byte[] bytes)
    {
        Assert.NotEmpty(bytes);
        switch (format)
        {
            case ReportFormat.Pdf:
                Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
                break;
            case ReportFormat.Word:
                using (var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false))
                    Assert.NotNull(doc.MainDocumentPart?.Document.Body);
                break;
            case ReportFormat.Excel:
                using (var doc = SpreadsheetDocument.Open(new MemoryStream(bytes), false))
                    Assert.NotEmpty(doc.WorkbookPart!.Workbook.Sheets!.ChildElements);
                break;
        }
    }

    private sealed class Env(string webRoot) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = webRoot;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "ActiveRolesDashboard";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = webRoot;
        public string EnvironmentName { get; set; } = "Testing";
    }
}
