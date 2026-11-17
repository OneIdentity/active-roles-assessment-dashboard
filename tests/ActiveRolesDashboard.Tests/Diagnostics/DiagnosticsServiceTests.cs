using System.Text.Json.Nodes;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace ActiveRolesDashboard.Tests.Diagnostics;

/// <summary>
/// Exercises <see cref="DiagnosticsService.RunAsync"/> through deterministic paths only: request
/// filtering, result ordering, HTTP status classification, RSTS authentication, REST GET (server and
/// Entra tenant), configuration skips and error handling. No real ICMP/LDAP/ADSI/SQL traffic is made.
/// </summary>
[Trait("Stage", "Diagnostics")]
public class DiagnosticsServiceTests : IDisposable
{
    private const string TenantGuid = "99999999-8888-7777-6666-555555555555";

    private readonly FakeActiveRolesServer _server = new FakeActiveRolesServer().WithHealthyDefaults();
    private readonly ServiceAccountSecretProtector _protector = new(new EphemeralDataProtectionProvider());

    private DiagnosticsService Create(bool withServiceAccount = true, string? protectedPassword = null, string? apiBaseUrl = null)
    {
        var config = new TestOptionsMonitor<ActiveRolesConfig>(new ActiveRolesConfig
        {
            ApiBaseUrl = apiBaseUrl ?? _server.ApiBaseUrl,
            RstsUrl = _server.RstsUrl,
            ServiceAccount = withServiceAccount
                ? new ServiceAccountConfig
                {
                    Username = FakeActiveRolesServer.ValidUser,
                    ProtectedPassword = protectedPassword ?? _protector.Protect(FakeActiveRolesServer.ValidPassword)
                }
                : new ServiceAccountConfig()
        });
        var factory = new TestHttpClientFactory(TimeSpan.FromSeconds(10));
        var rsts = new RstsAuthService(config, factory);
        var tokens = new ServiceAccountTokenProvider(rsts, _protector, config, NullLogger<ServiceAccountTokenProvider>.Instance);
        return new DiagnosticsService(factory, rsts, tokens, _protector, config, NullLogger<DiagnosticsService>.Instance);
    }

    private static DiagnosticsTarget Target(string id, DiagnosticsServerType type, string url = "", string host = "", string dn = "", params DiagnosticsTestType[] tests)
    {
        var t = new DiagnosticsTarget { Id = id, Name = id, ServerType = type, Url = url, Host = host, Dn = dn };
        t.ApplicableTests.AddRange(tests);
        return t;
    }

    private void WithPath(string path, int status, TimeSpan? delay = null)
    {
        var response = Response.Create().WithStatusCode(status).WithBody("ok");
        if (delay.HasValue) response = response.WithDelay(delay.Value);
        _server.Server.Given(Request.Create().WithPath(path).UsingGet()).RespondWith(response);
    }

    private static Task<DiagnosticsResult> Run(DiagnosticsService service, DiagnosticsRequest request, params DiagnosticsTarget[] targets)
        => service.RunAsync(request, targets);

    // ------------------------------------------------------------------ filtering / aggregation

    [Fact]
    public async Task Empty_target_list_produces_empty_result()
    {
        var result = await Run(Create(), new DiagnosticsRequest { Dashboard = DiagnosticsDashboard.Exchange });

        Assert.Equal(DiagnosticsDashboard.Exchange, result.Dashboard);
        Assert.Empty(result.Probes);
        Assert.True(result.CompletedUtc >= result.StartedUtc);
    }

    [Fact]
    public async Task Filters_by_server_type_test_type_and_target_ids()
    {
        WithPath("/web", 200);
        var web = Target("web-a", DiagnosticsServerType.WebInterface, $"{_server.BaseUrl}/web", tests: [DiagnosticsTestType.WebHttp, DiagnosticsTestType.Ping]);
        var web2 = Target("web-b", DiagnosticsServerType.WebInterface, $"{_server.BaseUrl}/web", tests: [DiagnosticsTestType.WebHttp]);
        var sql = Target("sql", DiagnosticsServerType.SqlServer, tests: [DiagnosticsTestType.SqlQuery]);

        var byServer = await Run(Create(), new DiagnosticsRequest { ServerType = DiagnosticsServerType.SqlServer }, web, web2, sql);
        Assert.Equal("sql", Assert.Single(byServer.Probes).TargetId);

        var byTest = await Run(Create(), new DiagnosticsRequest { TestType = DiagnosticsTestType.WebHttp }, web, web2, sql);
        Assert.Equal(2, byTest.Probes.Count);
        Assert.All(byTest.Probes, p => Assert.Equal(DiagnosticsTestType.WebHttp, p.TestType));

        var byId = await Run(Create(), new DiagnosticsRequest { TargetIds = ["web-b"] }, web, web2, sql);
        Assert.Equal("web-b", Assert.Single(byId.Probes).TargetId);
    }

