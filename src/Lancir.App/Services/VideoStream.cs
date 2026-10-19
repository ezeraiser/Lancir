using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace Lancir.App.Services;

/// <summary>
/// Plays one video inside the app, with sound, for the video viewer: a local clip or a web address (the Steam trailers are HLS
/// streams). Like <see cref="ClipStream"/> it uses the libVLC that ships next to the exe and copies the frames into a
/// <see cref="WriteableBitmap"/>, so no window, no second process and no extra video package; unlike it, it plays once (no loop),
/// has audio, and can be paused, sought and set to a volume. Frames are the size of the video (at most 1280 pixels wide).
/// </summary>
public sealed class VideoStream : IDisposable
{
    private const int MaxWidth = 1280;
    private static readonly TimeSpan ParseTimeout = TimeSpan.FromSeconds(12);

    private static readonly object Gate = new();
    private static LibVLC? _libVlc;
    private static int _players; // videos open: the shared libVLC is only released while there are none

    private readonly Dispatcher _dispatcher;
    private readonly IntPtr _buffer;
    private readonly int _width;
    private readonly int _height;
    private readonly MediaPlayer.LibVLCVideoLockCb _lock;
    private readonly MediaPlayer.LibVLCVideoDisplayCb _display;
    private MediaPlayer? _player;
    private Media? _media;
    private int _pending;
    private long _lastFrameTicks;
    private bool _disposed;

    private VideoStream(int width, int height)
    {
        _width = width;
        _height = height;
        _dispatcher = Application.Current.Dispatcher;
        _buffer = Marshal.AllocHGlobal(width * height * 4);
        Bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);

