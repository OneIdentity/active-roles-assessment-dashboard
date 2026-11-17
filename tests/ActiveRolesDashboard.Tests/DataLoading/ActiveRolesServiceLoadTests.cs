using System.Text.Json.Nodes;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActiveRolesDashboard.Tests.DataLoading;

/// <summary>
/// Drives the real <see cref="ActiveRolesService"/> collection methods against the fake Active Roles
/// server: dataset mapping, nextPage paging, empty directories, HTTP failures and malformed payloads.
/// </summary>
[Trait("Stage", "DataLoad")]
public class ActiveRolesServiceLoadTests : IDisposable
{
    private const string UserFilter = "objectCategory=person";
    private const string ComputerFilter = "objectClass=computer";

    private readonly FakeActiveRolesServer _server = new FakeActiveRolesServer().WithHealthyDefaults();

    private ActiveRolesService Create()
    {
        var config = new TestOptionsMonitor<ActiveRolesConfig>(new ActiveRolesConfig
        {
            ApiBaseUrl = _server.ApiBaseUrl,
            RstsUrl = _server.RstsUrl,
        });
        var factory = new TestHttpClientFactory(TimeSpan.FromSeconds(5));
        var tokens = new ServiceAccountTokenProvider(
            new RstsAuthService(config, factory),
            new ServiceAccountSecretProtector(new EphemeralDataProtectionProvider()),
            config,
            NullLogger<ServiceAccountTokenProvider>.Instance);
        return new ActiveRolesService(factory, config, NullLogger<ActiveRolesService>.Instance, tokens);
    }

    private static int DataSetCount(string dataSet, string filter)
        => TestDataSet.Load(dataSet).Searches.Single(s => s.FilterContains == filter).Items.Count;

    private static List<JsonNode> Users(int count) => Enumerable.Range(1, count)
        .Select(i => (JsonNode)new JsonObject
        {
            ["objectGUID"] = $"aaaaaaaa-0000-0000-0000-{i:D12}",
            ["name"] = $"User {i}",
            ["distinguishedName"] = $"CN=User {i},OU=Users,DC=test,DC=local",
            ["edsaDomainNetbiosName"] = "TEST",
            ["userAccountControl"] = 512,
        })
        .ToList();

    private int SearchRequestCount() => _server.Requests.Count(r => r.RequestMessage.Path == "/api/objects");

    // ------------------------------------------------------------------ mapping

    [Fact]
    public async Task Small_domain_users_are_loaded_without_error()
    {
        _server.WithDataSet("small-domain");

        var result = await Create().GetADUserAccountsCountAsync(FakeActiveRolesServer.ValidToken);

        Assert.Null(result.Error);
        Assert.Equal(DataSetCount("small-domain", UserFilter), result.TotalCount);
        Assert.Equal(result.TotalCount, result.Items.Count);
    }

    [Fact]
    public async Task Requests_carry_bearer_token_and_repeated_attribute_parameters()
    {
        _server.WithDataSet("small-domain");

        await Create().GetComputersAsync(FakeActiveRolesServer.ValidToken);

        var request = _server.Requests.Single(r => r.RequestMessage.Path == "/api/objects").RequestMessage;
        Assert.Equal($"Bearer {FakeActiveRolesServer.ValidToken}", request.Headers!["Authorization"].Single());
        Assert.True(request.Query!["attributes"].Count > 1);
        Assert.DoesNotContain(request.Query["attributes"], a => a.Contains(','));
    }