    [Fact]
    public async Task Results_are_ordered_by_target_then_test_and_counted()
    {
        WithPath("/ok", 200);
        WithPath("/err", 503);
        var targets = new[]
        {
            Target("zeta", DiagnosticsServerType.WebInterface, $"{_server.BaseUrl}/err", tests: [DiagnosticsTestType.WebHttp]),
            Target("alpha", DiagnosticsServerType.SqlServer, tests: [DiagnosticsTestType.SqlQuery]),
            Target("mid", DiagnosticsServerType.EntraId, $"{_server.BaseUrl}/ok", tests: [DiagnosticsTestType.EntraOpenId]),
            Target("mid2", DiagnosticsServerType.WebInterface, "http://127.0.0.1:1/unreachable", tests: [DiagnosticsTestType.WebHttp]),
        };

        var result = await Run(Create(), new DiagnosticsRequest(), targets);

        Assert.Equal(["alpha", "mid", "mid2", "zeta"], result.Probes.Select(p => p.TargetName));
        Assert.Equal(1, result.OkCount);
        Assert.Equal(1, result.WarnCount);
        Assert.Equal(1, result.FailCount);
        Assert.Equal(DiagnosticsStatus.Skipped, result.Probes[0].Status);
    }

    [Fact]
    public async Task Unsupported_test_type_is_skipped()
    {
        var target = Target("x", DiagnosticsServerType.WebInterface, tests: [(DiagnosticsTestType)999]);

        var probe = Assert.Single((await Run(Create(), new DiagnosticsRequest(), target)).Probes);

        Assert.Equal(DiagnosticsStatus.Skipped, probe.Status);
        Assert.Equal("Unsupported test type.", probe.Message);
    }

