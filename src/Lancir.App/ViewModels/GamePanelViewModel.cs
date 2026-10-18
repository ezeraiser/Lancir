using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lancir.App.Controls;
using Lancir.App.Services;
using Lancir.Core.Models;
using Lancir.Core.Services;

namespace Lancir.App.ViewModels;

public sealed record StatusOption(string Label, GameStatus Value);

public enum AboutBlockKind
{
    Paragraph,
    Heading,
    Bullet,
}

/// <summary>One block of the "Hakkında" card: a text block, or a clip/picture from the store page.</summary>
public sealed record AboutText(AboutBlockKind Kind, string Text);

/// <summary>A picture of the store description: <paramref name="DisplayUrl"/> is the downloaded copy when there is one, otherwise the web address.</summary>
public sealed record AboutImage(string DisplayUrl);

/// <summary>A hint at the top of the card: some pictures are AVIF files and this PC lacks Microsoft's extension to show them.</summary>
public sealed record AboutNotice(string Text);

/// <summary>
/// An animated clip of the store description, played like a GIF while it is on screen: <see cref="ClipStream"/>
/// decodes the downloaded file with libVLC and the tile shows <see cref="Frame"/>. Clicking the tile opens the
/// clip in the system's video player.
/// </summary>
public sealed partial class AboutClip : ObservableObject, IDisposable
{
    private static readonly TimeSpan PlayDuration = TimeSpan.FromSeconds(10);

    private ClipStream? _stream;
    private System.Windows.Threading.DispatcherTimer? _limit;
    private bool _finished;

    public AboutClip(string url, string? localPath, double width, double height)
    {
        Url = url;
        LocalPath = localPath;
        ThumbWidth = 480;
        ThumbHeight = width > 0 && height > 0 ? (int)Math.Round(ThumbWidth * height / width) : 270;
    }

    public string Url { get; }

    public string? LocalPath { get; }

    public int ThumbWidth { get; }

    public int ThumbHeight { get; }

    /// <summary>The picture of the current frame; null until the clip has been started once.</summary>
    [ObservableProperty]
    private System.Windows.Media.ImageSource? _frame;

    /// <summary>Starts or stops the playback (a clip that is scrolled out of view or in a background window is stopped).</summary>
    public void SetPlaying(bool play)
    {
        if (LocalPath is null)
        {
            return;
        }

        if (play)
        {
            if (_finished)
            {
                return; // already played its turn; the last frame stays until the tile leaves the view
            }

            if (_stream?.IsPlaying != true)
            {
                _stream ??= new ClipStream(LocalPath, ThumbWidth, ThumbHeight);
                Frame = _stream.Bitmap;
                _stream.Play();
                _limit ??= new System.Windows.Threading.DispatcherTimer { Interval = PlayDuration };
                _limit.Tick -= OnLimitReached;
                _limit.Tick += OnLimitReached;
                if (!_limit.IsEnabled)
                {
                    _limit.Start();
                }
            }
        }
        else
        {
            _limit?.Stop();
            _finished = false;
            _stream?.Stop();
        }
    }

    /// <summary>Starts the play time over: a clip that has finished becomes playable again, one that is playing gets its full time.</summary>
    public void Rearm()
    {
        _limit?.Stop();
        _finished = false;
        _stream?.Stop();
    }

    /// <summary>A clip plays for a few seconds like a short GIF and then holds its last frame, which also lets the GPU rest.</summary>
    private void OnLimitReached(object? sender, EventArgs e)
    {
        _limit?.Stop();
        _finished = true;
        _stream?.Stop();
    }

    public void Dispose()
    {
        _limit?.Stop();
        _stream?.Dispose();
        _stream = null;
    }
}
public sealed record DayBar(string Label, double BarHeight, bool IsToday, string Tooltip);

public sealed record SessionRow(string DateText, string DurationText);

public sealed partial class StarItem : ObservableObject
{
    public StarItem(int index)
    {
        Index = index;
    }

    public int Index { get; }

    [ObservableProperty]
    private bool _isOn;
}

/// <summary>One label/value pair of the Künye card; long values take the whole row.</summary>
public sealed partial class FactItem : ObservableObject, ISpanItem
{
    public FactItem(string label, string value)
    {
        Label = label;
        _value = value;
    }

    public string Label { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Span))]
    private string _value;

    public int Span => Value.Length > 26 ? 2 : 1;
}

/// <summary>
/// Everything the game details panel shows and does for the selected game: header actions,
/// play statistics, the activity chart, and the customizable card layout (order, size, reset).
/// </summary>
public sealed partial class GamePanelViewModel : ObservableObject
{
    private const double MaxBarHeight = 56;

    private static CultureInfo Turkish => AppLanguage.Culture;
    private static readonly Dictionary<string, long> DiskSizeCache = [];

    internal static readonly (string Id, string Title, CardSize Size)[] DefaultCards =
    [
        ("activity", Loc.T("Etkinlik"), CardSize.Full),
        ("info", Loc.T("Künye"), CardSize.Auto),
        ("tags", Loc.T("Türler ve etiketler"), CardSize.Auto),
        ("gallery", Loc.T("Görseller"), CardSize.Auto),
        ("about", Loc.T("Hakkında"), CardSize.Full),
        ("notes", Loc.T("Notlarım"), CardSize.Auto),
        ("launch", Loc.T("Başlatma"), CardSize.Auto),
        ("achievements", Loc.T("Başarımlar"), CardSize.Full),
    ];

