using System.Security.Cryptography;
using ActiveRolesDashboard.Services;
using Microsoft.AspNetCore.DataProtection;

namespace ActiveRolesDashboard.Tests.Login;

[Trait("Stage", "Login")]
public class ServiceAccountSecretProtectorTests
{
    private static ServiceAccountSecretProtector Create(IDataProtectionProvider? provider = null) =>
        new(provider ?? new EphemeralDataProtectionProvider());

    [Theory]
    [InlineData("P@ssw0rd!")]
    [InlineData("x")]
    [InlineData("ünïcødé 密码 🔐")]
    public void Round_trips_secret_without_exposing_plaintext(string secret)
    {
        var protector = Create();
        var payload = protector.Protect(secret);

        if (secret.Length > 3) Assert.DoesNotContain(secret, payload);
        Assert.Equal(secret, protector.Unprotect(payload));
    }

    [Fact]
    public void Long_secret_round_trips()
    {
        var secret = new string('a', 10_000);
        var protector = Create();
        Assert.Equal(secret, protector.Unprotect(protector.Protect(secret)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Protect_rejects_empty(string? secret)
    {
        Assert.Throws<ArgumentException>(() => Create().Protect(secret!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Unprotect_reports_missing_configuration(string? payload)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Create().Unprotect(payload!));
        Assert.Contains("--protect-secret", ex.Message);
    }

    [Fact]
    public void Tampered_payload_is_rejected()
    {
        var protector = Create();
        var payload = protector.Protect("secret");
        var tampered = payload[..^2] + (payload[^2] == 'A' ? "B" : "A") + payload[^1];

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(tampered));
    }

    [Fact]
    public void Payload_from_different_key_ring_is_rejected()
    {
        var payload = Create().Protect("secret");
        Assert.ThrowsAny<CryptographicException>(() => Create().Unprotect(payload));
    }

    [Fact]
    public void Payload_with_different_purpose_is_rejected()
    {
        var provider = new EphemeralDataProtectionProvider();
        var foreign = provider.CreateProtector("Other.Purpose").Protect("secret");
        Assert.ThrowsAny<CryptographicException>(() => Create(provider).Unprotect(foreign));
    }
}
