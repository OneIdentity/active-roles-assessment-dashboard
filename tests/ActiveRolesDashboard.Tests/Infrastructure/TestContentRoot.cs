using System.Text.Json;
using System.Text.Json.Nodes;

namespace ActiveRolesDashboard.Tests.Infrastructure;

/// <summary>A disposable temp folder used as the dashboard's content root.</summary>
public sealed class TestContentRoot : IDisposable
{
    public TestContentRoot()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ard-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string AppSettingsPath => System.IO.Path.Combine(Path, "appsettings.json");
    public string DataProtectionKeysPath => System.IO.Path.Combine(Path, "App_Data", "DataProtectionKeys");
    public string SnapshotsPath => System.IO.Path.Combine(Path, "App_Data", "Snapshots");
    public string AssessmentsPath => System.IO.Path.Combine(Path, "App_Data", "Assessments");
    public string UserSettingsPath => System.IO.Path.Combine(Path, "usersettings");

    public void WriteAppSettings(JsonObject settings)
        => File.WriteAllText(AppSettingsPath, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

    public void WriteAppSettingsRaw(string content) => File.WriteAllText(AppSettingsPath, content);

    public JsonObject ReadAppSettings()
        => JsonNode.Parse(File.ReadAllText(AppSettingsPath))!.AsObject();

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Locates the repository and the dashboard project from the test output folder.</summary>
public static class RepositoryPaths
{
    private static readonly Lazy<string> _projectDirectory = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(System.IO.Path.Combine(dir.FullName, "ActiveRolesDashboard.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("ActiveRolesDashboard.csproj not found above the test output folder.");
    });

    public static string ProjectDirectory => _projectDirectory.Value;

    /// <summary>The committed appsettings.json shipped with the dashboard.</summary>
    public static string PackagedAppSettings => System.IO.Path.Combine(ProjectDirectory, "appsettings.json");
}
