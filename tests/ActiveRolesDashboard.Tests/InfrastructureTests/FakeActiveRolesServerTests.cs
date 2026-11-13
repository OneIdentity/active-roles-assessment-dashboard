using System.Net;
using System.Text.Json.Nodes;
using ActiveRolesDashboard.Tests.Infrastructure;

namespace ActiveRolesDashboard.Tests.InfrastructureTests;

/// <summary>Verifies the fake Active Roles server behaves as the dashboard expects.</summary>
[Trait("Category", "Infrastructure")]
public class FakeActiveRolesServerTests : IDisposable
{
    private readonly FakeActiveRolesServer _server = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    [Fact]
    public async Task Token_endpoint_accepts_valid_credentials()
    {
        _server.WithTokenEndpoint();

        var response = await _http.PostAsync(_server.RstsUrl, TokenForm(FakeActiveRolesServer.ValidUser, FakeActiveRolesServer.ValidPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal(FakeActiveRolesServer.ValidToken, body["access_token"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(@"TEST\admin", "wrong")]
    [InlineData(@"TEST\nobody", "P@ssw0rd!")]
    [InlineData("", "")]
    public async Task Token_endpoint_rejects_invalid_credentials(string user, string password)
    {
        _server.WithTokenEndpoint();

        var response = await _http.PostAsync(_server.RstsUrl, TokenForm(user, password));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Search_pages_results_using_nextPage_only()
    {
        var items = Enumerable.Range(1, 5).Select(i => (JsonNode)new JsonObject { ["name"] = $"u{i}" }).ToList();
        _server.WithSearch("objectCategory=person", items, pageSize: 2);

        var names = new List<string>();
        var url = $"{_server.ApiBaseUrl}/objects?base=DC%3Dtest&filter={Uri.EscapeDataString("(&(objectCategory=person))")}&scope=sub&pageSize=2";
        var pages = 0;
        while (url != null)
        {
            pages++;
            var doc = JsonNode.Parse(await _http.GetStringAsync(url))!;
            names.AddRange(doc["items"]!.AsArray().Select(i => i!["name"]!.GetValue<string>()));
            url = doc["nextPage"] is JsonNode next ? $"{_server.ApiBaseUrl}/objects?nextPage={next.GetValue<string>()}" : null;
        }

        Assert.Equal(3, pages);
        Assert.Equal(["u1", "u2", "u3", "u4", "u5"], names);
    }

    [Fact]
    public async Task Search_error_returns_configured_status()
    {
        _server.WithEmptySearchDefault().WithSearchError("objectClass=group", 500);

        var response = await _http.GetAsync($"{_server.ApiBaseUrl}/objects?filter={Uri.EscapeDataString("(objectClass=group)")}");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Unmatched_search_returns_empty_items()
    {
        _server.WithEmptySearchDefault();

        var doc = JsonNode.Parse(await _http.GetStringAsync($"{_server.ApiBaseUrl}/objects?filter=x"))!;

        Assert.Empty(doc["items"]!.AsArray());
    }

    [Theory]
    [InlineData("small-domain")]
    [InlineData("edge-cases")]
    [InlineData("empty-domain")]
    public void Data_sets_load(string name)
    {
        var set = TestDataSet.Load(name);

        Assert.False(string.IsNullOrWhiteSpace(set.Description));
    }

    [Fact]
    public void Missing_data_set_throws()
        => Assert.Throws<FileNotFoundException>(() => TestDataSet.Load("does-not-exist"));

    private static FormUrlEncodedContent TokenForm(string user, string password) => new(new Dictionary<string, string>
    {
        ["grant_type"] = "password",
        ["username"] = user,
        ["password"] = password,
        ["resource"] = "urn:test"
    });

    public void Dispose()
    {
        _http.Dispose();
        _server.Dispose();
    }
}
