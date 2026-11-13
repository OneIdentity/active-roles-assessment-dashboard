using System.Text.Json.Nodes;

namespace ActiveRolesDashboard.Tests.Infrastructure;

/// <summary>
/// Builds appsettings.json content for a test, starting from the packaged appsettings.json so
/// tests always run against the real shipped schema and defaults.
/// </summary>
public sealed class AppSettingsBuilder
{
    private readonly JsonObject _root;

    private AppSettingsBuilder(JsonObject root) => _root = root;

    /// <summary>The packaged appsettings.json, unmodified (first-run state: no ApiBaseUrl).</summary>
    public static AppSettingsBuilder Packaged()
        => new(JsonNode.Parse(File.ReadAllText(RepositoryPaths.PackagedAppSettings),
            documentOptions: new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!.AsObject());

    /// <summary>
    /// Packaged settings pointed at the fake server (configured state). With no server, returns the
    /// packaged first-run settings. Startup cache load is disabled so tests control data loading.
    /// </summary>
    public static AppSettingsBuilder Default(FakeActiveRolesServer? server)
    {
        var b = Packaged().Set("ActiveRoles:DataRefresh:LoadOnStartup", false)
                          .Set("ActiveRoles:PerformanceTrending:Enabled", false)
                          .Set("ActiveRoles:Analytics:Enabled", false);
        if (server != null)
            b.Set("ActiveRoles:ApiBaseUrl", server.ApiBaseUrl)
             .Set("ActiveRoles:RstsUrl", server.RstsUrl)
             .Set("ActiveRoles:Resource", "urn:test:activeroles")
             .Set("ActiveRoles:WebInterfaceUrl", server.BaseUrl);
        return b;
    }

    /// <summary>Sets a value using a colon-separated path, creating intermediate objects.</summary>
    public AppSettingsBuilder Set(string path, JsonNode? value)
    {
        var parts = path.Split(':');
        var node = _root;
        foreach (var part in parts[..^1])
        {
            if (node[part] is not JsonObject child)
            {
                child = new JsonObject();
                node[part] = child;
            }
            node = child;
        }
        node[parts[^1]] = value;
        return this;
    }

    public AppSettingsBuilder Remove(string path)
    {
        var parts = path.Split(':');
        var node = _root;
        foreach (var part in parts[..^1])
        {
            if (node[part] is not JsonObject child) return this;
            node = child;
        }
        node.Remove(parts[^1]);
        return this;
    }

    public JsonObject Build() => (JsonObject)_root.DeepClone();
}
