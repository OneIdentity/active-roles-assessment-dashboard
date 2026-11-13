using System.Text.Json;
using System.Text.Json.Nodes;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace ActiveRolesDashboard.Tests.Infrastructure;

/// <summary>
/// In-process fake of the Active Roles REST API and RSTS token endpoint.
/// Tests configure responses per scenario: valid data, paging, HTTP errors, delays and malformed bodies.
/// </summary>
public sealed class FakeActiveRolesServer : IDisposable
{
    public const string ValidToken = "test-access-token";
    public const string ValidUser = @"TEST\admin";
    public const string ValidPassword = "P@ssw0rd!";

    private readonly WireMockServer _server;

    public FakeActiveRolesServer()
    {
        _server = WireMockServer.Start();
    }

    public string BaseUrl => _server.Urls[0];
    public string ApiBaseUrl => $"{BaseUrl}/api";
    public string RstsUrl => $"{BaseUrl}/RSTS/oauth2/token";

    public WireMockServer Server => _server;

    /// <summary>All requests received, for asserting on the exact query sent.</summary>
    public IReadOnlyList<WireMock.Logging.ILogEntry> Requests => _server.LogEntries.ToList();

    public void Reset() => _server.Reset();

    // ------------------------------------------------------------------ RSTS

    /// <summary>Accepts <see cref="ValidUser"/>/<see cref="ValidPassword"/>; anything else is 401.</summary>
    public FakeActiveRolesServer WithTokenEndpoint(int expiresIn = 3600)
    {
        _server.Given(Request.Create().WithPath("/RSTS/oauth2/token").UsingPost())
            .AtPriority(10)
            .RespondWith(Response.Create().WithStatusCode(401).WithBody("{\"error\":\"invalid_grant\"}"));

        _server.Given(Request.Create().WithPath("/RSTS/oauth2/token").UsingPost()
                .WithBody((string? body) => IsValidTokenRequest(body)))
            .AtPriority(1)
            .RespondWith(Json(200, new JsonObject { ["access_token"] = ValidToken, ["expires_in"] = expiresIn, ["token_type"] = "Bearer" }));
        return this;
    }

    public FakeActiveRolesServer WithTokenEndpointStatus(int statusCode, string body = "")
    {
        _server.Given(Request.Create().WithPath("/RSTS/oauth2/token").UsingPost())
            .AtPriority(0)
            .RespondWith(Response.Create().WithStatusCode(statusCode).WithBody(body));
        return this;
    }

    // ------------------------------------------------------------------ API

    /// <summary>Connectivity preflight (GET on the API base URL).</summary>
    public FakeActiveRolesServer WithApiRoot(int statusCode = 200)
    {
        _server.Given(Request.Create().WithPath("/api", "/api/").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(statusCode).WithBody("{}"));
        return this;
    }

    /// <summary>Default for any unconfigured search: an empty result set.</summary>
    public FakeActiveRolesServer WithEmptySearchDefault()
    {
        _server.Given(Request.Create().WithPath("/api/objects").UsingGet())
            .AtPriority(100)
            .RespondWith(Json(200, new JsonObject { ["items"] = new JsonArray() }));
        return this;
    }

    /// <summary>
    /// Serves <paramref name="items"/> for searches whose filter contains <paramref name="filterContains"/>,
    /// split into pages of <paramref name="pageSize"/> using nextPage continuation tokens.
    /// </summary>
    public FakeActiveRolesServer WithSearch(string filterContains, IReadOnlyList<JsonNode> items, int pageSize = 0, int priority = 50)
    {
        var pages = Paginate(items, pageSize);
        var tokenPrefix = "p" + Guid.NewGuid().ToString("N")[..8];

        _server.Given(Request.Create().WithPath("/api/objects").UsingGet()
                .WithParam("filter", new WildcardMatcher($"*{filterContains}*", ignoreCase: true)))
            .AtPriority(priority)
            .RespondWith(Json(200, PageBody(pages, 0, tokenPrefix)));

        for (var i = 1; i < pages.Count; i++)
        {
            _server.Given(Request.Create().WithPath("/api/objects").UsingGet()
                    .WithParam("nextPage", $"{tokenPrefix}-{i}"))
                .AtPriority(priority)
                .RespondWith(Json(200, PageBody(pages, i, tokenPrefix)));
        }
        return this;
    }

