using CommunityToolkit.Mvvm.ComponentModel;
using Lancir.Core.Services;

namespace Lancir.App.Services;

/// <summary>
/// Small on/off display preferences chosen in Settings. A shared instance so the library (which binds to it) and the
/// Settings page (which edits it) always agree; changes apply at once and are saved immediately.
/// </summary>
public sealed partial class UiPreferences : ObservableObject
{
    private SettingsService? _settingsService;
    private bool _loading;

    public static UiPreferences Instance { get; } = new();

    private UiPreferences()
    {
    }

    /// <summary>The play button over a cover in the library's grid view (the list view's button is not affected).</summary>
    [ObservableProperty]
    private bool _showGridPlayButton = true;

    partial void OnShowGridPlayButtonChanged(bool value) => Save();

    public void Initialize(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _loading = true;
        try
        {
            ShowGridPlayButton = settingsService.Load().ShowGridPlayButton;
        }
        finally
        {
            _loading = false;
        }
    }

    private void Save()
    {
        if (_loading || _settingsService is null)
        {
            return;
        }

        var settings = _settingsService.Load();
        settings.ShowGridPlayButton = ShowGridPlayButton;
        _settingsService.Save(settings);
    }
}
