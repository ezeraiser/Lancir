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
/// Plays one short clip silently and in a loop, like a GIF, inside the app: libVLC (a trimmed copy that
/// ships next to the exe) decodes it on the CPU and hands over small frames, which are copied into a
/// <see cref="WriteableBitmap"/> the tile shows. No window, no GPU decoding, no second process.
/// </summary>
public sealed class ClipStream : IDisposable
{
    private const long MinFrameIntervalMs = 66;

    private static readonly object Gate = new();
    private static LibVLC? _libVlc;

    private readonly string _path;
    private readonly int _width;
    private readonly int _height;
    private readonly Dispatcher _dispatcher;
    private readonly IntPtr _buffer;
    private readonly MediaPlayer.LibVLCVideoLockCb _lock;
    private readonly MediaPlayer.LibVLCVideoDisplayCb _display;
    private MediaPlayer? _player;
    private Media? _media;
    private int _pending;
    private long _lastFrameTicks;
    private bool _disposed;

    public ClipStream(string path, int width, int height)
    {
        _path = path;
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

    /// <summary>The picture of the clip's current frame; updated while the clip plays.</summary>
    public WriteableBitmap Bitmap { get; }

    public bool IsPlaying => _player?.IsPlaying == true;

    private static LibVLC Library()
    {
        lock (Gate)
        {
            if (_libVlc is null)
            {
                LibVLCSharp.Shared.Core.Initialize(Path.Combine(AppContext.BaseDirectory, "libvlc"));

                // CPU decoding with the loop filter skipped keeps a playing clip at a few percent of one core.
                _libVlc = new LibVLC(
                    "--no-audio", "--avcodec-hw=none", "--avcodec-skiploopfilter=4", "--avcodec-threads=2",
                    "--no-osd", "--no-stats", "--no-video-title-show", "--no-snapshot-preview", "--quiet",
                    "--no-plugins-cache");
            }

            return _libVlc;
        }
    }

    public void Play()
    {
        if (_disposed || IsPlaying)
        {
            return;
        }

        try
        {
            _player ??= CreatePlayer();
            _player.Play();
        }
        catch (Exception ex) when (ex is DllNotFoundException or VLCException or BadImageFormatException)
        {
            // no libVLC next to the exe: the tile simply stays still
            System.Diagnostics.Debug.WriteLine($"ClipStream: {ex.Message}");
        }
    }

    public void Stop()
    {
        if (_player is { } player)
        {
            // Stopped off the UI thread: libVLC can take a moment to wind down.
            _player = null;
            var media = _media;
            _media = null;
            Task.Run(() =>
            {
                try
                {
                    player.Stop();
                }
                finally
                {
                    player.Dispose();
                    media?.Dispose();
                }
            });
        }
    }

    private MediaPlayer CreatePlayer()
    {
        _media = new Media(Library(), _path, FromType.FromPath);
        _media.AddOption(":input-repeat=65535");

        var player = new MediaPlayer(_media);
        player.SetVideoFormat("RV32", (uint)_width, (uint)_height, (uint)(_width * 4));
        player.SetVideoCallbacks(_lock, null, _display);
        return player;
    }

    private void OnFrame()
    {
        // A GIF-like loop does not need more than ~15 pictures a second; skipping the rest halves the drawing work.
        var now = Environment.TickCount64;
        if (_disposed || now - _lastFrameTicks < MinFrameIntervalMs)
        {
            return;
        }

        // At most one copy waits for the UI thread; frames that arrive meanwhile are dropped.
        if (Interlocked.Exchange(ref _pending, 1) == 1)
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();

        // The buffer is released once libVLC has stopped writing into it.
        Task.Delay(1000).ContinueWith(_ => Marshal.FreeHGlobal(_buffer));
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
