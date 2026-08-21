using System.Text.Json;
using ActiveRolesDashboard.Models;

namespace ActiveRolesDashboard.Services;

public class UserSettingsService
{
    private readonly string _settingsFolder;
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public UserSettingsService(IWebHostEnvironment env)
    {
        _settingsFolder = Path.Combine(env.ContentRootPath, "usersettings");
        if (!Directory.Exists(_settingsFolder))
            Directory.CreateDirectory(_settingsFolder);
    }

    public UserSettings Load(string username)
    {
        var filePath = GetFilePath(username);
        if (!File.Exists(filePath))
            return new UserSettings();

        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
    }

    public void Save(string username, UserSettings settings)
    {
        var filePath = GetFilePath(username);
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        File.WriteAllText(filePath, json);
    }

    private string GetFilePath(string username)
    {
        // Sanitize username for use as filename (domain\user -> domain_user)
        var safe = username
            .Replace('\\', '_')
            .Replace('/', '_')
            .Replace(':', '_')
            .Replace('*', '_')
            .Replace('?', '_')
            .Replace('"', '_')
            .Replace('<', '_')
            .Replace('>', '_')
            .Replace('|', '_');
        return Path.Combine(_settingsFolder, $"{safe}.json");
    }
}
