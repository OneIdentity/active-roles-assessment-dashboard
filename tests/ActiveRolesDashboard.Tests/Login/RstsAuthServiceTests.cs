using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Tests.Login;

[Trait("Stage", "Login")]
public class RstsAuthServiceTests : IDisposable
{
    private readonly FakeActiveRolesServer _server = new();
    private readonly TestHttpClientFactory _factory = new(TimeSpan.FromSeconds(2));

    private RstsAuthService Create(string? rstsUrl = null, string? scope = null) =>
        new(new TestOptionsMonitor<ActiveRolesConfig>(new ActiveRolesConfig { RstsUrl = rstsUrl ?? _server.RstsUrl, Resource = "urn:test", RstsScope = scope! }), _factory);

    [Fact]
    public async Task Valid_credentials_return_token()
    {
        _server.WithTokenEndpoint(expiresIn: 1800);

        var result = await Create().GetTokenAsync(FakeActiveRolesServer.ValidUser, FakeActiveRolesServer.ValidPassword);

        Assert.True(result.Success);
        Assert.Equal(FakeActiveRolesServer.ValidToken, result.AccessToken);
        Assert.Equal(1800, result.ExpiresIn);
        Assert.Null(result.Error);
        Assert.Contains("RSTS", _factory.RequestedNames);
    }

    [Theory]
    [InlineData(@"TEST\admin", "wrong")]
    [InlineData(@"TEST\nobody", "P@ssw0rd!")]
    [InlineData("", "")]
    public async Task Invalid_credentials_fail_without_token(string user, string password)
    {
        _server.WithTokenEndpoint();

        var result = await Create().GetTokenAsync(user, password);

        Assert.False(result.Success);
        Assert.Empty(result.AccessToken);
        Assert.StartsWith("Authentication failed", result.Error);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(400)]
    public async Task Server_errors_are_reported(int status)
    {
        _server.WithTokenEndpointStatus(status, "{\"error\":\"x\"}");

        var result = await Create().GetTokenAsync(FakeActiveRolesServer.ValidUser, FakeActiveRolesServer.ValidPassword);

        Assert.False(result.Success);
        Assert.Contains("Authentication failed", result.Error);
    }

    [Fact]
    public async Task Malformed_success_body_is_reported_as_connection_error()
    {
        _server.WithTokenEndpointStatus(200, "not json");

        var result = await Create().GetTokenAsync(FakeActiveRolesServer.ValidUser, FakeActiveRolesServer.ValidPassword);

        Assert.False(result.Success);
        Assert.StartsWith("Connection error", result.Error);
    }

    [Fact]
    public async Task Unreachable_server_is_reported_as_connection_error()
    {
        var result = await Create("http://127.0.0.1:1/RSTS/oauth2/token").GetTokenAsync("u", "p");

        Assert.False(result.Success);
        Assert.StartsWith("Connection error", result.Error);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("   ", false)]
    [InlineData(" provider-x ", true)]
    public async Task Scope_is_sent_only_when_configured(string? scope, bool expected)
    {
        _server.WithTokenEndpoint();

        await Create(scope: scope).GetTokenAsync(FakeActiveRolesServer.ValidUser, FakeActiveRolesServer.ValidPassword);

        var body = await LastRequestBodyAsync();
        Assert.Equal(expected, body.Contains("scope=provider-x"));
        Assert.Contains("grant_type=password", body);
        Assert.Contains("resource=urn%3Atest", body);
    }

    private async Task<string> LastRequestBodyAsync()
    {
        for (var i = 0; i < 50 && _server.Requests.Count == 0; i++)
            await Task.Delay(20);
        return _server.Requests.LastOrDefault()?.RequestMessage?.Body ?? string.Empty;
    }

    public void Dispose() => _server.Dispose();
}
