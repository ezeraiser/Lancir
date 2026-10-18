using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Net.Http;
using Lancir.App.Services;
using Lancir.Core.GridImages;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Media;
using System.Windows.Threading;

namespace Lancir.App.ViewModels;

/// <summary>The in-app video viewer (fullscreen layer like the screenshot viewer): the store trailer and the description clips.</summary>
public sealed partial class GamePanelViewModel
{
    private VideoStream? _video;
    private DispatcherTimer? _videoTimer;
    private int _videoOpenVersion;
    private DateTime _videoOpenedUtc;

    /// <summary>A video that has shown nothing after this long is reported as failed instead of loading forever.</summary>
    private static readonly TimeSpan VideoStartTimeout = TimeSpan.FromSeconds(25);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVideoLoading))]
    [NotifyPropertyChangedFor(nameof(IsOverlayOpen))]
    private bool _isVideoOpen;

    /// <summary>True while any fullscreen layer (picture or video viewer) covers the panel, so the panel's own close button steps aside.</summary>
    public bool IsOverlayOpen => IsVideoOpen || IsViewerOpen;

    /// <summary>The video is on its way (looking it up, opening, buffering): a spinner is shown, not text.</summary>
    public bool IsVideoLoading => IsVideoOpen && VideoMessage is not null && !IsVideoFailed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVideoLoading))]
    private bool _isVideoFailed;

    [ObservableProperty]
    private ImageSource? _videoFrame;

    [ObservableProperty]
    private string? _videoTitle;

    /// <summary>"Loading..." / "could not open"; null while a picture is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVideoLoading))]
    private string? _videoMessage;

    /// <summary>The store page, offered when the video cannot be played here.</summary>
    [ObservableProperty]
    private string? _videoFallbackUrl;

    [ObservableProperty]
    private bool _isVideoPlaying;

    /// <summary>Position in the video, 0 to 1 (the seek bar).</summary>
    [ObservableProperty]
    private double _videoPosition;

    [ObservableProperty]
    private string _videoTimeText = "";

    [ObservableProperty]
    private int _videoVolume = 80;

    [ObservableProperty]
    private bool _isVideoMuted;

    /// <summary>True while the user drags the seek bar, so the clock does not move it under their finger.</summary>
    public bool VideoScrubbing { get; set; }

    partial void OnVideoVolumeChanged(int value)
    {
        if (_video is not null)
        {
            _video.Volume = value;
        }
    }

    partial void OnIsVideoMutedChanged(bool value)
    {
        if (_video is not null)
        {
            _video.Mute = value;
        }
    }

    /// <summary>Plays the store trailer. Steam serves it as an HLS stream; older saved data only has the store page, so the stream address is looked up first.</summary>
    [RelayCommand]
    private async Task OpenTrailerAsync()
    {
        var url = TrailerUrl;
        if (string.IsNullOrWhiteSpace(url) || _game is null)
        {
            return;
        }

        var game = _game;
        var storePage = game.SteamAppId is { } id ? $"https://store.steampowered.com/app/{id}/" : url;
        var source = url;
        if (!IsPlayableAddress(source) && game.SteamAppId is { } appId)
        {
            // saved before streams were kept: ask the store for the current trailer address
            ShowVideoLayer(game.DisplayName, Loc.T("Fragman aranıyor…"), storePage);
            try
            {
                var details = await App.Services.GetRequiredService<ISteamStoreClient>().GetDetailsAsync(appId);
                source = details?.Extra.TrailerUrl ?? source;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                source = url;
            }

            if (!IsVideoOpen)
            {
                return; // closed while looking
            }
        }

        if (!IsPlayableAddress(source))
        {
            CloseVideo();
            OpenUrl(source); // no stream to play: the store page, as before
            return;
        }

        // a trailer watched before plays from its copy on disk (instant, no network)
        await OpenVideoAsync(TrailerCache.IsCacheable(source) ? TrailerCache.TryGetLocal(source) ?? source : source, game.DisplayName, storePage);
    }

    /// <summary>Plays a description clip in the viewer.</summary>
    private Task OpenClipInViewerAsync(AboutClip clip) =>
        OpenVideoAsync(clip.LocalPath ?? clip.Url, _game?.DisplayName ?? "", clip.Url);

    private async Task OpenVideoAsync(string source, string title, string? fallbackUrl)
    {
        ShowVideoLayer(title, Loc.T("Video yükleniyor…"), fallbackUrl);
        var version = _videoOpenVersion;

        var stream = await VideoStream.OpenAsync(source, VideoVolume);
        if (version != _videoOpenVersion || !IsVideoOpen)
        {
            stream?.Dispose(); // closed (or another video chosen) while it was opening
            return;
        }

        if (stream is null)
        {
            IsVideoFailed = true;
            VideoMessage = Loc.T("Video açılamadı.");
            return;
        }

        _video = stream;
        stream.Mute = IsVideoMuted;
        stream.Ended += OnVideoEnded;
        stream.Failed += OnVideoFailed;
        _videoOpenedUtc = DateTime.UtcNow;
        VideoFrame = stream.Bitmap;
        IsVideoPlaying = true;
        _videoTimer ??= CreateVideoTimer();
        _videoTimer.Start();
    }

    private void ShowVideoLayer(string title, string message, string? fallbackUrl)
    {
        CloseVideo();
        _videoOpenVersion++;
        VideoTitle = title;
        IsVideoFailed = false;
        VideoMessage = message;
        VideoFallbackUrl = fallbackUrl;
        VideoPosition = 0;
        VideoTimeText = "";
        IsVideoPlaying = false;
        IsVideoOpen = true;
    }

    [RelayCommand]
    private void CloseVideo()
    {
        _videoOpenVersion++;
        _videoTimer?.Stop();
        if (_video is not null)
        {
            _video.Ended -= OnVideoEnded;
            _video.Failed -= OnVideoFailed;
            _video.Dispose();
            _video = null;
        }

        IsVideoOpen = false;
        VideoFrame = null;
        VideoMessage = null;
        IsVideoPlaying = false;
    }

    /// <summary>Skips 10 seconds back (Left key).</summary>
    [RelayCommand]
    private void VideoBack() => SkipVideo(-10_000);

    /// <summary>Skips 10 seconds ahead (Right key).</summary>
    [RelayCommand]
    private void VideoForward() => SkipVideo(10_000);

    private void SkipVideo(long ms)
    {
        if (_video is null)
        {
            return;
        }

        _video.SeekBy(ms);
        VideoPosition = Math.Clamp(_video.Position, 0, 1);
        VideoTimeText = $"{FormatTime(_video.TimeMs)} / {FormatTime(_video.LengthMs)}";
    }

    /// <summary>While the seek bar is dragged only the clock follows; the video jumps when it is let go.</summary>
    public void PreviewVideoSeek(double fraction)
    {
        if (_video is { LengthMs: > 0 } video)
        {
            VideoTimeText = $"{FormatTime((long)(fraction * video.LengthMs))} / {FormatTime(video.LengthMs)}";
        }
    }

    [RelayCommand]
    private void ToggleVideoMute() => IsVideoMuted = !IsVideoMuted;

    [RelayCommand]
    private void ToggleVideoPlay()
    {
        if (_video is null)
        {
            return;
        }

        _video.TogglePause();
        IsVideoPlaying = _video.IsPlaying;
    }

    /// <summary>Jumps to a position (0 to 1) in the video.</summary>
    public void SeekVideo(double fraction)
    {
        if (_video is not null)
        {
            _video.Position = fraction;
        }
    }

    [RelayCommand]
    private void OpenVideoFallback() => OpenUrl(VideoFallbackUrl);

    private DispatcherTimer CreateVideoTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            if (_video is not { } video)
            {
                return;
            }

            IsVideoPlaying = video.IsPlaying;
            if (video.TimeMs > 0)
            {
                VideoMessage = null; // pictures are arriving
            }
            else if (DateTime.UtcNow - _videoOpenedUtc > VideoStartTimeout)
            {
                OnVideoFailed();
                return;
            }

            if (!VideoScrubbing)
            {
                VideoPosition = Math.Clamp(video.Position, 0, 1);
            }

            VideoTimeText = $"{FormatTime(video.TimeMs)} / {FormatTime(video.LengthMs)}";
        };
        return timer;
    }

    private void OnVideoFailed()
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (_video is null)
            {
                return;
            }

            _videoTimer?.Stop();
            VideoFrame = null;
            IsVideoFailed = true;
            VideoMessage = Loc.T("Video açılamadı.");
            IsVideoPlaying = false;
        });
    }

    private void OnVideoEnded()
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            IsVideoPlaying = false;
            VideoPosition = 1;
        });
    }

    private static string FormatTime(long ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    /// <summary>True for an address libVLC can play here: a file or an HLS/DASH/mp4/webm address (not a store web page).</summary>
    private static bool IsPlayableAddress(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        if (!source.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return true; // a local file
        }

        var path = Uri.TryCreate(source, UriKind.Absolute, out var uri) ? uri.AbsolutePath : source;
        return path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase);
    }
}