    [Fact]
    public async Task Caller_cancellation_is_honoured()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var target = Target("x", DiagnosticsServerType.WebInterface, tests: [DiagnosticsTestType.WebHttp]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Create().RunAsync(new DiagnosticsRequest(), [target], cts.Token));
    }

    // ------------------------------------------------------------------ HTTP probes

    [Theory]
    [InlineData(200, DiagnosticsStatus.Ok)]
    [InlineData(401, DiagnosticsStatus.Ok)]
    [InlineData(404, DiagnosticsStatus.Ok)]
    [InlineData(500, DiagnosticsStatus.Warn)]
    [InlineData(503, DiagnosticsStatus.Warn)]
    public async Task Http_status_is_classified_for_reachability(int status, DiagnosticsStatus expected)
    {
        WithPath("/site", status);
        var target = Target("web", DiagnosticsServerType.WebInterface, $"{_server.BaseUrl}/site", tests: [DiagnosticsTestType.WebHttp]);

        var probe = Assert.Single((await Run(Create(), new DiagnosticsRequest(), target)).Probes);

        Assert.Equal(expected, probe.Status);
        Assert.NotNull(probe.LatencyMs);
        Assert.Contains($"HTTP {status}", probe.Message);
    }

    [Fact]
    public async Task Entra_openid_probe_reports_ok()
    {
        WithPath("/tenant/v2.0/.well-known/openid-configuration", 200);
        var target = Target("tenant", DiagnosticsServerType.EntraId,
            $"{_server.BaseUrl}/tenant/v2.0/.well-known/openid-configuration", tests: [DiagnosticsTestType.EntraOpenId]);

        var probe = Assert.Single((await Run(Create(), new DiagnosticsRequest(), target)).Probes);

        Assert.Equal(DiagnosticsStatus.Ok, probe.Status);
    }

    [Theory]
    [InlineData(DiagnosticsTestType.WebHttp)]
    [InlineData(DiagnosticsTestType.EntraOpenId)]
    public async Task Http_probe_without_url_is_skipped(DiagnosticsTestType test)
    {
        var target = Target("x", DiagnosticsServerType.WebInterface, tests: [test]);

        var probe = Assert.Single((await Run(Create(), new DiagnosticsRequest(), target)).Probes);

        Assert.Equal(DiagnosticsStatus.Skipped, probe.Status);
    }

    [Fact]
    public async Task Unreachable_http_endpoint_fails()
    {
        var target = Target("x", DiagnosticsServerType.WebInterface, "http://127.0.0.1:1/", tests: [DiagnosticsTestType.WebHttp]);

        var probe = Assert.Single((await Run(Create(), new DiagnosticsRequest(), target)).Probes);

        Assert.Equal(DiagnosticsStatus.Fail, probe.Status);
        Assert.False(string.IsNullOrWhiteSpace(probe.Message));
    }

    // ------------------------------------------------------------------ RSTS auth

    [Fact]
    public async Task Rsts_auth_succeeds_with_valid_service_account()
    {
        var target = Target("rsts", DiagnosticsServerType.Rsts, tests: [DiagnosticsTestType.RstsAuth]);

        var probe = Assert.Single((await Run(Create(), new DiagnosticsRequest(), target)).Probes);

        Assert.Equal(DiagnosticsStatus.Ok, probe.Status);
        Assert.NotNull(probe.LatencyMs);
    }

    [Fact]
    public async Task Rsts_auth_fails_with_wrong_password()
    {
        var target = Target("rsts", DiagnosticsServerType.Rsts, tests: [DiagnosticsTestType.RstsAuth]);

        var probe = Assert.Single((await Run(Create(protectedPassword: _protector.Protect("wrong")), new DiagnosticsRequest(), target)).Probes);

        Assert.Equal(DiagnosticsStatus.Fail, probe.Status);
        Assert.StartsWith("RSTS authentication failed", probe.Message);
    }

    [Theory]
    [InlineData(DiagnosticsTestType.RstsAuth)]
    [InlineData(DiagnosticsTestType.LdapBind)]
    public async Task Service_account_probes_skip_when_not_configured(DiagnosticsTestType test)
    {
        var target = Target("x", DiagnosticsServerType.DomainController, host: "dc01", tests: [test]);

        var probe = Assert.Single((await Run(Create(withServiceAccount: false), new DiagnosticsRequest(), target)).Probes);

        Assert.Equal(DiagnosticsStatus.Skipped, probe.Status);
        Assert.Equal("Service account is not configured.", probe.Message);
    }

    [Theory]
    [InlineData(DiagnosticsTestType.RstsAuth)]
    [InlineData(DiagnosticsTestType.LdapBind)]
    public async Task Service_account_probes_fail_when_password_unreadable(DiagnosticsTestType test)
    {
        var target = Target("x", DiagnosticsServerType.DomainController, host: "dc01", tests: [test]);

        var probe = Assert.Single((await Run(Create(protectedPassword: "tampered-payload"), new DiagnosticsRequest(), target)).Probes);

        Assert.Equal(DiagnosticsStatus.Fail, probe.Status);
        Assert.StartsWith("Could not read service-account password", probe.Message);
    }

    // ------------------------------------------------------------------ REST GET

    [Fact]
    public async Task Rest_get_hits_containers_on_target_host_with_bearer_token()
    {
        WithPath("/api/containers", 200);
        var target = Target("ars", DiagnosticsServerType.ActiveRolesServer, host: "localhost", tests: [DiagnosticsTestType.RestGet]);

        var probe = Assert.Single((await Run(Create(), new DiagnosticsRequest(), target)).Probes);

        Assert.Equal(DiagnosticsStatus.Ok, probe.Status);
        var request = _server.Requests.Single(r => r.RequestMessage.Path == "/api/containers").RequestMessage;
        Assert.Equal($"Bearer {FakeActiveRolesServer.ValidToken}", request.Headers!["Authorization"].Single());
    }

    [Fact]
    public async Task Rest_get_non_success_status_fails()
    {
        WithPath("/api/containers", 403);
        var target = Target("ars", DiagnosticsServerType.ActiveRolesServer, host: "localhost", tests: [DiagnosticsTestType.RestGet]);

        var probe = Assert.Single((await Run(Create(), new DiagnosticsRequest(), target)).Probes);

        Assert.Equal(DiagnosticsStatus.Fail, probe.Status);
        Assert.Contains("403", probe.Message);
    }

    [Fact]
    public async Task Rest_get_fails_when_token_cannot_be_acquired()
    {
        var target = Target("ars", DiagnosticsServerType.ActiveRolesServer, host: "localhost", tests: [DiagnosticsTestType.RestGet]);

        var probe = Assert.Single((await Run(Create(withServiceAccount: false), new DiagnosticsRequest(), target)).Probes);

        Assert.Equal(DiagnosticsStatus.Fail, probe.Status);
        Assert.StartsWith("Could not acquire service-account token", probe.Message);
    }

    [Theory]
    [InlineData("objectGUID")]
    [InlineData("ObjectGuid")]
    [InlineData("id")]
    public async Task Rest_get_resolves_entra_tenant_guid_then_gets_object(string guidProperty)
    {
        _server.WithSearch("edsAzureTenantcontainer",
            [new JsonObject { [guidProperty] = new JsonArray($"{{{TenantGuid}}}") }], priority: 10);
        WithPath($"/api/objects/{TenantGuid}", 200);
        var target = Target("contoso", DiagnosticsServerType.EntraId, dn: "contoso.onmicrosoft.com", tests: [DiagnosticsTestType.RestGet]);

        var probe = Assert.Single((await Run(Create(), new DiagnosticsRequest(), target)).Probes);

        Assert.Equal(DiagnosticsStatus.Ok, probe.Status);
        Assert.Contains(_server.Requests, r => r.RequestMessage.Path == $"/api/objects/{TenantGuid}");
    }

    [Fact]
    public async Task Rest_get_reads_entra_guid_nested_under_attributes()
    {
        _server.WithSearch("edsAzureTenantcontainer",
            [new JsonObject { ["attributes"] = new JsonObject { ["objectGUID"] = TenantGuid } }], priority: 10);
        WithPath($"/api/objects/{TenantGuid}", 200);
        var target = Target("contoso", DiagnosticsServerType.EntraId, tests: [DiagnosticsTestType.RestGet]);

        var probe = Assert.Single((await Run(Create(), new DiagnosticsRequest(), target)).Probes);

        Assert.Equal(DiagnosticsStatus.Ok, probe.Status);
    }

    [Fact]
    public async Task Rest_get_fails_when_entra_tenant_not_found()
    {
        var target = Target("contoso", DiagnosticsServerType.EntraId, tests: [DiagnosticsTestType.RestGet]);

        var probe = Assert.Single((await Run(Create(), new DiagnosticsRequest(), target)).Probes);

        Assert.Equal(DiagnosticsStatus.Fail, probe.Status);
        Assert.Contains("contoso", probe.Message);
    }

    [Fact]
    public async Task Rest_get_fails_when_entra_tenant_lookup_errors()
    {
        _server.WithSearchError("edsAzureTenantcontainer", 500, priority: 10);
        var target = Target("contoso", DiagnosticsServerType.EntraId, tests: [DiagnosticsTestType.RestGet]);

        var probe = Assert.Single((await Run(Create(), new DiagnosticsRequest(), target)).Probes);

        Assert.Equal(DiagnosticsStatus.Fail, probe.Status);
        Assert.StartsWith("Tenant lookup returned 500", probe.Message);
    }

    [Fact]
    public async Task Rest_get_skips_entra_without_api_base_url()
    {
        var service = Create(apiBaseUrl: "");
        var target = Target("contoso", DiagnosticsServerType.EntraId, tests: [DiagnosticsTestType.RestGet]);

        // Token acquisition does not use the API base URL, so the probe reaches the Entra branch.
        var probe = Assert.Single((await Run(service, new DiagnosticsRequest(), target)).Probes);

        Assert.Equal(DiagnosticsStatus.Skipped, probe.Status);
    }

    // ------------------------------------------------------------------ SQL / LDAP configuration

    [Theory]
    [InlineData("sqlhost", "")]
    [InlineData("", "")]
    public async Task Sql_probe_skips_without_server_or_database(string host, string db)
    {
        var target = new DiagnosticsTarget { Id = "sql", Name = host, Host = host, Dn = db, ServerType = DiagnosticsServerType.SqlServer };
        target.ApplicableTests.Add(DiagnosticsTestType.SqlQuery);

        var probe = Assert.Single((await Run(Create(), new DiagnosticsRequest(), target)).Probes);

        Assert.Equal(DiagnosticsStatus.Skipped, probe.Status);
    }

    public void Dispose() => _server.Dispose();
}
