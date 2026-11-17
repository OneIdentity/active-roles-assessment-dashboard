namespace ActiveRolesDashboard.Tests;

[Trait("Stage", "ServiceStart")]
public class SmokeTests
{
    [Fact]
    public void Dashboard_assembly_is_referenced()
    {
        var assembly = typeof(Program).Assembly;

        Assert.Equal("ActiveRolesDashboard", assembly.GetName().Name);
    }
}
