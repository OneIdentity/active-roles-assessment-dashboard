using ActiveRolesDashboard.Models.Reporting;

namespace ActiveRolesDashboard.Services.Reporting;

/// <summary>
/// Renders a format-neutral <see cref="ReportModel"/> to a concrete file format.
/// Implementations are keyed by <see cref="Format"/> so new formats (Excel, Word)
/// can be added without changing callers.
/// </summary>
public interface IReportExporter
{
    /// <summary>The format this exporter produces.</summary>
    ReportFormat Format { get; }

    /// <summary>MIME content type for the generated file (e.g. "application/pdf").</summary>
    string ContentType { get; }

    /// <summary>File extension including the leading dot (e.g. ".pdf").</summary>
    string FileExtension { get; }

    /// <summary>Generates the document bytes for the given report.</summary>
    byte[] Export(ReportModel model);
}

/// <summary>
/// Resolves the correct <see cref="IReportExporter"/> for a requested format.
/// </summary>
public class ReportExporterFactory
{
    private readonly IReadOnlyDictionary<ReportFormat, IReportExporter> _exporters;

    public ReportExporterFactory(IEnumerable<IReportExporter> exporters)
    {
        _exporters = exporters.ToDictionary(e => e.Format);
    }

    public bool TryGet(ReportFormat format, out IReportExporter exporter)
        => _exporters.TryGetValue(format, out exporter!);

    public IReportExporter Get(ReportFormat format)
    {
        if (_exporters.TryGetValue(format, out var exporter))
            return exporter;
        throw new NotSupportedException($"No exporter is registered for format '{format}'.");
    }
}
