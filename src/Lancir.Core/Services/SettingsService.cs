using System.Text.Json;

namespace Lancir.Core.Services;

/// <summary>
/// Reads and writes application settings to a local JSON file under %AppData%/Lancir.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private AppSettings? _cached;

    public AppSettings Load()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        AppPaths.EnsureFoldersExist();

        if (!File.Exists(AppPaths.SettingsPath))
        {
            _cached = new AppSettings();
            return _cached;
        }

        var json = File.ReadAllText(AppPaths.SettingsPath);
        _cached = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

        return _cached;
    }

    /// <summary>The decrypted Steam Web API key, or null when none is set (or it cannot be decrypted here).</summary>
    public string? GetSteamApiKey() => SecretProtector.Unprotect(Load().SteamApiKeyProtected);

    /// <summary>Stores the Steam Web API key encrypted; an empty key clears it.</summary>
    public void SetSteamApiKey(string? key)
    {
        var settings = Load();
        settings.SteamApiKeyProtected = SecretProtector.Protect(key);
        Save(settings);
    }

    public void Save(AppSettings settings)
    {
        AppPaths.EnsureFoldersExist();

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(AppPaths.SettingsPath, json);
        _cached = settings;
    }
}
