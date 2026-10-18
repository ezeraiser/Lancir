using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Lancir.App.Services;
using Lancir.Core.Services;
using Lancir.Core.Steam;

namespace Lancir.App.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly ISteamLibraryClient _steamLibraryClient;

    public SettingsViewModel(SettingsService settingsService, ISteamLibraryClient steamLibraryClient)
    {
        _settingsService = settingsService;
        _steamLibraryClient = steamLibraryClient;

        var systemName = AppLanguage.SystemLanguage() == AppLanguage.Turkish ? "Türkçe" : "English";
        LanguageOptions =
        [
            new(AppLanguage.System, Loc.F("Sistem dili ({0})", systemName)),
            new(AppLanguage.English, "English"),
            new(AppLanguage.Turkish, "Türkçe"),
        ];

        var saved = settingsService.Load().Language;
        _selectedLanguage = LanguageOptions.FirstOrDefault(o => o.Code == saved) ?? LanguageOptions[0];
    }

    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    /// <summary>True when the chosen language differs from the one the running app uses.</summary>
    [ObservableProperty]
    private bool _needsRestart;

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        var settings = _settingsService.Load();
        settings.Language = value.Code;
        _settingsService.Save(settings);
        NeedsRestart = AppLanguage.Resolve(value.Code) != AppLanguage.Current;
    }

    [RelayCommand]
    private void RestartApp()
    {
        if (Environment.ProcessPath is { } path)
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            System.Windows.Application.Current.Shutdown();
        }
    }

    [ObservableProperty]
    private string? _steamGridDbApiKey;

    [ObservableProperty]
    private string? _igdbClientId;

    [ObservableProperty]
    private string? _igdbClientSecret;

    [ObservableProperty]
    private string? _steamProfile;

    /// <summary>The Steam Web API key as typed; stored encrypted (see <see cref="SecretProtector"/>), never in plain text.</summary>
    [ObservableProperty]
    private string? _steamApiKey;

    [ObservableProperty]
    private bool _syncSteamPlaytimeOnStart;

    /// <summary>Result of the last "Dene" (test connection) click; null = nothing to show.</summary>
    [ObservableProperty]
    private string? _steamTestMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTestSteam))]
    private bool _isSteamTestRunning;

    public bool CanTestSteam => !IsSteamTestRunning;

    [ObservableProperty]
    private bool _isSaved;

    // ---- playtime tracking ----

    public int MinCheckSeconds => PlaytimeTracker.MinCheckIntervalSeconds;

    public int MaxCheckSeconds => PlaytimeTracker.MaxCheckIntervalSeconds;

    private bool _loadingTracking;

    /// <summary>Seconds between looks for a newly started game while none is running (5-600, default 30).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaytimeCheckText))]
    private int _playtimeCheckSeconds = 30;

    public string PlaytimeCheckText => Loc.F("{0} sn", PlaytimeCheckSeconds);

    partial void OnPlaytimeCheckSecondsChanged(int value)
    {
        if (_loadingTracking)
        {
            return;
        }

        var settings = _settingsService.Load();
        settings.PlaytimeCheckIntervalSeconds = Math.Clamp(value, MinCheckSeconds, MaxCheckSeconds);
        _settingsService.Save(settings); // read live by the tracker, so it applies without Kaydet
    }

    private void LoadTrackingSettings(AppSettings settings)
    {
        _loadingTracking = true;
        try
        {
            PlaytimeCheckSeconds = Math.Clamp(settings.PlaytimeCheckIntervalSeconds, MinCheckSeconds, MaxCheckSeconds);
        }
        finally
        {
            _loadingTracking = false;
        }
    }

    // ---- memory ----

    /// <summary>Limits offered for the automatic clean-up (MB); a custom value from the settings file is added when loaded.</summary>
    public ObservableCollection<int> MemoryLimitOptions { get; } = [200, 250, 300, 400, 500, 750, 1000];

    private bool _loadingMemory;

    [ObservableProperty]
    private bool _memoryTrimEnabled = true;

    [ObservableProperty]
    private int _memoryTrimThresholdMb = 300;

    [ObservableProperty]
    private bool _memoryTrimOnBackground;

    /// <summary>"Şu an: 187 MB": the app's current working set, read when the page opens or on refresh.</summary>
    [ObservableProperty]
    private string _currentMemoryText = "";

    /// <summary>Result of the last "Şimdi temizle" click; null = nothing to show.</summary>
    [ObservableProperty]
    private string? _trimResultText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTrimNow))]
    private bool _isTrimming;

    public bool CanTrimNow => !IsTrimming;

    partial void OnMemoryTrimEnabledChanged(bool value) => SaveMemorySettings();

    partial void OnMemoryTrimThresholdMbChanged(int value) => SaveMemorySettings();

    partial void OnMemoryTrimOnBackgroundChanged(bool value) => SaveMemorySettings();

    // Saved at once (the memory manager reads the settings live), so there is nothing to confirm with Kaydet.
    private void SaveMemorySettings()
    {
        if (_loadingMemory)
        {
            return;
        }

        var settings = _settingsService.Load();
        settings.MemoryTrimEnabled = MemoryTrimEnabled;
        settings.MemoryTrimThresholdMb = MemoryTrimThresholdMb;
        settings.MemoryTrimOnBackground = MemoryTrimOnBackground;
        _settingsService.Save(settings);
    }

    private void LoadMemorySettings(AppSettings settings)
    {
        _loadingMemory = true;
        try
        {
            if (!MemoryLimitOptions.Contains(settings.MemoryTrimThresholdMb))
            {
                var index = 0;
                while (index < MemoryLimitOptions.Count && MemoryLimitOptions[index] < settings.MemoryTrimThresholdMb)
                {
                    index++;
                }

                MemoryLimitOptions.Insert(index, settings.MemoryTrimThresholdMb);
            }

            MemoryTrimEnabled = settings.MemoryTrimEnabled;
            MemoryTrimThresholdMb = settings.MemoryTrimThresholdMb;
            MemoryTrimOnBackground = settings.MemoryTrimOnBackground;
        }
        finally
        {
            _loadingMemory = false;
        }

        RefreshMemory();
    }

    [RelayCommand]
    private void RefreshMemory() =>
        CurrentMemoryText = Loc.F("Şu an: {0} MB", MemoryTrimmer.CurrentBytes / (1024 * 1024));

    [RelayCommand]
    private async Task TrimNowAsync()
    {
        IsTrimming = true;
        try
        {
            var result = await Task.Run(MemoryTrimmer.Trim);
            TrimResultText = Loc.F("Temizlendi: {0} MB → {1} MB", result.Before / (1024 * 1024), result.After / (1024 * 1024));
            RefreshMemory();
        }
        finally
        {
            IsTrimming = false;
        }
    }

    /// <summary>Checks the typed profile and key against Steam without saving them.</summary>
    [RelayCommand]
    private async Task TestSteamAsync()
    {
        if (string.IsNullOrWhiteSpace(SteamProfile) || string.IsNullOrWhiteSpace(SteamApiKey))
        {
            SteamTestMessage = Loc.T("Önce Steam profilini ve API anahtarını girin.");
            return;
        }

        IsSteamTestRunning = true;
        SteamTestMessage = Loc.T("Steam'e bağlanılıyor...");
        try
        {
            var steamId = await _steamLibraryClient.ResolveSteamIdAsync(SteamProfile);
            if (steamId is null)
            {
                SteamTestMessage = SteamMessages.For(SteamFetchStatus.ProfileNotFound);
                return;
            }

            var result = await _steamLibraryClient.GetOwnedGamesAsync(SteamApiKey.Trim(), steamId);
            SteamTestMessage = result.Status == SteamFetchStatus.Ok
                ? Loc.F("Bağlandı: {0} oyun bulundu.", result.Games.Count)
                : SteamMessages.For(result.Status);
        }
        finally
        {
            IsSteamTestRunning = false;
        }
    }

    [RelayCommand]
    private Task LoadAsync()
    {
        var settings = _settingsService.Load();
        SteamGridDbApiKey = settings.SteamGridDbApiKey;
        IgdbClientId = settings.IgdbClientId;
        IgdbClientSecret = settings.IgdbClientSecret;
        SteamProfile = settings.SteamProfile;
        SteamApiKey = _settingsService.GetSteamApiKey();
        SyncSteamPlaytimeOnStart = settings.SyncSteamPlaytimeOnStart;
        LoadMemorySettings(settings);
        LoadTrackingSettings(settings);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void Save()
    {
        var settings = _settingsService.Load();
        settings.SteamGridDbApiKey = SteamGridDbApiKey;

        settings.IgdbClientId = string.IsNullOrWhiteSpace(IgdbClientId) ? null : IgdbClientId.Trim();
        settings.IgdbClientSecret = string.IsNullOrWhiteSpace(IgdbClientSecret) ? null : IgdbClientSecret.Trim();

        settings.SteamProfile = string.IsNullOrWhiteSpace(SteamProfile) ? null : SteamProfile.Trim();
        settings.SyncSteamPlaytimeOnStart = SyncSteamPlaytimeOnStart;
        settings.SteamApiKeyProtected = SecretProtector.Protect(SteamApiKey);

        _settingsService.Save(settings);

        IsSaved = true;

        // The "Kaydedildi" confirmation fades after a few seconds, like the error bars do.
        _savedTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _savedTimer.Tick -= OnSavedTimerTick;
        _savedTimer.Tick += OnSavedTimerTick;
        _savedTimer.Stop();
        _savedTimer.Start();
    }

    private System.Windows.Threading.DispatcherTimer? _savedTimer;

    private void OnSavedTimerTick(object? sender, EventArgs e)
    {
        _savedTimer?.Stop();
        IsSaved = false;
    }
}

public sealed record LanguageOption(string Code, string Label);
