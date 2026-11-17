using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.Reporting;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;

namespace ActiveRolesDashboard.Tests.Kpis;

[Trait("Stage", "Kpis")]
public class KpiExclusionServiceTests
{
    private const string Kpi = "PasswordNotRequired";

    private static KpiExclusionService Create(IEnumerable<string>? global = null, IEnumerable<string>? perKpi = null)
    {
        var cfg = new ActiveRolesConfig();
        cfg.KpiExclusions.Global = global?.ToList() ?? new();
        if (perKpi != null) cfg.KpiExclusions.PerKpi[Kpi] = perKpi.ToList();
        return new KpiExclusionService(new TestOptionsMonitor<ActiveRolesConfig>(cfg));
    }

    private static DashboardSummary Summary(int totalCount, params string[] names)
    {
        var summary = new DashboardSummary();
        summary.PasswordNotRequired = new ADUserAccountDetailSummary
        {
            TotalCount = totalCount,
            Items = names.Select(n => new ADUserAccountDetailInfo { Name = n, Domain = "test.local", Dn = $"CN={n},OU=Users,DC=test,DC=local" }).ToList()
        };
        return summary;
    }

    [Theory]
    [InlineData("svc_*", "svc_backup", true)]
    [InlineData("SVC_*", "svc_backup", true)]
    [InlineData("svc_?", "svc_a", true)]
    [InlineData("svc_?", "svc_ab", false)]
    [InlineData("*OU=Service*", "CN=x,OU=Service Accounts,DC=t", true)]
    [InlineData("a.b", "aXb", false)]
    [InlineData("(x)+", "(x)+", true)]
    [InlineData("svc_*", "", false)]
    [InlineData("svc_*", null, false)]
    [InlineData("   ", "anything", false)]
    [InlineData(" admin ", "admin", true)]
    [InlineData("Jösé*", "jösé.müller", true)]
    public void Wildcard_matching(string pattern, string? value, bool expected)
    {
        Assert.Equal(expected, WildcardPattern.IsMatch(pattern, value));
    }

    [Fact]
    public void Normalize_trims_and_deduplicates_case_insensitively()
    {
        var result = KpiExclusionConfig.Normalize([" a ", "A", "", "  ", "b", null!]);
        Assert.Equal(["a", "b"], result);
    }

    [Fact]
    public void Normalize_handles_null()
    {
        Assert.Empty(KpiExclusionConfig.Normalize(null));
    }

    [Fact]
    public void Patterns_combine_global_and_per_kpi_case_insensitively()
    {
        var service = Create(["svc_*"], ["SVC_*", "test*"]);
        Assert.Equal(["svc_*", "test*"], service.GetPatterns(Kpi.ToLowerInvariant()));
        Assert.Equal(["svc_*"], service.GetPatterns("OtherKpi"));
    }

    [Fact]
    public void No_patterns_leaves_count_unchanged()
    {
        var service = Create();
        Assert.Equal((3, null, false), service.Evaluate(Summary(3, "a", "b", "c"), Kpi));
    }

    [Fact]
    public void Matching_rows_are_removed_from_count()
    {
        var service = Create(perKpi: ["svc_*"]);
        var (count, error, notApplied) = service.Evaluate(Summary(3, "svc_a", "bob", "svc_b"), Kpi);
        Assert.Equal(1, count);
        Assert.Null(error);
        Assert.False(notApplied);
    }

    [Fact]
    public void Matches_against_distinguished_name()
    {
        var service = Create(["*OU=Users,DC=test*"]);
        Assert.Equal(0, service.Evaluate(Summary(2, "a", "b"), Kpi).Count);
    }

    [Fact]
    public void Incomplete_detail_list_is_flagged_not_applied()
    {
        var service = Create(["svc_*"]);
        var result = service.Evaluate(Summary(10, "svc_a", "b"), Kpi);
        Assert.Equal((10, null, true), result);
    }

    [Fact]
    public void Kpi_error_is_preserved_and_not_adjusted()
    {
        var service = Create(["*"]);
        var summary = Summary(2, "a", "b");
        summary.PasswordNotRequired.Error = "boom";
        Assert.Equal((2, "boom", false), service.Evaluate(summary, Kpi));
    }

    [Fact]
    public void Zero_count_is_not_adjusted()
    {
        var service = Create(["*"]);
        Assert.Equal((0, null, false), service.Evaluate(Summary(0), Kpi));
    }

    [Fact]
    public void Count_never_goes_negative()
    {
        var service = Create(["*"]);
        Assert.Equal(0, service.GetAdjustedResult(Summary(2, "a", "b"), Kpi).Count);
    }

    [Fact]
    public void ApplyToTable_appends_excluded_column_only_when_matched()
    {
        var service = Create(["svc_*"]);
        var table = new ReportTable { Columns = ["Name", "Distinguished Name"], Rows = [["svc_a", "dn1"], ["bob", "dn2"]] };

        Assert.Equal(1, service.ApplyToTable(Kpi, table));
        Assert.Equal(KpiExclusionService.ExcludedColumn, table.Columns[^1]);
        Assert.Equal("True", table.Rows[0][^1]);
        Assert.Equal("False", table.Rows[1][^1]);

        var untouched = new ReportTable { Columns = ["Name"], Rows = [["bob"]] };
        Assert.Equal(0, service.ApplyToTable(Kpi, untouched));
        Assert.Single(untouched.Columns);
    }

    [Fact]
    public void ApplyToTable_ignores_tables_without_identity_columns_errors_or_short_rows()
    {
        var service = Create(["*"]);
        Assert.Equal(0, service.ApplyToTable(Kpi, null));
        Assert.Equal(0, service.ApplyToTable(Kpi, new ReportTable { Columns = ["Other"], Rows = [["x"]] }));
        Assert.Equal(0, service.ApplyToTable(Kpi, new ReportTable { Columns = ["Name"], Rows = [["x"]], Error = "e" }));
        Assert.Equal(0, service.ApplyToTable(Kpi, new ReportTable { Columns = ["Name", "Distinguished Name"], Rows = [[]] }));
    }

    [Fact]
    public void LookupFor_returns_adjusted_results()
    {
        var service = Create(["svc_*"]);
        var lookup = service.LookupFor(Summary(2, "svc_a", "b"));
        Assert.Equal((1, null), lookup(Kpi));
    }
}