    [Fact]
    public async Task Computers_total_excludes_domain_controllers()
    {
        _server.WithSearch(ComputerFilter,
        [
            new JsonObject { ["name"] = "WS01", ["userAccountControl"] = 4096 },
            new JsonObject { ["name"] = "DC01", ["userAccountControl"] = 532480 },
            new JsonObject { ["name"] = "NOUAC" },
        ]);

        var result = await Create().GetComputersAsync(FakeActiveRolesServer.ValidToken);

        Assert.Null(result.Error);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task Edge_case_values_do_not_break_user_load()
    {
        _server.WithDataSet("edge-cases");

        var result = await Create().GetADUserAccountsCountAsync(FakeActiveRolesServer.ValidToken);

        Assert.Null(result.Error);
        Assert.Equal(DataSetCount("edge-cases", UserFilter), result.TotalCount);
    }

    // ------------------------------------------------------------------ empty

    [Fact]
    public async Task Empty_domain_returns_zero_counts_without_error()
    {
        _server.WithDataSet("empty-domain");
        var service = Create();

        var users = await service.GetADUserAccountsCountAsync(FakeActiveRolesServer.ValidToken);
        var groups = await service.GetADGroupsAsync(FakeActiveRolesServer.ValidToken);
        var computers = await service.GetComputersAsync(FakeActiveRolesServer.ValidToken);

        Assert.Null(users.Error);
        Assert.Null(groups.Error);
        Assert.Null(computers.Error);
        Assert.Equal(0, users.TotalCount);
        Assert.Empty(groups.Items);
        Assert.Equal(0, computers.TotalCount);
    }

    // ------------------------------------------------------------------ paging

    [Theory]
    [InlineData(7, 3, 3)]
    [InlineData(6, 3, 2)]
    [InlineData(1, 1, 1)]
    [InlineData(25, 1, 25)]
    public async Task All_pages_are_followed_and_accumulated(int total, int pageSize, int expectedRequests)
    {
        _server.WithSearch(UserFilter, Users(total), pageSize);

        var result = await Create().GetADUserAccountsCountAsync(FakeActiveRolesServer.ValidToken);

        Assert.Null(result.Error);
        Assert.Equal(total, result.TotalCount);
        Assert.Equal(total, result.Items.Select(i => i.GetProperty("objectGUID").GetString()).Distinct().Count());
        Assert.Equal(expectedRequests, SearchRequestCount());
    }

    [Fact]
    public async Task Continuation_requests_send_only_next_page()
    {
        _server.WithSearch(UserFilter, Users(4), pageSize: 2);

        await Create().GetADUserAccountsCountAsync(FakeActiveRolesServer.ValidToken);

        var continuation = _server.Requests
            .Select(r => r.RequestMessage)
            .Single(r => r.Path == "/api/objects" && r.Query!.ContainsKey("nextPage"));
        Assert.Equal(["nextPage"], continuation.Query!.Keys);
    }

    // ------------------------------------------------------------------ failures

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Http_failure_is_reported_on_component(int status)
    {
        _server.WithSearchError(ComputerFilter, status);

        var result = await Create().GetComputersAsync(FakeActiveRolesServer.ValidToken);

        Assert.NotNull(result.Error);
        Assert.StartsWith("No data (HttpRequestException", result.Error);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"items\":[")]
    public async Task Malformed_payload_is_reported_on_component(string body)
    {
        _server.WithSearchRawBody(ComputerFilter, body);

        var result = await Create().GetComputersAsync(FakeActiveRolesServer.ValidToken);

        Assert.NotNull(result.Error);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task Unexpected_payload_shape_yields_empty_result()
    {
        _server.WithSearchRawBody(ComputerFilter, "{\"value\":[{\"name\":\"X\"}]}");

        var result = await Create().GetComputersAsync(FakeActiveRolesServer.ValidToken);

        Assert.Null(result.Error);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task One_failing_component_does_not_affect_others()
    {
        _server.WithDataSet("small-domain").WithSearchError(ComputerFilter, 500);
        var service = Create();

        var computers = await service.GetComputersAsync(FakeActiveRolesServer.ValidToken);
        var users = await service.GetADUserAccountsCountAsync(FakeActiveRolesServer.ValidToken);

        Assert.NotNull(computers.Error);
        Assert.Null(users.Error);
        Assert.Equal(DataSetCount("small-domain", UserFilter), users.TotalCount);
    }

    [Fact]
    public async Task Unreachable_api_is_reported_on_component()
    {
        _server.Dispose();

        var result = await Create().GetComputersAsync(FakeActiveRolesServer.ValidToken);

        Assert.NotNull(result.Error);
        Assert.Equal(0, result.TotalCount);
    }

    public void Dispose() => _server.Dispose();
}