    private readonly SettingsService _settingsService;
    private readonly PlayHistoryService _history;
    private string? _aboutKey;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _toastTimer;
    private readonly DispatcherTimer _noticeTimer;
    private LibraryGameViewModel? _game;

    public GamePanelViewModel(LibraryViewModel library, SettingsService settingsService, PlayHistoryService history)
    {
        Library = library;
        _settingsService = settingsService;
        _history = history;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _saveTimer.Tick += async (_, _) =>
        {
            _saveTimer.Stop();
            await SaveNowAsync();
        };

        _noticeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer.Stop();
            IsFolderNoticeOpen = false;
        };

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            ToastMessage = null;
        };

        for (var i = 1; i <= 5; i++)
        {
            Stars.Add(new StarItem(i));
        }

        LoadCards();
        DefaultLayoutChanged += LoadCards;

        Library.PropertyChanged += OnLibraryPropertyChanged;
        Library.SessionCompleted += gameId =>
        {
            if (_game?.Id == gameId)
            {
                _ = LoadActivityAsync();
                _ = LoadAchievementsAsync(refresh: true); // the session may have unlocked something
            }
        };

        AttachGame(Library.SelectedGame);
    }

    public LibraryViewModel Library { get; }

    /// <summary>The game currently selected in the library; null when none.</summary>
    public LibraryGameViewModel? Game => _game;

    public ObservableCollection<DetailCardViewModel> Cards { get; } = [];

    public IReadOnlyList<StatusOption> StatusOptions { get; } =
    [
        new(Loc.T("Listede"), GameStatus.Backlog),
        new(Loc.T("Oynuyorum"), GameStatus.Playing),
        new(Loc.T("Bitirdim"), GameStatus.Finished),
        new(Loc.T("Bıraktım"), GameStatus.Dropped),
    ];

    public ObservableCollection<StarItem> Stars { get; } = [];

    public ObservableCollection<DayBar> DayBars { get; } = [];

    public ObservableCollection<SessionRow> RecentSessions { get; } = [];

    public ObservableCollection<FactItem> Facts { get; } = [];

    public ObservableCollection<string> Genres { get; } = [];

    public ObservableCollection<string> Tags { get; } = [];

    public ObservableCollection<string> Screenshots { get; } = [];

    /// <summary>The description split into headings, paragraphs and list items.</summary>
    public ObservableCollection<object> AboutBlocks { get; } = [];

    // ---- full-size screenshot viewer ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsViewerOpen))]
    [NotifyPropertyChangedFor(nameof(IsOverlayOpen))]
    [NotifyPropertyChangedFor(nameof(ViewerImageUrl))]
    [NotifyPropertyChangedFor(nameof(ViewerCounter))]
    private int _viewerIndex = -1;

    public bool IsViewerOpen => ViewerIndex >= 0;

    public string? ViewerImageUrl => ViewerIndex >= 0 && ViewerIndex < Screenshots.Count ? Screenshots[ViewerIndex] : null;

    public string ViewerCounter => $"{ViewerIndex + 1} / {Screenshots.Count}";

    [RelayCommand]
    private void OpenViewer(string? url)
    {
        var index = url is null ? -1 : Screenshots.IndexOf(url);
        ViewerIndex = index;
    }

    [RelayCommand]
    private void CloseViewer() => ViewerIndex = -1;

    [RelayCommand]
    private void NextScreenshot()
    {
        if (Screenshots.Count > 0)
        {
            ViewerIndex = (ViewerIndex + 1) % Screenshots.Count;
        }
    }

    [RelayCommand]
    private void PreviousScreenshot()
    {
        if (Screenshots.Count > 0)
        {
            ViewerIndex = (ViewerIndex - 1 + Screenshots.Count) % Screenshots.Count;
        }
    }

    /// <summary>Trailer thumbnail (remote) and the video address; both null when the game has none.</summary>
    [ObservableProperty]
    private string? _trailerThumbnailUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGallery))]
    private string? _trailerUrl;

    public bool HasGallery => Screenshots.Count > 0 || _pendingScreenshots > 0 || !string.IsNullOrWhiteSpace(TrailerUrl);

    [ObservableProperty]
    private string _newTag = "";

    [ObservableProperty]
    private string? _toastMessage;

    [ObservableProperty]
    private bool _hasSessions;

    /// <summary>Shows the executable-not-found notice under the folder icon; closes itself after two seconds.</summary>
    [ObservableProperty]
    private bool _isFolderNoticeOpen;

    // ---- proxies for the editable fields (saved shortly after the user stops typing) ----

    public GameStatus Status
    {
        get => _game?.Status ?? GameStatus.Backlog;
        set
        {
            if (_game is null || _game.Status == value)
            {
                return;
            }

            _game.Status = value;
            OnPropertyChanged();
            QueueSave();
        }
    }

    public string? Notes
    {
        get => _game?.Notes;
        set
        {
            if (_game is null || _game.Notes == value)
            {
                return;
            }

            _game.Notes = value;
            OnPropertyChanged();
            QueueSave();
        }
    }

    public string? LaunchArguments
    {
        get => _game?.LaunchArguments;
        set
        {
            if (_game is null || _game.LaunchArguments == value)
            {
                return;
            }

            _game.LaunchArguments = value;
            OnPropertyChanged();
            QueueSave();
        }
    }

    public string? WorkingDirectory
    {
        get => _game?.WorkingDirectory;
        set
        {
            if (_game is null || _game.WorkingDirectory == value)
            {
                return;
            }

            _game.WorkingDirectory = value;
            OnPropertyChanged();
            QueueSave();
        }
    }

    public bool LaunchViaSteam
    {
        get => _game?.LaunchViaSteam ?? false;
        set
        {
            if (_game is null || _game.LaunchViaSteam == value)
            {
                return;
            }

            _game.LaunchViaSteam = value;
            OnPropertyChanged();
            QueueSave();
        }
    }

    /// <summary>The Steam app id as text; non-numeric input is ignored until it parses (empty clears it).</summary>
    public string SteamAppIdText
    {
        get => _game?.SteamAppId?.ToString(CultureInfo.InvariantCulture) ?? "";
        set
        {
            if (_game is null)
            {
                return;
            }

            int? parsed;
            if (string.IsNullOrWhiteSpace(value))
            {
                parsed = null;
            }
            else if (int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0)
            {
                parsed = id;
            }
            else
            {
                return;
            }

            if (_game.SteamAppId == parsed)
            {
                return;
            }

            _game.SteamAppId = parsed;
            OnPropertyChanged();
            QueueSave();
        }
    }

    public bool RunAsAdmin
    {
        get => _game?.RunAsAdmin ?? false;
        set
        {
            if (_game is null || _game.RunAsAdmin == value)
            {
                return;
            }

            _game.RunAsAdmin = value;
            OnPropertyChanged();
            QueueSave();
        }
    }

    public string? LaunchUri
    {
        get => _game?.LaunchUri;
        set
        {
            if (_game is null)
            {
                return;
            }

            var cleaned = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (_game.LaunchUri == cleaned)
            {
                return;
            }

            _game.LaunchUri = cleaned;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLaunchUriInvalid));
            QueueSave();
        }
    }

    /// <summary>True when something is typed that is not a usable launch address (empty is fine).</summary>
    public bool IsLaunchUriInvalid =>
        !string.IsNullOrWhiteSpace(LaunchUri) && !GameLaunchService.IsValidLaunchUri(LaunchUri);

    public string? TrackedProcessName
    {
        get => _game?.TrackedProcessName;
        set
        {
            if (_game is null)
            {
                return;
            }

            var cleaned = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (_game.TrackedProcessName == cleaned)
            {
                return;
            }

            _game.TrackedProcessName = cleaned;
            OnPropertyChanged();
            QueueSave();
        }
    }

    // ---- selected game ----

    private void OnLibraryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryViewModel.SelectedGame))
        {
            AttachGame(Library.SelectedGame);
        }
    }

    private void AttachGame(LibraryGameViewModel? game)
    {
        CloseVideo(); // a video of the previous game must not keep playing behind the next one

        // Flush edits of the previous game before switching.
        if (_saveTimer.IsEnabled)
        {
            _saveTimer.Stop();
            _ = SaveNowAsync();
        }

        _game = game;
        IsEditingLayout = false;
        LoadCards();
        OnPropertyChanged(nameof(Game));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(LaunchArguments));
        OnPropertyChanged(nameof(WorkingDirectory));
        OnPropertyChanged(nameof(LaunchViaSteam));
        OnPropertyChanged(nameof(SteamAppIdText));
        OnPropertyChanged(nameof(TrackedProcessName));
        OnPropertyChanged(nameof(RunAsAdmin));
        OnPropertyChanged(nameof(LaunchUri));
        OnPropertyChanged(nameof(IsLaunchUriInvalid));
        NewTag = "";

        RebuildStars();
        RebuildTags();
        RebuildGenres();
        RebuildFacts();
        RebuildGallery();
        RebuildAbout();
        _ = LoadActivityAsync();
        ShowAllAchievements = false;
        _ = LoadAchievementsAsync(refresh: false);
    }

    private void RebuildStars()
    {
        var rating = _game?.UserRating ?? 0;
        foreach (var star in Stars)
        {
            star.IsOn = star.Index <= rating;
        }
    }

    private void RebuildTags()
    {
        Tags.Clear();
        foreach (var tag in _game?.UserTags ?? [])
        {
            Tags.Add(tag);
        }
    }

    private void RebuildGenres()
    {
        Genres.Clear();
        var genres = (_game?.Genres ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var genre in genres)
        {
            Genres.Add(genre);
        }
    }

    /// <summary>
    /// Rebuilds the "Hakkında" card. Reading the description is instant, but each picture and clip needs a disk check
    /// (is it downloaded? can Windows show it?), so the blocks are built on a background task and swapped in when ready.
    /// </summary>
    private void RebuildAbout()
    {
        var source = !string.IsNullOrWhiteSpace(_game?.DescriptionRich) ? _game!.DescriptionRich! : _game?.Description ?? "";

        // The same game with the same description (e.g. the list reloaded behind the scenes) keeps its tiles,
        // so a clip that has used up its play time does not start over by itself.
        var key = $"{_game?.Id}|{source}";
        if (key == _aboutKey && AboutBlocks.OfType<AboutClip>().All(c =>
                c.LocalPath is not null || SteamMediaCache.LocalPathIfCached(_game!.Id, c.Url) is null))
        {
            return;
        }

        _aboutKey = key;

        // Show nothing of the previous game while the new blocks are prepared.
        foreach (var old in AboutBlocks.OfType<AboutClip>())
        {
            old.Dispose();
        }

        AboutBlocks.Clear();

        var gameId = _game?.Id ?? 0;
        if (_game is null || source.Length == 0)
        {
            return;
        }

        _ = FillAboutAsync(key, gameId, source);
    }

    private async Task FillAboutAsync(string key, int gameId, string source)
    {
        var (blocks, needsAvifExtension) = await Task.Run(() => BuildAboutBlocks(gameId, source));
        if (key != _aboutKey)
        {
            // Superseded by another game; clips made for this one were never shown, so just release them.
            foreach (var clip in blocks.OfType<AboutClip>())
            {
                clip.Dispose();
            }

            return;
        }

        foreach (var block in blocks)
        {
            AboutBlocks.Add(block);
        }

        if (needsAvifExtension)
        {
            AboutBlocks.Insert(0, new AboutNotice(Loc.T(
                "Bu açıklamadaki bazı görseller AVIF biçiminde ve bu bilgisayar onları gösteremiyor. Microsoft'un ücretsiz “AVIF Görüntü Uzantısı”nı yüklersen görünürler (yükledikten sonra oyunu yeniden seç).")));
        }
    }

    /// <summary>Parses the description into blocks (text, downloaded pictures, clips). Does disk checks: call it off the UI thread.</summary>
    private static (List<object> Blocks, bool NeedsAvifExtension) BuildAboutBlocks(int gameId, string source)
    {
        var blocks = new List<object>();
        var clipUrls = new HashSet<string>();
        var needsAvifExtension = false;

        foreach (var raw in source.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("# !", StringComparison.Ordinal))
            {
                line = line[2..]; // a picture that an older save left inside a heading
            }

            if (line.StartsWith("!video ", StringComparison.Ordinal))
            {
                var parts = line[7..].Split('|');
                var url = parts[0];
                if (clipUrls.Add(url))
                {
                    _ = double.TryParse(parts.ElementAtOrDefault(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var width);
                    _ = double.TryParse(parts.ElementAtOrDefault(2), NumberStyles.Float, CultureInfo.InvariantCulture, out var height);
                    blocks.Add(new AboutClip(url, SteamMediaCache.LocalPathIfCached(gameId, url), width, height));
                }

                continue;
            }

            if (line.StartsWith("!image ", StringComparison.Ordinal))
            {
                var imageUrl = line[7..].Trim();
                var local = SteamMediaCache.LocalPathIfCached(gameId, imageUrl);

                // Only downloaded pictures are shown: the web copy may be a format Windows cannot display.
                if (local is not null)
                {
                    if (AvifSupport.IsUndisplayable(local))
                    {
                        needsAvifExtension = true;
                    }
                    else
                    {
                        blocks.Add(new AboutImage(local));
                    }
                }

                continue;
            }

            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                blocks.Add(new AboutText(AboutBlockKind.Heading, line[2..].Trim()));
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("• ", StringComparison.Ordinal))
            {
                blocks.Add(new AboutText(AboutBlockKind.Bullet, "• " + line[2..].Trim()));
            }
            else
            {
                blocks.Add(new AboutText(AboutBlockKind.Paragraph, line));
            }
        }

        return (blocks, needsAvifExtension);
    }

    /// <summary>Opens the Microsoft Store page of the AVIF Image Extension.</summary>
    [RelayCommand]
    private void InstallAvifExtension()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AvifSupport.StoreUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Toast(Loc.F("Açılamadı: {0}", ex.Message));
        }
    }

    /// <summary>Gives every clip a fresh play time (the pointer rested on the description).</summary>
    public void ReplayClips()
    {
        foreach (var clip in AboutBlocks.OfType<AboutClip>())
        {
            clip.Rearm();
        }
    }

    /// <summary>Plays the clips whose tiles are on screen and stops the rest; called by the view as the panel scrolls.</summary>
    public void PlayClips(IReadOnlyCollection<AboutClip> visible)
    {
        foreach (var clip in AboutBlocks.OfType<AboutClip>())
        {
            clip.SetPlaying(visible.Contains(clip));
        }
    }

    /// <summary>Opens a downloaded file (a description picture) in the system's default app.</summary>
    [RelayCommand]
    private void OpenFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(PathForSystemViewer(path)) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Toast(Loc.F("Açılamadı: {0}", ex.Message));
        }
    }

    /// <summary>
    /// Windows does not always have a WebP viewer. A WebP picture is therefore opened as a PNG copy made in the temp folder
    /// (the copy is replaced on every open and cleaned up by Windows' temp cleanup).
    /// </summary>
    private static string PathForSystemViewer(string path)
    {
        if (!path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var image = WebpThumbnails.TryLoad(path);
        if (image is null)
        {
            return path;
        }

        var folder = Path.Combine(Path.GetTempPath(), "Lancir");
        Directory.CreateDirectory(folder);
        var copy = Path.Combine(folder, Path.GetFileNameWithoutExtension(path) + ".png");
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        using (var stream = File.Create(copy))
        {
            encoder.Save(stream);
        }

        return copy;
    }

    [RelayCommand]
    private void OpenClip(AboutClip? clip)
    {
        if (clip is null)
        {
            return;
        }

        // Played in the app's own viewer (a downloaded clip from disk, otherwise straight from its address).
        _ = OpenClipInViewerAsync(clip);
    }

    /// <summary>The downloaded copy of a Steam media address when there is one, otherwise the address itself.</summary>
    private string? Local(string? url) => _game is null ? url : SteamMediaCache.LocalPathIfCached(_game.Id, url) ?? url;

    private int _galleryVersion;
    private int _pendingScreenshots;

    /// <summary>
    /// Clears the gallery at once and refills it on a background task: finding out which pictures are already downloaded
    /// touches the disk once per picture, which used to happen on the UI thread on every game change.
    /// </summary>
    private void RebuildGallery()
    {
        ViewerIndex = -1;
        Screenshots.Clear();
        TrailerUrl = _game?.TrailerUrl;
        TrailerThumbnailUrl = null;

        var version = ++_galleryVersion;
        var game = _game;
        _pendingScreenshots = game?.Screenshots.Count ?? 0;
        OnPropertyChanged(nameof(HasGallery));
        if (game is null)
        {
            return;
        }

        _ = FillGalleryAsync(version, game.Id, game.Screenshots, game.TrailerThumbnailUrl);
    }

    private async Task FillGalleryAsync(int version, int gameId, IReadOnlyList<string> urls, string? trailerThumbnail)
    {
        var (shots, thumb) = await Task.Run(() => (
            urls.Select(url => SteamMediaCache.LocalPathIfCached(gameId, url) ?? url).ToList(),
            trailerThumbnail is null ? null : SteamMediaCache.LocalPathIfCached(gameId, trailerThumbnail) ?? trailerThumbnail));

        if (version != _galleryVersion)
        {
            return; // another game was selected meanwhile
        }

        _pendingScreenshots = 0;
        Screenshots.Clear();
        foreach (var shot in shots)
        {
            Screenshots.Add(shot);
        }

        TrailerThumbnailUrl = thumb;
        OnPropertyChanged(nameof(HasGallery));
    }

    private void RebuildFacts()
    {
        Facts.Clear();
        var game = _game;
        if (game is null)
        {
            return;
        }

        if (game.ReleaseDateUtc is { } release)
        {
            Facts.Add(new FactItem(Loc.T("Yayın tarihi"), release.ToString("d MMMM yyyy", Turkish)));
        }

        if (game.CommunityRating is { } rating)
        {
            Facts.Add(new FactItem(Loc.T("Puan"), $"{rating:F0} / 100"));
        }

        AddFact(Loc.T("Geliştirici"), game.Developers);
        AddFact(Loc.T("Yayıncı"), game.Publishers);
        AddFact(Loc.T("Modlar"), game.Modes);
        AddFact(Loc.T("Yaş sınırı"), game.AgeRating);
        AddFact(Loc.T("Diller"), SummarizeLanguages(game.Languages));

        Facts.Add(new FactItem(Loc.T("Eklenme"), game.AddedText));

        if (game.HasExecutable)
        {
            Facts.Add(new FactItem(Loc.T("Dosya"), Path.GetFileName(game.ExecutablePath!)));

            var version = FileVersionInfo.GetVersionInfo(game.ExecutablePath!).FileVersion;
            if (!string.IsNullOrWhiteSpace(version))
            {
                Facts.Add(new FactItem(Loc.T("Sürüm"), version.Trim()));
            }
        }

        var disk = new FactItem(Loc.T("Disk boyutu"), Loc.T("hesaplanıyor…"));
        Facts.Add(disk);
        _ = ComputeDiskSizeAsync(game.FolderPath, disk, game.Id);

        Facts.Add(new FactItem(Loc.T("Klasör"), game.FolderPath));
    }

    private void AddFact(string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Facts.Add(new FactItem(label, value));
        }
    }

    /// <summary>"14 dil (Türkçe var)" instead of the full list.</summary>
    private static string? SummarizeLanguages(string? languages)
    {
        if (string.IsNullOrWhiteSpace(languages))
        {
            return null;
        }

        var list = languages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hasTurkish = list.Any(l => l.Contains("Türk", StringComparison.OrdinalIgnoreCase) || l.Contains("Turk", StringComparison.OrdinalIgnoreCase));
        return hasTurkish ? Loc.F("{0} dil (Türkçe var)", list.Length) : Loc.F("{0} dil", list.Length);
    }

    private async Task ComputeDiskSizeAsync(string folder, FactItem fact, int gameId)
    {
        if (!DiskSizeCache.TryGetValue(folder, out var bytes))
        {
            bytes = await Task.Run(() => DirectorySize(folder));
            DiskSizeCache[folder] = bytes;
        }

        if (_game?.Id == gameId)
        {
            fact.Value = bytes <= 0 ? "—" : FormatSize(bytes);
        }
    }

    private static long DirectorySize(string folder)
    {
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            return Directory.EnumerateFiles(folder, "*", options).Sum(path => new FileInfo(path).Length);
        }
        catch
        {
            return 0;
        }
    }

    private static string FormatSize(long bytes) => bytes >= 1L << 30
        ? $"{(bytes / (double)(1L << 30)).ToString("0.0", Turkish)} GB"
        : $"{(bytes / (double)(1L << 20)).ToString("0", Turkish)} MB";

    private async Task LoadActivityAsync()
    {
        var game = _game;
        if (game is null)
        {
            DayBars.Clear();
            RecentSessions.Clear();
            HasSessions = false;
            return;
        }

        try
        {
            var activity = await _history.GetActivityAsync(game.Id, 14, 3);
            if (_game?.Id != game.Id)
            {
                return;
            }

            DayBars.Clear();
            var max = activity.DailyMinutes.Count > 0 ? activity.DailyMinutes.Max() : 0;
            var today = DateTime.Now.Date;
            for (var i = 0; i < activity.DailyMinutes.Count; i++)
            {
                var date = today.AddDays(i - (activity.DailyMinutes.Count - 1));
                var minutes = activity.DailyMinutes[i];
                var height = max > 0 ? Math.Max(2, minutes / max * MaxBarHeight) : 2;
                DayBars.Add(new DayBar(
                    i % 2 == 0 ? date.Day.ToString(Turkish) : "",
                    height,
                    i == activity.DailyMinutes.Count - 1,
                    $"{date.ToString("d MMM", Turkish)}: {TimeText.Duration((long)(minutes * 60))}"));
            }

            RecentSessions.Clear();
            foreach (var session in activity.RecentSessions)
            {
                RecentSessions.Add(new SessionRow(
                    $"{TimeText.Relative(session.StartedAtUtc)} {session.StartedAtUtc.ToLocalTime():HH:mm}",
                    TimeText.Duration(session.DurationSeconds)));
            }

            HasSessions = activity.RecentSessions.Count > 0;
        }
        catch (Exception ex)
        {
            Library.ErrorMessage = Loc.F("Oturum geçmişi okunamadı: {0}", ex.Message);
        }
    }

    // ---- saving ----

    private void QueueSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private async Task SaveNowAsync()
    {
        try
        {
            await Library.Service.SaveAsync();
        }
        catch (Exception ex)
        {
            Library.ErrorMessage = Loc.F("Kaydedilemedi: {0}", ex.Message);
        }
    }

    private void Toast(string message)
    {
        ToastMessage = message;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // ---- header actions ----

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        if (_game is null)
        {
            return;
        }

        _game.IsFavorite = !_game.IsFavorite;
        Toast(_game.IsFavorite ? Loc.T("Favorilere eklendi") : Loc.T("Favorilerden çıkarıldı"));
        await SaveNowAsync();
    }

    [RelayCommand]
    private async Task SetRatingAsync(int stars)
    {
        if (_game is null)
        {
            return;
        }

        // Clicking the current rating again clears it.
        _game.UserRating = _game.UserRating == stars ? 0 : stars;
        RebuildStars();
        await SaveNowAsync();
    }

    [RelayCommand]
    private void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Toast(Loc.F("Açılamadı: {0}", ex.Message));
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (_game is null)
        {
            return;
        }

        if (!_game.HasExecutable)
        {
            IsFolderNoticeOpen = true;
            _noticeTimer.Stop();
            _noticeTimer.Start();
            return;
        }

        try
        {
            Process.Start("explorer.exe", $"/select,\"{_game.ExecutablePath}\"");
        }
        catch (Exception ex)
        {
            Toast(Loc.F("Açılamadı: {0}", ex.Message));
        }
    }

    [RelayCommand]
    private void CopyExePath()
    {
        if (string.IsNullOrWhiteSpace(_game?.ExecutablePath))
        {
            Toast(Loc.T("Bu oyun için exe seçilmemiş"));
            return;
        }

        try
        {
            Clipboard.SetText(_game.ExecutablePath);
            Toast(Loc.T("Exe yolu kopyalandı"));
        }
        catch (Exception ex)
        {
            Toast(Loc.F("Kopyalanamadı: {0}", ex.Message));
        }
    }

    [RelayCommand]
    private async Task AddTagAsync()
    {
        var tag = NewTag.Replace(",", " ").Trim();
        if (_game is null || tag.Length == 0)
        {
            return;
        }

        if (!Tags.Any(t => string.Equals(t, tag, StringComparison.CurrentCultureIgnoreCase)))
        {
            Tags.Add(tag);
            _game.SetUserTags(Tags);
            await SaveNowAsync();
        }

        NewTag = "";
    }

    [RelayCommand]
    private async Task RemoveTagAsync(string? tag)
    {
        if (_game is null || tag is null || !Tags.Remove(tag))
        {
            return;
        }

        _game.SetUserTags(Tags);
        await SaveNowAsync();
    }

    // ---- card layout ----

    /// <summary>Raised when the default layout is edited in Settings, so a game without its own layout picks it up.</summary>
    public static event Action? DefaultLayoutChanged;

    public static void NotifyDefaultLayoutChanged() => DefaultLayoutChanged?.Invoke();

    /// <summary>True when the selected game has a card layout of its own (not the default one).</summary>
    [ObservableProperty]
    private bool _hasOwnLayout;

    /// <summary>The layout in force for the selected game: its own when it has one, otherwise the default from Settings, completed with new cards.</summary>
    private List<(string Id, string Title, CardSize Size, bool Hidden, int Span, int Rows)> ResolveLayout(out bool own)
    {
        var settings = _settingsService.Load();
        List<DetailCardSetting> saved = settings.DetailCards;
        own = false;
        if (_game is not null && settings.GameCardLayouts.TryGetValue(_game.Id, out var gameLayout))
        {
            saved = gameLayout;
            own = true;
        }

        var result = new List<(string Id, string Title, CardSize Size, bool Hidden, int Span, int Rows)>();
        var used = new HashSet<string>();
        foreach (var item in saved)
        {
            var definition = DefaultCards.FirstOrDefault(d => d.Id == item.Id);
            if (definition.Id is null || !used.Add(item.Id) || !Enum.TryParse<CardSize>(item.Size, out var size))
            {
                continue;
            }

            result.Add((definition.Id, definition.Title, size, item.Hidden, Math.Clamp(item.Span, 0, 10), Math.Clamp(item.Rows, 0, 40)));
        }

        // Cards added in later versions (or missing from the saved layout) go at the end.
        foreach (var definition in DefaultCards)
        {
            if (used.Add(definition.Id))
            {
                result.Add((definition.Id, definition.Title, definition.Size, false, 0, 0));
            }
        }

        return result;
    }

    public static IReadOnlyList<IntOption> CardColumnOptions { get; } =
    [
        new(0, Loc.T("Otomatik")), ..Enumerable.Range(1, 10).Select(n => new IntOption(n, n.ToString())),
    ];

    public static IReadOnlyList<IntOption> GalleryColumnOptions { get; } = [.. Enumerable.Range(2, 9).Select(n => new IntOption(n, n.ToString()))];

    /// <summary>Columns of cards for the selected game (0 = from the width of the panel).</summary>
    [ObservableProperty]
    private int _cardColumns;

    /// <summary>Pictures per row of the "Görseller" card for the selected game.</summary>
    [ObservableProperty]
    private int _galleryColumns = 2;

    private bool _loadingOptions;

    partial void OnCardColumnsChanged(int value) => SaveOptionsIfEdited();

    partial void OnGalleryColumnsChanged(int value) => SaveOptionsIfEdited();

    private void SaveOptionsIfEdited()
    {
        if (!_loadingOptions)
        {
            SaveCards();
        }
    }

    private void LoadOptions(AppSettings settings, bool own)
    {
        var options = own && _game is not null && settings.GameCardOptions.TryGetValue(_game.Id, out var gameOptions)
            ? gameOptions
            : settings.DefaultCardOptions;
        _loadingOptions = true;
        CardColumns = options.Columns;
        GalleryColumns = Math.Clamp(options.GalleryColumns, 2, 10);
        _loadingOptions = false;
    }

    private void LoadCards()
    {
        var layout = ResolveLayout(out var own);
        HasOwnLayout = own;
        LoadOptions(_settingsService.Load(), own);

        // Same cards in the same order and size: keep them (rebuilding them is not free and would reset scrolling).
        if (Cards.Count == layout.Count && Cards.Zip(layout).All(x => x.First.Id == x.Second.Id && x.First.Size == x.Second.Size && x.First.IsHidden == x.Second.Hidden && x.First.Span == x.Second.Span && x.First.Rows == x.Second.Rows))
        {
            return;
        }

        Cards.Clear();
        foreach (var (id, title, size, hidden, span, rows) in layout)
        {
            Cards.Add(new DetailCardViewModel(id, title, this, size, hidden, span, rows));
        }
    }

    /// <summary>A change made in the panel belongs to the selected game only.</summary>
    private void SaveCards()
    {
        if (_game is null)
        {
            return;
        }

        var settings = _settingsService.Load();
        settings.GameCardLayouts[_game.Id] = CurrentLayout();
        settings.GameCardOptions[_game.Id] = new CardViewOptions { Columns = CardColumns, GalleryColumns = GalleryColumns };
        _settingsService.Save(settings);
        HasOwnLayout = true;
    }

    private List<DetailCardSetting> CurrentLayout() =>
        Cards.Select(c => new DetailCardSetting { Id = c.Id, Size = c.Size.ToString(), Hidden = c.IsHidden, Span = c.Span, Rows = c.Rows }).ToList();

    /// <summary>Moves <paramref name="dragged"/> to <paramref name="target"/>'s position; between two half cards it takes the target's column.</summary>
    public void MoveCard(DetailCardViewModel dragged, DetailCardViewModel target)
    {
        var from = Cards.IndexOf(dragged);
        var to = Cards.IndexOf(target);
        if (from < 0 || to < 0 || from == to)
        {
            return;
        }

        if (dragged.Size != CardSize.Full && target.Size != CardSize.Full)
        {
            dragged.Size = target.Size;
        }

        Cards.Move(from, to);
        SaveCards();
    }

    /// <summary>Pins <paramref name="dragged"/> to a column and places it after the last card of that segment.</summary>
    public void DropCardIntoColumn(DetailCardViewModel dragged, CardSize side, int lastIndexInSegment)
    {
        var from = Cards.IndexOf(dragged);
        if (from < 0)
        {
            return;
        }

        dragged.Size = side;
        Cards.RemoveAt(from);
        var insertAt = from <= lastIndexInSegment ? lastIndexInSegment : lastIndexInSegment + 1;
        Cards.Insert(Math.Clamp(insertAt, 0, Cards.Count), dragged);
        SaveCards();
    }

    public void SetCardSize(DetailCardViewModel card, CardSize size)
    {
        var index = Cards.IndexOf(card);
        if (index < 0)
        {
            return;
        }

        card.Size = size;
        card.Span = 0;

        // Re-inserting makes the items control rebuild the container, which re-runs the panel layout.
        Cards.RemoveAt(index);
        Cards.Insert(index, card);
        SaveCards();
    }

    /// <summary>Edit mode of the cards: grips, size buttons and the show/hide switches appear; hidden cards show dimmed.</summary>
    [ObservableProperty]
    private bool _isEditingLayout;

    partial void OnIsEditingLayoutChanged(bool value)
    {
        foreach (var card in Cards)
        {
            card.RefreshShown();
        }
    }

    [RelayCommand]
    private void ToggleEditLayout() => IsEditingLayout = !IsEditingLayout;

    /// <summary>Shows or hides a card for the selected game only.</summary>
    public void ToggleCardHidden(DetailCardViewModel card)
    {
        card.IsHidden = !card.IsHidden;
        SaveCards();
    }

    /// <summary>The number of columns on screen right now, reported by the layout panel.</summary>
    [ObservableProperty]
    private int _actualCardColumns = 2;

    partial void OnActualCardColumnsChanged(int value)
    {
        foreach (var card in Cards)
        {
            card.RefreshSpanText();
        }
    }

    /// <summary>Makes a card one column wider or narrower (never beyond the columns on screen); all columns is stored as "full width" so it keeps following the column count.</summary>
    public void ChangeCardSpan(DetailCardViewModel card, int delta)
    {
        var index = Cards.IndexOf(card);
        if (index < 0)
        {
            return;
        }

        var columns = Math.Max(1, ActualCardColumns);
        var span = Math.Clamp(card.EffectiveSpan(columns) + delta, 1, columns);
        if (span >= columns && columns > 1)
        {
            card.Size = CardSize.Full;
            card.Span = 0;
        }
        else
        {
            if (card.Size == CardSize.Full)
            {
                card.Size = CardSize.Auto;
            }

            card.Span = span;
        }

        card.RefreshSpanText();
        Cards.RemoveAt(index);
        Cards.Insert(index, card);
        SaveCards();
    }

    /// <summary>The card's width in columns and height in rows, as set by dragging its corner (applied live; <see cref="SaveCardShape"/> keeps it).</summary>
    public void SetCardShape(DetailCardViewModel card, int span, int rows)
    {
        var columns = Math.Max(1, ActualCardColumns);
        span = Math.Clamp(span, 1, columns);
        if (span >= columns && columns > 1)
        {
            card.Size = CardSize.Full;
            card.Span = 0;
        }
        else
        {
            if (card.Size == CardSize.Full)
            {
                card.Size = CardSize.Auto;
            }

            card.Span = span;
        }

        card.Rows = Math.Clamp(rows, 1, 40);
        card.RefreshSpanText();
    }

    public void SaveCardShape() => SaveCards();

    /// <summary>The card is as tall as its content again.</summary>
    public void ResetCardHeight(DetailCardViewModel card)
    {
        card.Rows = 0;
        SaveCards();
    }

    public void ToggleCardSize(DetailCardViewModel card) =>
        SetCardSize(card, card.Size == CardSize.Full ? CardSize.Auto : CardSize.Full);

    /// <summary>The selected game goes back to the default layout (its own one is forgotten).</summary>
    public void ResetLayout()
    {
        if (_game is not null)
        {
            var settings = _settingsService.Load();
            var removed = settings.GameCardLayouts.Remove(_game.Id);
            removed |= settings.GameCardOptions.Remove(_game.Id);
            if (removed)
            {
                _settingsService.Save(settings);
            }
        }

        LoadCards();
        Toast(Loc.T("Varsayılan düzen geri yüklendi"));
    }

    /// <summary>The selected game's layout becomes the default and replaces the own layout of every game.</summary>
    public void ApplyLayoutToAllGames()
    {
        var settings = _settingsService.Load();
        settings.DetailCards = CurrentLayout();
        settings.GameCardLayouts.Clear();
        settings.DefaultCardOptions = new CardViewOptions { Columns = CardColumns, GalleryColumns = GalleryColumns };
        settings.GameCardOptions.Clear();
        _settingsService.Save(settings);
        HasOwnLayout = false;
        Toast(Loc.T("Düzen tüm oyunlara uygulandı"));
    }
}