        // kept in fields so the delegates are not collected while libVLC still holds them
        _lock = (_, planes) =>
        {
            Marshal.WriteIntPtr(planes, _buffer);
            return IntPtr.Zero;
        };
        _display = (_, _) => OnFrame();
    }

    /// <summary>The picture of the current frame; updated while the video plays.</summary>
    public WriteableBitmap Bitmap { get; }

    /// <summary>Raised (on a background thread) when the video reaches its end.</summary>
    public event Action? Ended;

    /// <summary>Raised (on a background thread) when libVLC cannot play the video (a dead address, an unsupported stream).</summary>
    public event Action? Failed;

    public bool IsPlaying => _player?.IsPlaying == true;

    /// <summary>Position in the video, 0 to 1.</summary>
    public double Position
    {
        get => _player?.Position ?? 0;
        set
        {
            if (_player is { IsSeekable: true } player)
            {
                player.Position = (float)Math.Clamp(value, 0, 1);
            }
        }
    }

    /// <summary>Jumps forward or back by a number of milliseconds (kept inside the video).</summary>
    public void SeekBy(long deltaMs)
    {
        if (_player is { IsSeekable: true, Length: > 0 } player)
        {
            player.Time = Math.Clamp(player.Time + deltaMs, 0, Math.Max(0, player.Length - 500));
        }
    }

    public long TimeMs => _player?.Time ?? 0;

    public long LengthMs => _player?.Length ?? 0;

    /// <summary>Volume, 0 to 100.</summary>
    public int Volume
    {
        get => _player?.Volume ?? 0;
        set
        {
            if (_player is not null)
            {
                _player.Volume = Math.Clamp(value, 0, 100);
            }
        }
    }

    public bool Mute
    {
        get => _player?.Mute == true;
        set
        {
            if (_player is not null)
            {
                _player.Mute = value;
            }
        }
    }

    /// <summary>
    /// Opens a local file or a web address and starts playing it. Returns null when it cannot be opened (no libVLC next to the
    /// exe, an address that does not work, a format libVLC does not have). Must be called on the UI thread.
    /// </summary>
    public static async Task<VideoStream?> OpenAsync(string source, int volume, CancellationToken cancellationToken = default)
    {
        LibVLC library;
        lock (Gate)
        {
            try
            {
                library = Library();
            }
            catch (Exception ex) when (ex is DllNotFoundException or VLCException or BadImageFormatException or InvalidOperationException)
            {
                System.Diagnostics.Debug.WriteLine($"VideoStream: {ex.Message}");
                return null;
            }

            _players++; // counted before libVLC is used, so ReleaseIfIdle cannot take it away in between
        }

        var handedOver = false; // the stream itself gives the count back when it is disposed
        try
        {
            var isFile = !source.StartsWith("http", StringComparison.OrdinalIgnoreCase);
            if (isFile && !File.Exists(source))
            {
                return null;
            }

            var media = new Media(library, source, isFile ? FromType.FromPath : FromType.FromLocation);

            // Find out the picture size first (the frame buffer has to be sized before playing): quick for a file, a request for a stream.
            var (width, height) = (MaxWidth, MaxWidth * 9 / 16);
            // A stream is not parsed first: that costs seconds and Steam's trailers are 16:9; only a local file is measured.
            var parsing = isFile && !source.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ? media.Parse(MediaParseOptions.ParseLocal, (int)ParseTimeout.TotalMilliseconds, cancellationToken) : null;
            if (parsing is not null && await Task.WhenAny(parsing, Task.Delay(ParseTimeout, cancellationToken)) == parsing && parsing.IsCompletedSuccessfully)
            {
                var video = media.Tracks.FirstOrDefault(t => t.TrackType == TrackType.Video);
                if (video.Data.Video.Width > 0 && video.Data.Video.Height > 0)
                {
                    // A file is shown at its own size (at most 1280 wide); a stream is shown at 1280 wide whichever quality level
                    // the parse happened to see first (the player settles on the best one it is allowed, see Library()).
                    var scale = isFile ? Math.Min(1.0, (double)MaxWidth / video.Data.Video.Width) : (double)MaxWidth / video.Data.Video.Width;
                    width = Math.Max(16, (int)Math.Round(video.Data.Video.Width * scale / 2) * 2);
                    height = Math.Max(16, (int)Math.Round(video.Data.Video.Height * scale / 2) * 2);
                }
            }

            // the picture (a WPF object) belongs to the UI thread, whichever thread the awaits above resumed on
            var stream = await Application.Current.Dispatcher.InvokeAsync(() => new VideoStream(width, height));
            stream._media = media;
            stream._player = new MediaPlayer(media) { Volume = Math.Clamp(volume, 0, 100) };
            stream._player.SetVideoFormat("RV32", (uint)width, (uint)height, (uint)(width * 4));
            stream._player.SetVideoCallbacks(stream._lock, null, stream._display);
            stream._player.EndReached += (_, _) => stream.Ended?.Invoke();
            stream._player.EncounteredError += (_, _) => stream.Failed?.Invoke();
            stream._player.Play();
            if (!isFile && TrailerCache.IsCacheable(source))
            {
                TrailerCache.CacheInBackground(source); // watched once: the next time it plays from disk
            }

            handedOver = true;
            return stream;
        }
        catch (Exception ex) when (ex is DllNotFoundException or VLCException or BadImageFormatException or OperationCanceledException
                                       or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"VideoStream: {ex.Message}");
            return null;
        }
        finally
        {
            if (!handedOver)
            {
                Interlocked.Decrement(ref _players);
            }
        }
    }

    public void Play() => _player?.Play();

    public void Pause() => _player?.SetPause(true);

    public void TogglePause()
    {
        if (_player is null)
        {
            return;
        }

        if (_player.State == VLCState.Ended || _player.State == VLCState.Stopped)
        {
            _player.Stop();
            _player.Play(); // from the start again
        }
        else
        {
            _player.Pause();
        }
    }

    private void OnFrame()
    {
        // ~30 pictures a second is plenty for a trailer; at most one copy waits for the UI thread, the rest are dropped.
        var now = Environment.TickCount64;
        if (_disposed || now - _lastFrameTicks < 30 || Interlocked.Exchange(ref _pending, 1) == 1)
        {
            return;
        }

        _lastFrameTicks = now;
        _dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            try
            {
                if (!_disposed)
                {
                    Bitmap.Lock();
                    unsafe
                    {
                        Buffer.MemoryCopy((void*)_buffer, (void*)Bitmap.BackBuffer, _width * _height * 4L, _width * _height * 4L);
                    }

                    Bitmap.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
                    Bitmap.Unlock();
                }
            }
            finally
            {
                Interlocked.Exchange(ref _pending, 0);
            }
        });
    }

    private static LibVLC Library()
    {
        lock (Gate)
        {
            if (_libVlc is null)
            {
                LibVLCSharp.Shared.Core.Initialize(Path.Combine(AppContext.BaseDirectory, "libvlc"));

                // Unlike the clip tiles this one has sound; the network cache smooths out a stream's start.
                _libVlc = new LibVLC(
                    "--no-osd", "--no-stats", "--no-video-title-show", "--no-snapshot-preview", "--quiet", "--no-plugins-cache",
                    "--network-caching=3000", // a stream starts smoother with a longer head start (a few MB); local files are not affected
                    // a trailer is a stream with several quality levels: take the best one up to 720p instead of the lowest
                    "--adaptive-logic=highest", "--adaptive-maxwidth=1280", "--adaptive-maxheight=720");
            }

            return _libVlc;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var player = _player;
        var media = _media;
        _player = null;
        _media = null;

        // Stopped off the UI thread: libVLC can take a moment to wind down; the buffer is freed once it stopped writing.
        Task.Run(() =>
        {
            try
            {
                player?.Stop();
            }
            finally
            {
                player?.Dispose();
                media?.Dispose();
                Marshal.FreeHGlobal(_buffer);
                Interlocked.Decrement(ref _players);
            }
        });
    }

    /// <summary>Releases the shared libVLC instance (its threads and memory) when no video is open; the next video creates it again.</summary>
    public static void ReleaseIfIdle()
    {
        lock (Gate)
        {
            if (_players == 0 && _libVlc is not null)
            {
                _libVlc.Dispose();
                _libVlc = null;
            }
        }
    }

    /// <summary>Releases the shared libVLC instance when the app exits.</summary>
    public static void ShutDown()
    {
        lock (Gate)
        {
            _libVlc?.Dispose();
            _libVlc = null;
        }
    }
}
