using ActiveRolesDashboard.Models.Reporting;

namespace ActiveRolesDashboard.Services.PermissionTracker;

/// <summary>
/// Turns a Permission Tracker scan into a format-neutral <see cref="ReportModel"/> so the existing
/// PDF/Excel/Word exporters can render it. Applies the same filters as the page.
/// </summary>
public static class PermissionTrackerReportBuilder
{
    public sealed record Filters(bool RiskOnly, bool HideInherited, bool HideBuiltIn, bool HideDefault = true, bool HideExchange = true);

    /// <param name="text">Resolves a localized label by resource key.</param>
    public static ReportModel Build(PermissionScanJob scan, Filters filters, string generatedBy, Func<string, string> text)
    {
        var result = scan.Result;
        var adRows = result.AdObjects
            .SelectMany(o => o.Entries
                .Where(e => (!filters.RiskOnly || e.IsHighRisk) && (!filters.HideInherited || !e.IsInherited)
                    && (!filters.HideBuiltIn || !e.IsWellKnownTrustee) && (!filters.HideDefault || !e.IsDefault)
                    && (!filters.HideExchange || !ExchangeDefaults.IsExchangeTrustee(result.TrusteeName(e.TrusteeSid))))
                .Select(e => (o.Object, Entry: e)))
            .ToList();
        var arRows = result.ArEntries.Where(e => !filters.RiskOnly || e.IsHighRisk).ToList();

        var model = new ReportModel
        {
            Title = text("PageTitle"),
            Subtitle = string.Join(" · ", new[]
            {
                scan.Request.BaseDn,
                $"{text("Status")}: {text("Status_" + scan.Status)}",
                result.Truncated ? text("Truncated") : null
            }.Where(s => !string.IsNullOrEmpty(s))),
            GeneratedBy = generatedBy,
            GeneratedUtc = DateTime.UtcNow,
            IncludeDetails = true
        };

        if (scan.Request.IncludeActiveDirectory)
        {
            var section = new ReportSection { Heading = text("SourceAd") };
            section.Tiles.Add(new ReportTile { Label = text("Objects"), Value = result.AdObjects.Count, CssColor = "blue" });
            section.Tiles.Add(new ReportTile { Label = text("Rights"), Value = adRows.Count, CssColor = "blue" });
            section.Tiles.Add(new ReportTile { Label = text("HighRisk"), Value = adRows.Count(r => r.Entry.IsHighRisk), CssColor = "red" });

            section.Tables.Add(new ReportTable
            {
                Title = text("SourceAd"),
                Columns = [text("Object"), text("Trustee"), text("Type"), text("Rights"), text("AppliesTo"), text("Inherited"), text("Risk")],
                Rows = adRows
                    .OrderByDescending(r => r.Entry.IsHighRisk).ThenBy(r => r.Object.Dn, StringComparer.OrdinalIgnoreCase)
                    .Select(r => (IReadOnlyList<string>)
                    [
                        r.Object.Dn,
                        result.TrusteeName(r.Entry.TrusteeSid),
                        r.Entry.IsAllow ? text("Allow") : text("Deny"),
                        Rights(r.Entry.Rights, r.Entry.ObjectTypeName),
                        r.Entry.AppliesTo,
                        r.Entry.IsInherited ? text("Yes") : text("No"),
                        r.Entry.IsHighRisk ? text("HighRisk") : string.Empty
                    ]).ToList()
            });

            var failed = result.AdObjects.Where(o => o.Error != null).ToList();
            if (failed.Count > 0)
                section.Tables.Add(new ReportTable
                {
                    Title = text("ReadFailed"),
                    Columns = [text("Object"), text("Status")],
                    Rows = failed.Select(o => (IReadOnlyList<string>)[o.Object.Dn, o.Error!]).ToList()
                });
            model.Sections.Add(section);
        }

        if (scan.Request.IncludeActiveRoles)
        {
            var section = new ReportSection { Heading = text("SourceAr") };
            section.Tiles.Add(new ReportTile { Label = text("Rights"), Value = arRows.Count, CssColor = "blue" });
            section.Tiles.Add(new ReportTile { Label = text("HighRisk"), Value = arRows.Count(r => r.IsHighRisk), CssColor = "red" });
            section.Tables.Add(new ReportTable
            {
                Title = text("SourceAr"),
                Error = result.ArError,
                Columns = [text("Object"), text("Trustee"), text("AccessTemplate"), text("Type"), text("Rights"), text("AppliesTo"), text("Risk")],
                Rows = arRows
                    .OrderByDescending(r => r.IsHighRisk).ThenBy(r => r.SecuredObjectDn, StringComparer.OrdinalIgnoreCase)
                    .Select(r => (IReadOnlyList<string>)
                    [
                        r.SecuredObjectDn,
                        result.TrusteeName(r.TrusteeSid),
                        r.AccessTemplateName,
                        r.IsAllow ? text("Allow") : text("Deny"),
                        Rights(r.Rights, r.ObjectTypeName),
                        r.AppliesTo,
                        r.IsHighRisk ? text("HighRisk") : string.Empty
                    ]).ToList()
            });
            model.Sections.Add(section);
        }

        return model;
    }

    private static string Rights(string rights, string? objectType) =>
        objectType is { Length: > 0 } ? $"{rights} ({objectType})" : rights;
}