    /// <summary>Searches matching <paramref name="filterContains"/> fail with the given status/body.</summary>
    public FakeActiveRolesServer WithSearchError(string filterContains, int statusCode, string body = "{\"message\":\"error\"}", int priority = 40)
    {
        _server.Given(Request.Create().WithPath("/api/objects").UsingGet()
                .WithParam("filter", new WildcardMatcher($"*{filterContains}*", ignoreCase: true)))
            .AtPriority(priority)
            .RespondWith(Response.Create().WithStatusCode(statusCode).WithBody(body));
        return this;
    }

    /// <summary>Searches matching <paramref name="filterContains"/> return a raw (possibly malformed) body.</summary>
    public FakeActiveRolesServer WithSearchRawBody(string filterContains, string body, int priority = 40)
    {
        _server.Given(Request.Create().WithPath("/api/objects").UsingGet()
                .WithParam("filter", new WildcardMatcher($"*{filterContains}*", ignoreCase: true)))
            .AtPriority(priority)
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(body));
        return this;
    }

    /// <summary>Searches matching <paramref name="filterContains"/> respond only after <paramref name="delay"/>.</summary>
    public FakeActiveRolesServer WithSearchDelay(string filterContains, TimeSpan delay, int priority = 40)
    {
        _server.Given(Request.Create().WithPath("/api/objects").UsingGet()
                .WithParam("filter", new WildcardMatcher($"*{filterContains}*", ignoreCase: true)))
            .AtPriority(priority)
            .RespondWith(Json(200, new JsonObject { ["items"] = new JsonArray() }).WithDelay(delay));
        return this;
    }

    public FakeActiveRolesServer WithObject(string objectGuid, JsonNode body)
    {
        _server.Given(Request.Create().WithPath($"/api/objects/{objectGuid}").UsingGet())
            .RespondWith(Json(200, body));
        return this;
    }

    /// <summary>Loads a data set from TestData/&lt;name&gt;.json and registers its searches.</summary>
    public FakeActiveRolesServer WithDataSet(string name)
    {
        var set = TestDataSet.Load(name);
        foreach (var search in set.Searches)
            WithSearch(search.FilterContains, search.Items, search.PageSize);
        return this;
    }

    /// <summary>Standard healthy back end: token endpoint, API root and empty default searches.</summary>
    public FakeActiveRolesServer WithHealthyDefaults()
        => WithTokenEndpoint().WithApiRoot().WithEmptySearchDefault();

    // ------------------------------------------------------------------ helpers

    private static bool IsValidTokenRequest(string? body)
    {
        if (string.IsNullOrEmpty(body)) return false;
        var form = body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0].Replace('+', ' ')),
                          p => p.Length > 1 ? Uri.UnescapeDataString(p[1].Replace('+', ' ')) : string.Empty);
        return form.GetValueOrDefault("grant_type") == "password"
            && form.GetValueOrDefault("username") == ValidUser
            && form.GetValueOrDefault("password") == ValidPassword;
    }

    private static List<List<JsonNode>> Paginate(IReadOnlyList<JsonNode> items, int pageSize)
    {
        if (pageSize <= 0 || items.Count <= pageSize)
            return [items.Select(i => i.DeepClone()).ToList()];
        return items.Select(i => i.DeepClone()).Chunk(pageSize).Select(c => c.ToList()).ToList();
    }

    private static JsonObject PageBody(List<List<JsonNode>> pages, int index, string tokenPrefix)
    {
        var body = new JsonObject { ["items"] = new JsonArray(pages[index].Select(n => n.DeepClone()).ToArray()) };
        if (index < pages.Count - 1)
            body["nextPage"] = $"{tokenPrefix}-{index + 1}";
        return body;
    }

    private static IResponseBuilder Json(int status, JsonNode body)
        => Response.Create().WithStatusCode(status)
            .WithHeader("Content-Type", "application/json")
            .WithBody(body.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));

    public void Dispose() => _server.Stop();
}
