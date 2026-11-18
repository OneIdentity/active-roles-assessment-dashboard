using System.Text.Json;
using ActiveRolesDashboard.Models;
using ActiveRolesDashboard.Models.Definitions;
using ActiveRolesDashboard.Services;
using ActiveRolesDashboard.Services.Definitions;
using ActiveRolesDashboard.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ActiveRolesDashboard.Tests.Definitions;

public sealed class DatasetCollectorTests : IDisposable
{
    private readonly FakeActiveRolesServer _server = new FakeActiveRolesServer().WithHealthyDefaults();

    public void Dispose() => _server.Dispose();

    private static DefinitionRegistry Registry() =>
        new DefinitionStore(DefinitionStore.LoadEmbedded(typeof(DefinitionStore).Assembly), null).Registry;

    private static ActiveRolesConfig Config() => new()
    {
        DefaultDNs = new DefaultDnsConfig { ActiveDirectory = "CN=Active Directory", ARConfiguration = "CN=Configuration" },
        DefaultADUserAccountAttributes = ["name", "distinguishedName", "userAccountControl"],
        CustomADUserAccountAttributes = ["department", "name"],
    };

    [Fact]
    public void Resolves_config_tokens_and_user_attributes()
    {
        var config = Config();
        var request = DatasetRequestResolver.Resolve(Registry().Datasets["ADUserAccounts"], config);

        Assert.NotNull(request);
        Assert.Equal("CN=Active Directory", request.Base);
        Assert.Equal(config.DefaultFilters.ADUserAccounts, request.Filter);
        Assert.Equal("sub", request.Scope);
        Assert.Equal(["name", "distinguishedName", "userAccountControl", "department", "edsvaAPOLinksEffective"], request.Attributes);
    }

    [Fact]
    public void Merges_attributes_declared_by_consuming_kpis()
    {
        var dataset = new DatasetDefinition
        {
            Id = "D",
            DataSource = DataSource.ActiveDirectory,
            Retrieval = new RetrievalDefinition { Base = "{DefaultARConfigDN}", Filter = "(objectClass=x)" },
            Attributes = ["name"]
        };
        var kpi = new DerivedKpi { Id = "K", SourceDatasetId = "D", Attributes = ["Name", "mail"] };

        var request = DatasetRequestResolver.Resolve(dataset, Config(), [kpi]);

        Assert.NotNull(request);
        Assert.Equal("CN=Configuration", request.Base);
        Assert.Equal(["name", "mail"], request.Attributes);
    }

    [Fact]
    public void Object_reads_resolve_to_member_expanding_requests()
    {
        var objects = new DatasetDefinition
        {
            Id = "O",
            Retrieval = new RetrievalDefinition { Target = RetrievalTarget.Objects, Base = "{DefaultADDN}", Objects = ["Domain Admins", "x(y)"] }
        };

        var request = DatasetRequestResolver.Resolve(objects, Config());

        Assert.NotNull(request);
        Assert.True(request.ExpandMembers);
        Assert.Equal("(|(name=Domain Admins)(name=x\\28y\\29))", request.Filter);
        Assert.Equal(["distinguishedName", "edsaDomainNetbiosName", "member"], request.Attributes);
    }

    [Fact]
    public void Embedded_fixed_object_datasets_resolve()
    {
        var config = Config();
        config.RoleGroups.ActiveRolesAdmins = "AR Admins";
        var registry = Registry();

        var admins = DatasetRequestResolver.Resolve(registry.Datasets["DomainAdmins"], config);
        var arAdmins = DatasetRequestResolver.Resolve(registry.Datasets["ActiveRolesAdmins"], config);

        Assert.Equal("(&(objectClass=group)(name=Domain Admins))", admins!.Filter);
        Assert.True(admins.ExpandMembers);
        Assert.Equal("(&(objectClass=group)(name=AR Admins))", arAdmins!.Filter);
        Assert.Equal("CN=Active Directory", arAdmins.Base);
    }

    [Fact]
    public void Unresolved_filters_and_unconfigured_role_groups_are_not_collected()
    {
        var unknown = new DatasetDefinition
        {
            Id = "U",
            Retrieval = new RetrievalDefinition { Base = "{DefaultADDN}", Filter = "{ConfigFilter:DoesNotExist}" }
        };

        Assert.Null(DatasetRequestResolver.Resolve(unknown, Config()));
        Assert.Null(DatasetRequestResolver.Resolve(Registry().Datasets["ActiveRolesAdmins"], Config()));
    }

