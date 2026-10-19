using Lancir.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Lancir.Core.Data;
using System.IO;
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
    private readonly ImageOptimizer _imageOptimizer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ClipOptimizer _clipOptimizer;
    private readonly OriginalsRestorer _restorer;

    public SettingsViewModel(
        SettingsService settingsService, ISteamLibraryClient steamLibraryClient, ImageOptimizer imageOptimizer,
        IServiceScopeFactory scopeFactory, ClipOptimizer clipOptimizer, OriginalsRestorer restorer, BackupViewModel backup)
    {
        Backup = backup;
        _restorer = restorer;
        _scopeFactory = scopeFactory;
        _clipOptimizer = clipOptimizer;
        _settingsService = settingsService;
        _steamLibraryClient = steamLibraryClient;
        _imageOptimizer = imageOptimizer;

        var systemName = AppLanguage.SystemLanguage() == AppLanguage.Turkish ? "Türkçe" : "English";
        LanguageOptions =
        [
            new(AppLanguage.System, Loc.F("Sistem dili ({0})", systemName)),
            new(AppLanguage.English, "English"),
            new(AppLanguage.Turkish, "Türkçe"),
        ];

        var saved = settingsService.Load().Language;
        _selectedLanguage = LanguageOptions.FirstOrDefault(o => o.Code == saved) ?? LanguageOptions[0];
        LoadCardRows();
        LoadLibrarySwitches();
    }

    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    // ---- the status libraries of the left strip ----

    [ObservableProperty]
    private bool _showPlayingLibrary = true;

    [ObservableProperty]
    private bool _showFinishedLibrary = true;

    [ObservableProperty]
    private bool _showDroppedLibrary = true;

    [ObservableProperty]
    private bool _showBacklogLibrary = true;

    [ObservableProperty]
    private bool _showFavoritesLibrary = true;

    private bool _loadingLibrarySwitches;

    partial void OnShowPlayingLibraryChanged(bool value) => SaveLibrarySwitches();

    partial void OnShowFinishedLibraryChanged(bool value) => SaveLibrarySwitches();

    partial void OnShowDroppedLibraryChanged(bool value) => SaveLibrarySwitches();

    partial void OnShowBacklogLibraryChanged(bool value) => SaveLibrarySwitches();

    partial void OnShowFavoritesLibraryChanged(bool value) => SaveLibrarySwitches();

    private void LoadLibrarySwitches()
    {
        var settings = _settingsService.Load();
        _loadingLibrarySwitches = true;
        ShowPlayingLibrary = settings.ShowPlayingLibrary;
        ShowFinishedLibrary = settings.ShowFinishedLibrary;
        ShowDroppedLibrary = settings.ShowDroppedLibrary;
        ShowBacklogLibrary = settings.ShowBacklogLibrary;
        ShowFavoritesLibrary = settings.ShowFavoritesLibrary;
        _loadingLibrarySwitches = false;
    }

    private void SaveLibrarySwitches()
    {
        if (_loadingLibrarySwitches)
        {
            return;
        }

        var settings = _settingsService.Load();
        settings.ShowPlayingLibrary = ShowPlayingLibrary;
        settings.ShowFinishedLibrary = ShowFinishedLibrary;
        settings.ShowDroppedLibrary = ShowDroppedLibrary;
        settings.ShowBacklogLibrary = ShowBacklogLibrary;
        settings.ShowFavoritesLibrary = ShowFavoritesLibrary;
        _settingsService.Save(settings);
        LibraryViewModel.NotifyScopeSettingsChanged();
    }

    /// <summary>The section (tab) shown in Settings; the folder scan is the sixth.</summary>
    public const int ScanSectionIndex = 6;

    public BackupViewModel Backup { get; }

    [ObservableProperty]
    private int _selectedSectionIndex;

    // ---- default card layout of the game details panel ----

    public IReadOnlyList<CardSizeOption> CardSizeOptions { get; } =
    [
        new(CardSize.Full, 0, Loc.T("Tam genişlik")),
        .. Enumerable.Range(1, 10).Select(n => new CardSizeOption(CardSize.Auto, n, n == 1 ? Loc.T("1 sütun") : Loc.F("{0} sütun", n))),
        new(CardSize.Left, 1, Loc.T("Sola sabitle")),
        new(CardSize.Right, 1, Loc.T("Sağa sabitle")),
    ];

    private CardSizeOption OptionFor(CardSize size, int span) =>
        size == CardSize.Full && span == 0
            ? CardSizeOptions[0]
            : size is CardSize.Left or CardSize.Right
                ? CardSizeOptions.First(o => o.Size == size)
                : CardSizeOptions.First(o => o.Size == CardSize.Auto && o.Span == Math.Clamp(span == 0 ? 1 : span, 1, 10));

    public ObservableCollection<CardLayoutRow> DefaultCardRows { get; } = [];

    public IReadOnlyList<IntOption> CardColumnOptions => GamePanelViewModel.CardColumnOptions;

    public IReadOnlyList<IntOption> GalleryColumnOptions => GamePanelViewModel.GalleryColumnOptions;

    [ObservableProperty]
    private int _defaultCardColumns;

    [ObservableProperty]
    private int _defaultGalleryColumns = 2;

    partial void OnDefaultCardColumnsChanged(int value) => SaveCardRows();

    partial void OnDefaultGalleryColumnsChanged(int value) => SaveCardRows();

    private bool _loadingCardRows;

    private void LoadCardRows()
    {
        _loadingCardRows = true;
        DefaultCardRows.Clear();
        var loaded = _settingsService.Load();
        DefaultCardColumns = loaded.DefaultCardOptions.Columns;
        DefaultGalleryColumns = Math.Clamp(loaded.DefaultCardOptions.GalleryColumns, 2, 10);
        var saved = loaded.DetailCards;
        var used = new HashSet<string>();
        foreach (var item in saved)
        {
            var definition = GamePanelViewModel.DefaultCards.FirstOrDefault(d => d.Id == item.Id);
            if (definition.Id is not null && used.Add(item.Id) && Enum.TryParse<CardSize>(item.Size, out var size))
            {
                AddCardRow(definition.Id, definition.Title, size, !item.Hidden, item.Span);
            }
        }

        foreach (var definition in GamePanelViewModel.DefaultCards.Where(d => used.Add(d.Id)))
        {
            AddCardRow(definition.Id, definition.Title, definition.Size, true, 0);
        }

        _loadingCardRows = false;
    }

    private void AddCardRow(string id, string title, CardSize size, bool visible, int span)
    {
        var row = new CardLayoutRow(id, title, OptionFor(size, span), visible);
        row.PropertyChanged += (_, _) => SaveCardRows();
        DefaultCardRows.Add(row);
    }

    private void SaveCardRows()
    {
        if (_loadingCardRows)
        {
            return;
        }

        var settings = _settingsService.Load();
        settings.DefaultCardOptions = new CardViewOptions { Columns = DefaultCardColumns, GalleryColumns = DefaultGalleryColumns };
        settings.DetailCards = DefaultCardRows.Select(r => new DetailCardSetting { Id = r.Id, Size = r.SelectedSize.Size.ToString(), Hidden = !r.IsVisible, Span = r.SelectedSize.Span }).ToList();
        _settingsService.Save(settings);
        GamePanelViewModel.NotifyDefaultLayoutChanged();
    }

    [RelayCommand]
    private void MoveCardRowUp(CardLayoutRow row) => MoveCardRow(row, -1);

    [RelayCommand]
    private void MoveCardRowDown(CardLayoutRow row) => MoveCardRow(row, 1);

    private void MoveCardRow(CardLayoutRow row, int step)
    {
        var from = DefaultCardRows.IndexOf(row);
        var to = from + step;
        if (from < 0 || to < 0 || to >= DefaultCardRows.Count)
        {
            return;
        }

        DefaultCardRows.Move(from, to);
        SaveCardRows();
    }

    [RelayCommand]
    private void ResetDefaultCardLayout()
    {
        var settings = _settingsService.Load();
        settings.DetailCards = [];
        settings.DefaultCardOptions = new CardViewOptions();
        _settingsService.Save(settings);
        LoadCardRows();
        GamePanelViewModel.NotifyDefaultLayoutChanged();
    }

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

    // ---- clips (video) ----

    public IReadOnlyList<ImageStorageOption> ClipStorageOptions { get; } =
    [
        new(ImageStorageMode.Original, Loc.T("Orijinal (sıkıştırma yok)")),
        new(ImageStorageMode.CompressKeepOriginal, Loc.T("Küçült, orijinali de sakla")),
        new(ImageStorageMode.CompressReplace, Loc.T("Küçült, orijinali sil (en az yer)")),
    ];

    public IReadOnlyList<ClipQualityOption> ClipQualityOptions { get; } =
    [
        new(ClipQualityPreset.Smallest, Loc.T("En küçük (640 px, 1 Mbps)")),
        new(ClipQualityPreset.Balanced, Loc.T("Dengeli (720 px, 1,5 Mbps)")),
        new(ClipQualityPreset.High, Loc.T("Yüksek kalite (960 px, 3 Mbps)")),
    ];

    private bool _loadingClips;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClipCompressing))]
    private ImageStorageOption _selectedClipStorage = null!;

    [ObservableProperty]
    private ClipQualityOption _selectedClipQuality = null!;

    public bool IsClipCompressing => SelectedClipStorage?.Mode != ImageStorageMode.Original;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOptimizeClips))]
    private bool _isClipOptimizing;

    public bool CanOptimizeClips => !IsClipOptimizing && IsClipCompressing;

    [ObservableProperty]
    private string? _clipOptimizeText;

    [ObservableProperty]
    private string _clipPendingText = "";

    /// <summary>Per-game history of clip optimization, most recent first.</summary>
    public ObservableCollection<OptimizationRow> ClipRows { get; } = [];

    [ObservableProperty]
    private string _clipSummary = "";

    [ObservableProperty]
    private bool _hasClipRows;

    partial void OnSelectedClipStorageChanged(ImageStorageOption value)
    {
        OnPropertyChanged(nameof(CanOptimizeClips));
        SaveClipSettings();
    }

    partial void OnSelectedClipQualityChanged(ClipQualityOption value) => SaveClipSettings();

    private void SaveClipSettings()
    {
        if (_loadingClips || SelectedClipStorage is null || SelectedClipQuality is null)
        {
            return;
        }

        var settings = _settingsService.Load();
        settings.ClipStorage = SelectedClipStorage.Mode;
        settings.ClipQuality = SelectedClipQuality.Preset;
        _settingsService.Save(settings);
        _ = RefreshClipPendingAsync();
    }

    private void LoadClipSettings(AppSettings settings)
    {
        _loadingClips = true;
        try
        {
            SelectedClipStorage = ClipStorageOptions.First(o => o.Mode == settings.ClipStorage);
            SelectedClipQuality = ClipQualityOptions.First(o => o.Preset == settings.ClipQuality);
        }
        finally
        {
            _loadingClips = false;
        }

        OnPropertyChanged(nameof(CanOptimizeClips));
        _ = RefreshClipPendingAsync();
        _ = RefreshClipLogAsync();
    }

    /// <summary>How many downloaded clips are still waiting to be looked at.</summary>
    private async Task RefreshClipPendingAsync()
    {
        var pending = await Task.Run(() => _clipOptimizer.PendingClips().Count);
        ClipPendingText = pending == 0
            ? Loc.T("Bekleyen klip yok.")
            : Loc.F("{0} klip henüz işlenmedi.", pending);
    }

    [RelayCommand]
    private async Task RefreshClipLogAsync()
    {
        var (rows, summary) = await Task.Run(() =>
        {
            var log = OptimizationLog.Clips.Load();
            Dictionary<int, string> names;
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
                names = db.Games.Select(g => new { g.Id, g.DisplayName }).ToDictionary(g => g.Id, g => g.DisplayName);
            }

            var list = log.Select(e => new OptimizationRow(
                names.TryGetValue(e.GameId, out var name) ? name : Loc.T("(kütüphanede olmayan oyun)"),
                TimeText.DateTimeText(e.LastRunUtc),
                Loc.F("Son işlem: {0} klip, {1} MB → {2} MB ({3} MB kazanç)",
                    e.LastConverted, Mb(e.LastBefore), Mb(e.LastAfter), Mb(e.LastSaved)),
                Loc.F("Toplam: {0} klip, {1} MB kazanç", e.TotalConverted, Mb(e.TotalSaved)))).ToList();

            return (list, log.Count == 0
                ? ""
                : Loc.F("{0} oyun, {1} klip küçültüldü, toplam {2} MB kazanç", log.Count, log.Sum(e => e.TotalConverted), Mb(log.Sum(e => e.TotalSaved))));
        });

        ClipRows.Clear();
        foreach (var row in rows)
        {
            ClipRows.Add(row);
        }

        HasClipRows = rows.Count > 0;
        ClipSummary = summary;
    }

    /// <summary>Re-encodes the clips that have not been handled yet, now (a few seconds per clip).</summary>
    [RelayCommand]
    private async Task OptimizeClipsAsync()
    {
        IsClipOptimizing = true;
        ClipOptimizeText = Loc.T("Taranıyor...");
        try
        {
            var progress = new Progress<ClipProgress>(p =>
                ClipOptimizeText = Loc.F("Klip küçültülüyor: {0} / {1}", p.Done, p.Total));
            var result = await Task.Run(() => _clipOptimizer.RunAsync(progress));
            ClipOptimizeText = result switch
            {
                null => Loc.T("Başka bir klip işlemi zaten çalışıyor."),
                { Converted: 0, AlreadySmall: 0, Failed: 0 } => Loc.T("İşlenecek yeni klip yok."),
                _ => Loc.F("Tamamlandı: {0} klip küçültüldü ({1} MB → {2} MB, {3} MB kazanç), {4} klip zaten küçük, {5} klip dönüştürülemedi.",
                    result.Converted, Mb(result.BytesBefore), Mb(result.BytesAfter), Mb(result.BytesSaved), result.AlreadySmall, result.Failed),
            };
        }
        finally
        {
            IsClipOptimizing = false;
            _ = RefreshClipPendingAsync();
            _ = RefreshClipLogAsync();
            _ = RefreshCacheUsageAsync();
        }
    }

    // ---- pictures and disk ----

    public IReadOnlyList<ImageStorageOption> ImageStorageOptions { get; } =
    [
        new(ImageStorageMode.Original, Loc.T("Orijinal (sıkıştırma yok)")),
        new(ImageStorageMode.CompressKeepOriginal, Loc.T("Sıkıştır, orijinali de sakla")),
        new(ImageStorageMode.CompressReplace, Loc.T("Sıkıştır, orijinali sil (en az yer)")),
    ];

    /// <summary>WebP quality levels offered; a custom value from the settings file is added when loaded.</summary>
    public ObservableCollection<ImageQualityOption> ImageQualityOptions { get; } =
    [
        new(70, Loc.T("En küçük (70)")),
        new(80, Loc.T("Dengeli (80)")),
        new(90, Loc.T("Yüksek kalite (90)")),
    ];

    private bool _loadingImages;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCompressing))]
    private ImageStorageOption _selectedImageStorage = null!;

    [ObservableProperty]
    private ImageQualityOption _selectedImageQuality = null!;

    /// <summary>True when pictures are compressed (so the quality and "apply now" make sense).</summary>
    public bool IsCompressing => SelectedImageStorage?.Mode != ImageStorageMode.Original;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOptimizeNow))]
    private bool _isOptimizing;

    public bool CanOptimizeNow => !IsOptimizing && IsCompressing;

    [ObservableProperty]
    private string? _optimizeText;

    [ObservableProperty]
    private string _cacheUsageText = "";

    partial void OnSelectedImageStorageChanged(ImageStorageOption value)
    {
        OnPropertyChanged(nameof(CanOptimizeNow));
        SaveImageSettings();
    }

    partial void OnSelectedImageQualityChanged(ImageQualityOption value) => SaveImageSettings();

    /// <summary>True when the image settings were changed and the cache has not been converted for them yet.</summary>
    public bool IsImageChangePending => _imageOptimizer.NeedsRun;

    private void SaveImageSettings()
    {
        if (_loadingImages || SelectedImageStorage is null || SelectedImageQuality is null)
        {
            return;
        }

        var settings = _settingsService.Load();
        settings.ImageStorage = SelectedImageStorage.Mode;
        settings.ImageQuality = SelectedImageQuality.Quality;
        _settingsService.Save(settings); // read live by the downloads and the optimizer
        OnPropertyChanged(nameof(IsImageChangePending));
    }

    private void LoadImageSettings(AppSettings settings)
    {
        _loadingImages = true;
        try
        {
            if (ImageQualityOptions.All(o => o.Quality != settings.ImageQuality))
            {
                var index = 0;
                while (index < ImageQualityOptions.Count && ImageQualityOptions[index].Quality < settings.ImageQuality)
                {
                    index++;
                }

                ImageQualityOptions.Insert(index, new ImageQualityOption(settings.ImageQuality, Loc.F("Özel ({0})", settings.ImageQuality)));
            }

            SelectedImageStorage = ImageStorageOptions.First(o => o.Mode == settings.ImageStorage);
            SelectedImageQuality = ImageQualityOptions.First(o => o.Quality == settings.ImageQuality);
        }
        finally
        {
            _loadingImages = false;
        }

        OnPropertyChanged(nameof(CanOptimizeNow));
        OnPropertyChanged(nameof(IsImageChangePending));
        _ = RefreshCacheUsageAsync();
        _ = RefreshOptimizationLogAsync();
    }

    [RelayCommand]
    private async Task RefreshCacheUsageAsync()
    {
        var usage = await Task.Run(ImageOptimizer.MeasureCache);
        CacheUsageText = Loc.F(
            "Önbellek: {0} MB (görseller {1}, klipler {2}, küçük kopyalar {3}, saklanan orijinaller {4})",
            Mb(usage.Total), Mb(usage.Pictures), Mb(usage.Clips), Mb(usage.Thumbnails), Mb(usage.Originals));
    }

    private static string Mb(long bytes) => (bytes / 1048576.0).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>Converts the pictures already on disk now, following the chosen storage mode and quality.</summary>
    [RelayCommand]
    private async Task OptimizeNowAsync()
    {
        IsOptimizing = true;
        OptimizeText = Loc.T("Taranıyor...");
        try
        {
            var progress = new Progress<OptimizeProgress>(p =>
                OptimizeText = Loc.F("Sıkıştırılıyor: {0} / {1}", p.Done, p.Total));
            var result = await Task.Run(() => _imageOptimizer.RunAsync(progress));
            OptimizeText = result switch
            {
                null => Loc.T("Başka bir sıkıştırma zaten çalışıyor."),
                { Converted: 0, Failed: 0 } => Loc.T("Sıkıştırılacak yeni görsel yok."),
                _ => Loc.F("Tamamlandı: {0} görsel, {1} MB → {2} MB ({3} MB kazanç).{4}",
                    result.Converted, Mb(result.BytesBefore), Mb(result.BytesAfter), Mb(result.BytesSaved),
                    result.Failed > 0 ? " " + Loc.F("{0} dosya kullanımda, sonra tekrar denenir.", result.Failed) : ""),
            };
        }
        finally
        {
            IsOptimizing = false;
            OnPropertyChanged(nameof(IsImageChangePending));
            _ = RefreshCacheUsageAsync();
            _ = RefreshOptimizationLogAsync();
        }
    }

    /// <summary>Per-game history of picture optimization: when it last ran and how much it saved, most recent first.</summary>
    public ObservableCollection<OptimizationRow> OptimizationRows { get; } = [];

    [ObservableProperty]
    private string _optimizationSummary = "";

    [ObservableProperty]
    private bool _hasOptimizationRows;

    [RelayCommand]
    private async Task RefreshOptimizationLogAsync()
    {
        var (rows, summary) = await Task.Run(() =>
        {
            var log = OptimizationLog.Pictures.Load();
            Dictionary<int, string> names;
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
                names = db.Games.Select(g => new { g.Id, g.DisplayName }).ToDictionary(g => g.Id, g => g.DisplayName);
            }

            var list = log.Select(e => new OptimizationRow(
                names.TryGetValue(e.GameId, out var name) ? name : Loc.T("(kütüphanede olmayan oyun)"),
                TimeText.DateTimeText(e.LastRunUtc),
                Loc.F("Son işlem: {0} dosya, {1} MB → {2} MB ({3} MB kazanç)",
                    e.LastConverted, Mb(e.LastBefore), Mb(e.LastAfter), Mb(e.LastSaved)),
                Loc.F("Toplam: {0} dosya, {1} MB kazanç", e.TotalConverted, Mb(e.TotalSaved)))).ToList();

            var saved = log.Sum(e => e.TotalSaved);
            var files = log.Sum(e => e.TotalConverted);
            return (list, log.Count == 0
                ? ""
                : Loc.F("{0} oyun, {1} dosya sıkıştırıldı, toplam {2} MB kazanç", log.Count, files, Mb(saved)));
        });

        OptimizationRows.Clear();
        foreach (var row in rows)
        {
            OptimizationRows.Add(row);
        }

        HasOptimizationRows = rows.Count > 0;
        OptimizationSummary = summary;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanManageOriginals))]
    private bool _isManagingOriginals;

    public bool CanManageOriginals => !IsManagingOriginals;

    [ObservableProperty]
    private string? _originalsText;

    /// <summary>
    /// Downloads the original pictures of every Steam-matched game again and keeps them in the originals folder (the pictures in
    /// use stay compressed). Needed once to revise already-compressed pictures, whose originals were deleted. Covers and
    /// backgrounds are not covered: re-match the game ("Üst veri getir") to fetch those again.
    /// </summary>
    [RelayCommand]
    private async Task RedownloadOriginalsAsync()
    {
        IsManagingOriginals = true;
        OriginalsText = Loc.T("Oyunlar taranıyor...");
        try
        {
            var games = await Task.Run(() =>
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
                return db.Games
                    .Where(g => g.SteamAppId != null)
                    .Select(g => new { g.Id, g.DisplayName, g.ScreenshotUrls, g.TrailerThumbnailUrl, g.DescriptionRich })
                    .ToList();
            });

            int downloaded = 0, failed = 0, done = 0;
            long bytes = 0;
            foreach (var game in games)
            {
                OriginalsText = Loc.F("Orijinaller indiriliyor: {0} / {1} ({2})", ++done, games.Count, game.DisplayName);
                var extra = new GameExtraDetails
                {
                    RichDescription = game.DescriptionRich,
                    TrailerThumbnailUrl = game.TrailerThumbnailUrl,
                    ScreenshotUrls = (game.ScreenshotUrls ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                };

                using var scope = _scopeFactory.CreateScope();
                var cache = scope.ServiceProvider.GetRequiredService<SteamMediaCache>();
                var result = await cache.DownloadOriginalsAsync(game.Id, extra);
                downloaded += result.Downloaded;
                failed += result.Failed;
                bytes += result.Bytes;
            }

            OriginalsText = Loc.F("Tamamlandı: {0} orijinal indirildi ({1} MB){2}. Şimdi \"Orijinallerden yeniden sıkıştır\" ile kaliteyi uygulayabilirsiniz.",
                downloaded, Mb(bytes), failed > 0 ? ", " + Loc.F("{0} indirilemedi", failed) : "");
        }
        catch (Exception ex)
        {
            OriginalsText = Loc.F("Orijinaller indirilemedi: {0}", ex.Message);
        }
        finally
        {
            IsManagingOriginals = false;
            _ = RefreshCacheUsageAsync();
        }
    }

    /// <summary>
    /// Replaces EVERYTHING compression changed with its original: store pictures and screenshots, the re-encoded clips, covers and
    /// backgrounds. Kept originals are moved back, the rest is downloaded again. Compression is switched off afterwards so the
    /// restored files are not compressed again at the next start (switch it back on to compress anew).
    /// </summary>
    [RelayCommand]
    private async Task RestoreAllOriginalsAsync()
    {
        if (_imageOptimizer.IsRunning || _clipOptimizer.IsRunning || _restorer.IsRunning)
        {
            OriginalsText = Loc.T("Başka bir sıkıştırma veya geri yükleme çalışıyor. Bitince tekrar deneyin.");
            return;
        }

        IsManagingOriginals = true;
        OriginalsText = Loc.T("Orijinaller geri yükleniyor...");
        try
        {
            var progress = new Progress<RestoreProgress>(p =>
                OriginalsText = Loc.F("Orijinaller geri yükleniyor: {0} / {1} ({2})", p.Done, p.Total, p.Current ?? "…"));
            var result = await Task.Run(() => _restorer.RunAsync(progress));
            if (result is null)
            {
                OriginalsText = Loc.T("Başka bir geri yükleme zaten çalışıyor.");
                return;
            }

            // Stop compressing: the restored files must stay as they are until the user switches compression on again.
            SelectedImageStorage = ImageStorageOptions.First(o => o.Mode == ImageStorageMode.Original);
            SelectedClipStorage = ClipStorageOptions.First(o => o.Mode == ImageStorageMode.Original);
            ImageLoader.Clear();

            var parts = new List<string>
            {
                Loc.F("{0} görsel, {1} klip, {2} kapak/arka plan orijinaliyle değiştirildi ({3} MB → {4} MB).",
                    result.Pictures, result.Clips, result.Covers, Mb(result.BytesBefore), Mb(result.BytesAfter)),
            };
            if (result.CoversWithoutSource > 0)
            {
                parts.Add(Loc.F("{0} kapak/arka planın kaynak adresi kayıtlı değil: o oyunları \"Üst veri getir\" ile yeniden eşleştirin.", result.CoversWithoutSource));
            }

            if (result.Failed > 0)
            {
                parts.Add(Loc.F("{0} dosya geri yüklenemedi (tekrar deneyebilirsiniz).", result.Failed));
            }

            parts.Add(Loc.T("Sıkıştırma kapatıldı."));
            OriginalsText = string.Join(" ", parts);
        }
        catch (Exception ex)
        {
            OriginalsText = Loc.F("Orijinaller geri yüklenemedi: {0}", ex.Message);
        }
        finally
        {
            IsManagingOriginals = false;
            _ = RefreshCacheUsageAsync();
            _ = RefreshClipPendingAsync();
        }
    }

    /// <summary>Re-makes the compressed pictures from the kept originals with the current quality (never compresses a compressed picture again).</summary>
    [RelayCommand]
    private async Task RecompressFromOriginalsAsync()
    {
        IsManagingOriginals = true;
        OriginalsText = Loc.T("Taranıyor...");
        try
        {
            var progress = new Progress<OptimizeProgress>(p => OriginalsText = Loc.F("Yeniden sıkıştırılıyor: {0} / {1}", p.Done, p.Total));
            var result = await Task.Run(() => _imageOptimizer.RecompressFromOriginalsAsync(progress));
            OriginalsText = result switch
            {
                null => Loc.T("Başka bir sıkıştırma zaten çalışıyor."),
                { Converted: 0, Failed: 0 } => Loc.T("Saklanan orijinal yok. Önce \"Orijinalleri yeniden indir\" ya da \"orijinali de sakla\" kullanın."),
                _ => Loc.F("Tamamlandı: {0} görsel orijinalinden yeniden üretildi ({1} MB → {2} MB).{3}",
                    result.Converted, Mb(result.BytesBefore), Mb(result.BytesAfter),
                    result.Failed > 0 ? " " + Loc.F("{0} dosya atlandı (orijinalinden küçük olmadı).", result.Failed) : ""),
            };
        }
        finally
        {
            IsManagingOriginals = false;
            _ = RefreshCacheUsageAsync();
            _ = RefreshOptimizationLogAsync();
        }
    }

    /// <summary>Opens the folder where originals are kept when "compress, keep original" is chosen.</summary>
    [RelayCommand]
    private void OpenOriginalsFolder()
    {
        Directory.CreateDirectory(AppPaths.OriginalImagesFolder);
        Process.Start(new ProcessStartInfo(AppPaths.OriginalImagesFolder) { UseShellExecute = true });
    }

    // ---- startup and window ----

    private bool _loadingWindow;

    [ObservableProperty]
    private bool _runAtStartup;

    [ObservableProperty]
    private bool _startMinimized;

    /// <summary>Start minimized only when Windows starts the app at sign-in.</summary>
    [ObservableProperty]
    private bool _startMinimizedAtWindowsStartup;

    [ObservableProperty]
    private bool _minimizeToTray;

    [ObservableProperty]
    private bool _closeToTray;

    /// <summary>Set when Windows would not let the "start with Windows" entry be written.</summary>
    [ObservableProperty]
    private string? _startupMessage;

    partial void OnRunAtStartupChanged(bool value)
    {
        if (_loadingWindow)
        {
            return;
        }

        StartupMessage = null;
        if (!StartupRegistration.Apply(value))
        {
            StartupMessage = Loc.T("Windows başlangıç kaydı yazılamadı.");
            _loadingWindow = true;
            RunAtStartup = StartupRegistration.IsEnabled; // show what is really set
            _loadingWindow = false;
            return;
        }

        SaveWindowSettings();
    }

    partial void OnStartMinimizedChanged(bool value) => SaveWindowSettings();

    partial void OnStartMinimizedAtWindowsStartupChanged(bool value) => SaveWindowSettings();

    partial void OnMinimizeToTrayChanged(bool value) => SaveWindowSettings();

    partial void OnCloseToTrayChanged(bool value) => SaveWindowSettings();

    private void SaveWindowSettings()
    {
        if (_loadingWindow)
        {
            return;
        }

        var settings = _settingsService.Load();
        settings.RunAtStartup = RunAtStartup;
        settings.StartMinimized = StartMinimized;
        settings.StartMinimizedAtWindowsStartup = StartMinimizedAtWindowsStartup;
        settings.MinimizeToTray = MinimizeToTray;
        settings.CloseToTray = CloseToTray;
        _settingsService.Save(settings);
        WindowBehavior.Current?.Refresh(); // shows or hides the notification-area icon at once
    }

    private void LoadWindowSettings(AppSettings settings)
    {
        _loadingWindow = true;
        try
        {
            RunAtStartup = StartupRegistration.IsEnabled; // the registry is the truth: Windows' Startup apps page can change it too
            StartMinimized = settings.StartMinimized;
            StartMinimizedAtWindowsStartup = settings.StartMinimizedAtWindowsStartup;
            MinimizeToTray = settings.MinimizeToTray;
            CloseToTray = settings.CloseToTray;
            StartupMessage = null;
        }
        finally
        {
            _loadingWindow = false;
        }
    }

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
        LoadWindowSettings(settings);
        LoadImageSettings(settings);
        LoadClipSettings(settings);
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

public sealed record ImageStorageOption(ImageStorageMode Mode, string Label);

public sealed record ImageQualityOption(int Quality, string Label);

/// <summary>One line of the optimization history on the Settings page.</summary>
public sealed record OptimizationRow(string Game, string When, string Last, string Total);

public sealed record ClipQualityOption(ClipQualityPreset Preset, string Label);
