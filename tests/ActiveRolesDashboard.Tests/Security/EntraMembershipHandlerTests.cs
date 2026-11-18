using System.Net;
using System.Text.Json;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ActiveRolesDashboard.Tests.Security;

/// <summary>
/// The lazy Entra membership handlers shared by every dashboard page (DashboardPageModel):
/// anonymous rejection, the 409 contract when no Entra totals are cached, the progress
/// snapshot, and the full/batched loaders completing and persisting enriched totals.
/// </summary>
[Trait("Stage", "Security")]
public class EntraMembershipHandlerTests : IDisposable
{
    private const string Page = "/Assessments";

    private readonly FakeActiveRolesServer _server = new FakeActiveRolesServer().WithHealthyDefaults();
    private DashboardAppFactory? _app;

    private PerUserSummaryCache SummaryCache => _app!.Services.GetRequiredService<PerUserSummaryCache>();

    private async Task<HttpClient> SignedIn(EntraTotalsSummary? entraTotals = null)
    {
        _app = new DashboardAppFactory(activeRoles: _server);
        var client = _app.CreateNonRedirectingClient();
        var login = new HttpRequestMessage(HttpMethod.Post, "/Login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Username"] = FakeActiveRolesServer.ValidUser,
                ["Password"] = FakeActiveRolesServer.ValidPassword
            })
        };
        login.Headers.Accept.ParseAdd("application/json");
        var response = await client.SendAsync(login);
        Assert.Contains("/app/", await response.Content.ReadAsStringAsync());

        if (entraTotals != null)
            SummaryCache.SetSummary(FakeActiveRolesServer.ValidUser,
                JsonSerializer.Serialize(new DashboardSummary { EntraTotals = entraTotals }));
        return client;
    }

    private static EntraTotalsSummary TotalsWithoutGroups() => new()
    {
        TotalCount = 1,
        Items =
        [
            new EntraObjectInfo
            {
                Name = "Alice",
                Tenant = "contoso",
                ObjectType = EntraObjectType.User,
                Raw = JsonDocument.Parse("{}").RootElement.Clone()
            }
        ]
    };

    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    [Theory]
    [InlineData("EntraMembership")]
    [InlineData("EntraMembershipBatch")]
    [InlineData("EntraMembershipProgress")]
    public async Task Anonymous_caller_is_redirected_to_login(string handler)
    {
        _app = new DashboardAppFactory(activeRoles: _server);
        using var client = _app.CreateNonRedirectingClient();

        var response = await client.GetAsync($"{Page}?handler={handler}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Login", response.Headers.Location!.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("EntraMembership")]
    [InlineData("EntraMembershipBatch")]
    public async Task Loader_without_cached_entra_totals_returns_conflict(string handler)
    {
        using var client = await SignedIn();

        var json = await Json(await client.GetAsync($"{Page}?handler={handler}"), HttpStatusCode.Conflict);

        Assert.Contains("No cached dashboard data", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Progress_reports_idle_collector_as_done()
    {
        using var client = await SignedIn();

        var json = await Json(await client.GetAsync($"{Page}?handler=EntraMembershipProgress"), HttpStatusCode.OK);

        Assert.False(json.GetProperty("serverLoading").GetBoolean());
        Assert.Equal(0, json.GetProperty("totalGroups").GetInt32());
        Assert.Equal(0, json.GetProperty("loadedCount").GetInt32());
        Assert.Equal(0, json.GetProperty("remaining").GetInt32());
        Assert.True(json.GetProperty("done").GetBoolean());
    }

    [Fact]
    public async Task Full_loader_returns_kpi_payloads_and_persists_loaded_totals()
    {
        using var client = await SignedIn(TotalsWithoutGroups());

        var json = await Json(await client.GetAsync($"{Page}?handler=EntraMembership"), HttpStatusCode.OK);

        foreach (var kpi in new[] { "emptyGroups", "noGroupOwner", "guestContaining", "singleOwner", "largeGroups" })
            Assert.Equal(0, json.GetProperty(kpi).GetProperty("totalCount").GetInt32());
        Assert.True(SummaryCache.GetSummaryModel(FakeActiveRolesServer.ValidUser)!.EntraTotals!.MembershipLoaded);
    }

    [Fact]
    public async Task Batch_loader_reports_completion_and_persists_loaded_totals()
    {
        using var client = await SignedIn(TotalsWithoutGroups());

        var json = await Json(await client.GetAsync($"{Page}?handler=EntraMembershipBatch&skip=0&take=5"), HttpStatusCode.OK);

        Assert.Equal(0, json.GetProperty("totalGroups").GetInt32());
        Assert.Equal(0, json.GetProperty("remaining").GetInt32());
        Assert.True(json.GetProperty("done").GetBoolean());
        Assert.Equal(0, json.GetProperty("emptyGroups").GetProperty("totalCount").GetInt32());
        Assert.True(SummaryCache.GetSummaryModel(FakeActiveRolesServer.ValidUser)!.EntraTotals!.MembershipLoaded);
    }

    public void Dispose()
    {
        _app?.Dispose();
        _server.Dispose();
    }
}
