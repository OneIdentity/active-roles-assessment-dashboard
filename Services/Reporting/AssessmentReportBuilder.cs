using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.Reporting;
using ActiveRolesDashboard.Services;

namespace ActiveRolesDashboard.Services.Reporting;

/// <summary>
/// Maps a computed <see cref="AssessmentResult"/> into a format-neutral
/// <see cref="ReportModel"/> so it can be exported through the existing
/// PDF/Word exporters. Produces a scorecard section (score + pass/warn/fail
/// tiles) followed by one section per category containing a table of checks.
/// </summary>
public class AssessmentReportBuilder
{
    public ReportModel Build(AssessmentResult assessment, string generatedBy)
    {
        var typeName = AssessmentTypeInfo.DisplayName(assessment.Type);
        var model = new ReportModel
        {
            Title = $"{typeName} Security Assessment",
            Subtitle = string.IsNullOrWhiteSpace(assessment.Label)
                ? $"Grade {assessment.Grade} \u2022 Score {assessment.Score}/100"
                : $"{assessment.Label} \u2014 Grade {assessment.Grade} \u2022 Score {assessment.Score}/100",
            GeneratedUtc = assessment.GeneratedUtc,
            GeneratedBy = string.IsNullOrWhiteSpace(generatedBy) ? (assessment.GeneratedBy ?? string.Empty) : generatedBy,
            IncludeDetails = true
        };

        // Optional scope/disclaimer section (currently populated for GDPR). Rendered as a
        // single-cell table so it flows through the existing PDF/Word exporters without a
        // schema change.
        var disclaimer = AssessmentTypeInfo.Description(assessment.Type);
        if (!string.IsNullOrWhiteSpace(disclaimer))
        {
            var scopeSection = new ReportSection { Heading = "Scope & Disclaimer" };
            scopeSection.Tables.Add(new ReportTable
            {
                Title = "Scope & Disclaimer",
                Columns = new List<string> { "Scope" },
                Rows = new List<IReadOnlyList<string>>
                {
                    new List<string> { disclaimer }
                }
            });
            model.Sections.Add(scopeSection);
        }

        // Scorecard summary section.
        var summarySection = new ReportSection { Heading = "Summary" };
        summarySection.Tiles.Add(new ReportTile { Label = "Score", Value = assessment.Score, CssColor = "blue" });
        summarySection.Tiles.Add(new ReportTile { Label = "Passed", Value = assessment.PassCount, CssColor = "green" });
        summarySection.Tiles.Add(new ReportTile { Label = "Warnings", Value = assessment.WarnCount, CssColor = "amber" });
        summarySection.Tiles.Add(new ReportTile { Label = "Failed", Value = assessment.FailCount, CssColor = "red" });
        if (assessment.NotApplicableCount > 0)
            summarySection.Tiles.Add(new ReportTile { Label = "N/A", Value = assessment.NotApplicableCount, CssColor = "slate" });
        model.Sections.Add(summarySection);

        // One section per category with a table of its checks.
        foreach (var category in assessment.Categories)
        {
            var categoryName = AssessmentLocalizer.Category(category.Name);
            var section = new ReportSection { Heading = categoryName };
            var table = new ReportTable
            {
                Title = categoryName,
                Columns = new List<string> { "Check", "Severity", "Count", "Status", "Recommendation" }
            };

            foreach (var check in category.Checks)
            {
                var status = check.Status == AssessmentStatus.NotApplicable ? "N/A" : check.Status.ToString();
                var count = check.Error != null ? "\u2014" : check.Count.ToString();
                var note = check.Error != null
                    ? check.Error
                    : (check.Status == AssessmentStatus.Pass ? "No action required" : AssessmentLocalizer.Recommendation(check.RuleId, check.Recommendation));

                table.Rows.Add(new List<string>
                {
                    AssessmentLocalizer.Title(check.RuleId, check.Title),
                    check.Severity.ToString(),
                    count,
                    status,
                    note ?? string.Empty
                });
            }

            section.Tables.Add(table);
            model.Sections.Add(section);
        }

        return model;
    }
}
