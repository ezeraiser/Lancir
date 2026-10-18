using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace Lancir.App.Services;

/// <summary>
/// The icon in the notification area (bottom right of the taskbar). Written directly against the Windows shell API instead of
/// pulling in Windows Forms just for <c>NotifyIcon</c>: a hidden window receives the clicks, the right-click menu is a normal
/// WPF context menu, and the icon is put back if Explorer restarts.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int WmUser = 0x0400;
    private const int CallbackMessage = WmUser + 1;
    private const int WmLButtonUp = 0x0202;
    private const int WmLButtonDblClk = 0x0203;
    private const int WmRButtonUp = 0x0205;
    private const uint NimAdd = 0;
    private const uint NimModify = 1;
    private const uint NimDelete = 2;
    private const uint NifMessage = 1;
    private const uint NifIcon = 2;
    private const uint NifTip = 4;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExToolWindow = 0x80;

    private readonly HwndSource _window;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly Icon _icon;
    private readonly string _tooltip;
    private readonly ContextMenu _menu;
    private bool _visible;

    public TrayIcon(string tooltip, string openText, string exitText)
    {
        _tooltip = tooltip;

        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Lancir.ico"))
                       ?? throw new InvalidOperationException("Lancir.ico resource not found");
        using (resource.Stream)
        {
            _icon = new Icon(resource.Stream);
        }

        // A real (but empty, never shown) top-level window, not a message-only one: SetForegroundWindow needs it so that the
        // context menu closes when the user clicks elsewhere.
        _window = new HwndSource(new HwndSourceParameters("LancirTray")
        {
            Width = 0,
            Height = 0,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = WsPopup,
            ExtendedWindowStyle = WsExToolWindow,
        });
        _window.AddHook(WndProc);

        var open = new MenuItem { Header = openText, FontWeight = FontWeights.SemiBold };
        open.Click += (_, _) => Activated?.Invoke();
        var exit = new MenuItem { Header = exitText };
        exit.Click += (_, _) => ExitRequested?.Invoke();
        _menu = new ContextMenu { Items = { open, new Separator(), exit } };
    }

    /// <summary>The icon was clicked (left button): bring the window back.</summary>
    public event Action? Activated;

    /// <summary>"Çıkış" was chosen in the menu.</summary>
    public event Action? ExitRequested;

    /// <summary>Shows or hides the icon in the notification area.</summary>
    public bool IsVisible
    {
        get => _visible;
        set
        {
            if (_visible == value)
            {
                return;
            }

            _visible = value;
            if (value)
            {
                Notify(NimAdd);
            }
            else
            {
                Notify(NimDelete);
            }
        }
    }

    public void Dispose()
    {
        IsVisible = false;
        _window.RemoveHook(WndProc);
        _window.Dispose();
        _icon.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            switch ((int)(lParam.ToInt64() & 0xFFFF))
            {
                case WmLButtonUp:
                case WmLButtonDblClk:
                    Activated?.Invoke();
                    handled = true;
                    break;
                case WmRButtonUp:
                    ShowMenu();
                    handled = true;
                    break;
            }
        }
        else if (msg == _taskbarCreated && _visible)
        {
            Notify(NimAdd); // Explorer restarted: its notification area is empty again
        }

        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        SetForegroundWindow(_window.Handle);
        _menu.Placement = PlacementMode.MousePoint;
        _menu.IsOpen = true;
    }

    private void Notify(uint message)
    {
        var data = new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = _window.Handle,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon.Handle,
            szTip = _tooltip,
        };

        if (!Shell_NotifyIcon(message, ref data) && message == NimAdd)
        {
            // An old icon with the same id may be left over (e.g. a crashed run): replace it.
            Shell_NotifyIcon(NimModify, ref data);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
