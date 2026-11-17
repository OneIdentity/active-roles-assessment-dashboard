using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;

namespace ActiveRolesDashboard.Tests.Kpis;

[Trait("Stage", "Kpis")]
public class KpiThresholdServiceTests
{
    private static readonly ResolvedKpiThresholds Fallback = new(5, 10);

    private static KpiThresholdService Create(params (string Key, KpiThresholdConfig Config)[] entries)
    {
        var cfg = new ActiveRolesConfig();
        foreach (var (key, config) in entries) cfg.KpiThresholds[key] = config;
        return new KpiThresholdService(new TestOptionsMonitor<ActiveRolesConfig>(cfg));
    }

    [Theory]
    [InlineData(KpiThresholdMode.Count, 1, 2, true)]
    [InlineData(KpiThresholdMode.Count, 5, 5, true)]
    [InlineData(KpiThresholdMode.Percent, 50, 100, true)]
    [InlineData(KpiThresholdMode.Count, 0, 5, false)]
    [InlineData(KpiThresholdMode.Count, 5, 0, false)]
    [InlineData(KpiThresholdMode.Count, -1, 5, false)]
    [InlineData(KpiThresholdMode.Count, 10, 5, false)]
    [InlineData(KpiThresholdMode.Percent, 50, 100.01, false)]
    [InlineData(KpiThresholdMode.Count, 50, 1000, true)]
    public void Validate_enforces_boundaries(KpiThresholdMode mode, double medium, double high, bool valid)
    {
        var config = new KpiThresholdConfig { Mode = mode, Medium = medium, High = high };
        Assert.Equal(valid, config.Validate() == null);
    }

    [Fact]
    public void Unconfigured_kpi_uses_fallback()
    {
        var service = Create();
        Assert.Null(service.GetConfigured("PasswordNotRequired"));
        Assert.Equal(Fallback, service.Resolve("PasswordNotRequired", Fallback, null));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Empty_key_is_not_configured(string? key)
    {
        var service = Create(("X", new KpiThresholdConfig { Medium = 1, High = 2 }));
        Assert.Null(service.GetConfigured(key!));
    }

    [Fact]
    public void Lookup_is_case_insensitive()
    {
        var service = Create(("PasswordNotRequired", new KpiThresholdConfig { Medium = 2, High = 4 }));
        Assert.NotNull(service.GetConfigured("passwordnotrequired"));
    }

    [Fact]
    public void Invalid_configuration_falls_back()
    {
        var service = Create(("K", new KpiThresholdConfig { Medium = 10, High = 5 }));
        Assert.Null(service.GetConfigured("K"));
        Assert.Equal(Fallback, service.Resolve("K", Fallback, null));
    }

    [Fact]
    public void Count_mode_rounds_up_fractional_values()
    {
        var service = Create(("K", new KpiThresholdConfig { Medium = 2.1, High = 3.5 }));
        Assert.Equal(new ResolvedKpiThresholds(3, 4), service.Resolve("K", Fallback, null));
    }

    [Fact]
    public void Count_mode_normalizes_minimum_of_one()
    {
        var service = Create(("K", new KpiThresholdConfig { Medium = 0.2, High = 0.5 }));
        Assert.Equal(new ResolvedKpiThresholds(1, 1), service.Resolve("K", Fallback, null));
    }

    [Fact]
    public void Percent_mode_uses_explicit_total_kpi()
    {
        var service = Create(("K", new KpiThresholdConfig { Mode = KpiThresholdMode.Percent, Medium = 10, High = 25, TotalKpiKey = "Total" }));
        string? requested = null;
        var result = service.Resolve("K", Fallback, key => { requested = key; return (200, null); });

        Assert.Equal("Total", requested);
        Assert.Equal(new ResolvedKpiThresholds(20, 50), result);
    }

    [Fact]
    public void Percent_mode_rounds_up()
    {
        var service = Create(("K", new KpiThresholdConfig { Mode = KpiThresholdMode.Percent, Medium = 10, High = 25, TotalKpiKey = "Total" }));
        Assert.Equal(new ResolvedKpiThresholds(1, 1), service.Resolve("K", Fallback, _ => (3, null)));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-5, null)]
    [InlineData(100, "query failed")]
    public void Percent_mode_with_unusable_total_falls_back(int total, string? error)
    {
        var service = Create(("K", new KpiThresholdConfig { Mode = KpiThresholdMode.Percent, Medium = 10, High = 20, TotalKpiKey = "Total" }));
        Assert.Equal(Fallback, service.Resolve("K", Fallback, _ => (total, error)));
    }

    [Fact]
    public void Percent_mode_without_lookup_falls_back()
    {
        var service = Create(("K", new KpiThresholdConfig { Mode = KpiThresholdMode.Percent, Medium = 10, High = 20, TotalKpiKey = "Total" }));
        Assert.Equal(Fallback, service.Resolve("K", Fallback, null));
    }

    [Theory]
    [InlineData(-1, KpiThresholdLevel.None)]
    [InlineData(0, KpiThresholdLevel.None)]
    [InlineData(1, KpiThresholdLevel.Low)]
    [InlineData(4, KpiThresholdLevel.Low)]
    [InlineData(5, KpiThresholdLevel.Medium)]
    [InlineData(9, KpiThresholdLevel.Medium)]
    [InlineData(10, KpiThresholdLevel.High)]
    [InlineData(int.MaxValue, KpiThresholdLevel.High)]
    public void LevelFor_bands_counts_at_boundaries(int count, KpiThresholdLevel expected)
    {
        Assert.Equal(expected, KpiThresholdService.LevelFor(count, Fallback));
    }

    [Fact]
    public void Configuration_changes_are_picked_up_without_restart()
    {
        var cfg = new ActiveRolesConfig();
        var monitor = new TestOptionsMonitor<ActiveRolesConfig>(cfg);
        var service = new KpiThresholdService(monitor);
        Assert.Null(service.GetConfigured("K"));

        var updated = new ActiveRolesConfig();
        updated.KpiThresholds["K"] = new KpiThresholdConfig { Medium = 1, High = 2 };
        monitor.CurrentValue = updated;

        Assert.NotNull(service.GetConfigured("K"));
    }
}
