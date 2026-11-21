using System.Text.Json;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.Definitions;
using ActiveRolesDashboard.Models.Definitions.Evaluation;
using ActiveRolesDashboard.Services.Definitions;
using Xunit;

namespace ActiveRolesDashboard.Tests.Definitions;

/// <summary>
/// Hidden assessment-only KPIs: evaluated from the embedded definitions but never rendered.
/// </summary>
public sealed class AssessmentDefinitionTests
{
    private static readonly DateTimeOffset Now = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private const long TicksPerDay = 864000000000;

    private static readonly DefinitionStore Store = new(DefinitionStore.LoadEmbedded(typeof(DefinitionStore).Assembly), null);

    private static DashboardSummary Evaluate(Action<DashboardSummary> seed)
    {
        var summary = new DashboardSummary();
        seed(summary);
        new DefinitionKpiCalculator(Store.Registry, new KpiEvaluator(new KpiFunctionRegistry()))
            .Populate(summary, new KpiSettings(), new EvaluationContext { Now = Now });
        return summary;
    }

    private static DatasetResult Dataset(params object[] items) =>
        new(items.Select(i => JsonSerializer.SerializeToElement(i)).ToList(), null);

    private static DashboardSummary WithSecurityData(params object[] policies) => Evaluate(s =>
    {
        s.CollectedDatasets["DomainPolicies"] = Dataset(policies);
        s.CollectedDatasets["KrbtgtAccounts"] = Dataset(
            new Dictionary<string, object> { ["pwdLastSet"] = Now.AddDays(-30).ToFileTime().ToString() },
            new Dictionary<string, object> { ["pwdLastSet"] = Now.AddDays(-400).ToFileTime().ToString() },
            new Dictionary<string, object> { ["pwdLastSet"] = "0" });
    });

    private static Dictionary<string, object> Policy(int minLength, int pwdProperties, int lockout, long maxAgeDays) => new()
    {
        ["minPwdLength"] = minLength,
        ["pwdProperties"] = pwdProperties,
        ["lockoutThreshold"] = lockout,
        ["maxPwdAge"] = (-maxAgeDays * TicksPerDay).ToString()
    };

    [Fact]
    public void Assessment_category_is_hidden_from_rendered_catalog()
    {
        var hidden = Store.Registry.Set.Kpis.Where(k => k.CategoryId == "Assessment").Select(k => k.Id).ToList();

        Assert.Equal(8, hidden.Count);
        Assert.DoesNotContain(CategoryInfo.All, c => c.Key == "Assessment");
        Assert.DoesNotContain(KpiInfo.All, k => hidden.Contains(k.Key));
    }

    [Fact]
    public void Krbtgt_password_age_reports_the_oldest_domain_and_ignores_unset_values()
    {
        var summary = WithSecurityData();

        Assert.Equal((400, null), summary.GetKpiResult("KrbtgtPasswordAgeDays"));
    }

    [Fact]
    public void Password_policy_kpis_count_weak_domains_and_report_the_worst_max_age()
    {
        var summary = WithSecurityData(
            Policy(minLength: 14, pwdProperties: 1, lockout: 5, maxAgeDays: 42),
            Policy(minLength: 7, pwdProperties: 0, lockout: 0, maxAgeDays: 180),
            Policy(minLength: 8, pwdProperties: 1, lockout: 50, maxAgeDays: 0));

        Assert.Equal((2, null), summary.GetKpiResult("WeakPasswordLength"));
        Assert.Equal((1, null), summary.GetKpiResult("PasswordComplexityDisabled"));
        Assert.Equal((2, null), summary.GetKpiResult("NoAccountLockout"));
        Assert.Equal((180, null), summary.GetKpiResult("PasswordMaxAgeDays"));
    }

    [Fact]
    public void Unsupported_os_composites_are_evaluated()
    {
        var summary = WithSecurityData();

        Assert.Equal((0, null), summary.GetKpiResult("UnsupportedServerOs"));
        Assert.Equal((0, null), summary.GetKpiResult("UnsupportedClientOs"));
        Assert.Equal((0, null), summary.GetKpiResult("UnsupportedOs"));
    }

    [Fact]
    public void Missing_dataset_reports_an_error()
    {
        var summary = Evaluate(_ => { });

        Assert.Equal("Dataset 'DomainPolicies' was not collected", summary.GetKpiResult("WeakPasswordLength").Error);
    }

    [Fact]
    public void Unevaluated_kpi_reports_not_evaluated()
    {
        Assert.Equal((0, DashboardSummary.KpiNotEvaluatedError), new DashboardSummary().GetKpiResult("WeakPasswordLength"));
    }
}