    [Fact]
    public void Membership_expander_lists_direct_then_new_indirect_members()
    {
        var group = JsonDocument.Parse("{\"distinguishedName\":\"CN=G,DC=x\",\"edsaDomainNetbiosName\":\"X\",\"member\":[\"CN=A,DC=x\",\"CN=Sub,DC=x\"]}").RootElement;
        var raw = JsonDocument.Parse("{\"objectClass\":\"user\"}").RootElement;

        var items = MembershipExpander.Expand([group],
            _ => ["CN=A,DC=x", "CN=B\\, Jr,DC=x"],
            dn => dn == "CN=A,DC=x" ? raw : null);

        Assert.Equal(3, items.Count);
        Assert.Equal("Direct", items[0].GetProperty("membershipType").GetString());
        Assert.Equal("user", items[0].GetProperty("objectClass").GetString());
        Assert.Equal("Indirect", items[2].GetProperty("membershipType").GetString());
        Assert.Equal("B, Jr", items[2].GetProperty("name").GetString());
        Assert.Equal("X", items[2].GetProperty("domain").GetString());
    }

    [Fact]
    public async Task Collector_runs_each_dataset_once_and_captures_failures()
    {
        var collector = new DatasetCollector(Registry(), NullLogger<DatasetCollector>.Instance);
        var calls = new List<string>();

        var results = await collector.CollectAsync(["ADUserAccounts", "adUserAccounts", "ADGroups", "Missing"], Config(), r =>
        {
            lock (calls) calls.Add(r.DatasetId);
            if (r.DatasetId == "ADGroups") throw new HttpRequestException("boom");
            return Task.FromResult(new List<JsonElement> { JsonDocument.Parse("{\"distinguishedName\":\"CN=a\"}").RootElement });
        });

        Assert.Equal(2, calls.Count);
        Assert.Single(results["ADUserAccounts"].Items);
        Assert.Null(results["ADUserAccounts"].Error);
        Assert.Contains("boom", results["ADGroups"].Error);
        Assert.False(results.ContainsKey("Missing"));
    }

    [Fact]
    public void Parity_reports_missing_and_extra_items_by_distinguished_name()
    {
        static JsonElement Item(string dn) => JsonDocument.Parse($"{{\"distinguishedName\":\"{dn}\"}}").RootElement;
        var legacy = new Dictionary<string, IReadOnlyList<JsonElement>> { ["D"] = [Item("CN=a"), Item("CN=b")] };
        var generic = new Dictionary<string, DatasetResult> { ["D"] = new([Item("cn=A"), Item("CN=c")], null) };

        var result = Assert.Single(DatasetParity.Compare(legacy, generic));

        Assert.False(result.Matches);
        Assert.Equal(1, result.MissingInGeneric);
        Assert.Equal(1, result.ExtraInGeneric);
    }

    [Theory]
    [InlineData("small-domain")]
    public async Task Shadow_collection_matches_legacy_datasets(string dataSet)
    {
        _server.WithDataSet(dataSet);
        var config = new TestOptionsMonitor<ActiveRolesConfig>(new ActiveRolesConfig
        {
            ApiBaseUrl = _server.ApiBaseUrl,
            RstsUrl = _server.RstsUrl,
            DefinitionDatasetShadowCollection = true,
        });
        var factory = new TestHttpClientFactory(TimeSpan.FromSeconds(10));
        var tokens = new ServiceAccountTokenProvider(
            new RstsAuthService(config, factory),
            new ServiceAccountSecretProtector(new EphemeralDataProtectionProvider()),
            config,
            NullLogger<ServiceAccountTokenProvider>.Instance);
        var collector = new DatasetCollector(Registry(), NullLogger<DatasetCollector>.Instance);
        var service = new ActiveRolesService(factory, config, NullLogger<ActiveRolesService>.Instance, tokens, datasetCollector: collector);

        await service.GetDashboardSummaryAsync(FakeActiveRolesServer.ValidToken);

        var results = service.LastDatasetParity;
        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.True(r.Matches,
            $"{r.DatasetId}: legacy {r.Legacy}, definition {r.Generic}, missing {r.MissingInGeneric}, extra {r.ExtraInGeneric} {r.Error}"));
    }
}
