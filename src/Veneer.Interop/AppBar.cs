using System.Runtime.InteropServices;
using Veneer.Core.Layout;
using Veneer.Core.Themes;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Veneer.Interop;

/// <summary>
/// Keeps one window on a screen edge. With <c>reserveSpace</c> it registers as a shell app bar, so maximized
/// windows stay clear of it. Feed every window message to <see cref="WndProc"/>.
/// </summary>
public sealed class AppBar(nint hwnd)
{
    const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3, ABM_ACTIVATE = 6, ABM_WINDOWPOSCHANGED = 9;
    const uint ABN_POSCHANGED = 1;
    const int WM_ACTIVATE = 0x0006, WM_WINDOWPOSCHANGING = 0x0046, WM_WINDOWPOSCHANGED = 0x0047;
    static readonly uint CallbackMessage = PInvoke.RegisterWindowMessage("Veneer.AppBar");

    readonly HWND handle = (HWND)hwnd;
    Edge edge;
    PixelRect desired;
    bool reserve;
    bool registered;

    /// <summary>Where the window actually is, in physical pixels (the shell may have adjusted the request).</summary>
    public PixelRect Bounds { get; private set; }

    /// <summary>Raised when the shell moved the bar (another app bar appeared, the taskbar changed...).</summary>
    public event Action? Moved;

    public void Place(Edge edge, PixelRect desired, bool reserveSpace)
    {
        this.edge = edge;
        this.desired = desired;
        reserve = reserveSpace;
        if (reserve && !registered)
        {
            var data = Data();
            registered = PInvoke.SHAppBarMessage(ABM_NEW, ref data) != 0;
        }
        else if (!reserve && registered)
        {
            Remove();
        }

        Reposition();
    }

    public void Remove()
    {
        if (!registered)
            return;
        var data = Data();
        PInvoke.SHAppBarMessage(ABM_REMOVE, ref data);
        registered = false;
    }

    /// <summary>Explorer restarted and forgot every app bar: register again.</summary>
    public void OnShellRestarted()
    {
        registered = false;
        if (Bounds.Width > 0)
            Place(edge, desired, reserve);
    }

    void Reposition()
    {
        var rect = desired;
        if (registered)
        {
            var data = Data();
            data.rc = ToRect(desired);
            PInvoke.SHAppBarMessage(ABM_QUERYPOS, ref data);
            // The shell may move our edge; keep our thickness from the edge it gave back.
            rect = KeepThickness(FromRect(data.rc), desired, edge);
            data.rc = ToRect(rect);
            PInvoke.SHAppBarMessage(ABM_SETPOS, ref data);
            rect = KeepThickness(FromRect(data.rc), desired, edge);
        }

        Bounds = rect;
        PInvoke.SetWindowPos(handle, (HWND)(nint)(-1) /* HWND_TOPMOST */, rect.Left, rect.Top, rect.Width, rect.Height, SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    static PixelRect KeepThickness(PixelRect r, PixelRect desired, Edge edge) => edge switch
    {
        Edge.Top => r with { Bottom = r.Top + desired.Height },
        Edge.Bottom => r with { Top = r.Bottom - desired.Height },
        Edge.Left => r with { Right = r.Left + desired.Width },
        _ => r with { Left = r.Right - desired.Width },
    };

    /// <summary>Call for every message of the window. Returns true when the message is fully handled.</summary>
    public unsafe bool WndProc(int msg, nint wParam, nint lParam)
    {
        if (msg == WM_WINDOWPOSCHANGING && Bounds.Width > 0)
        {
            // The system moves an app bar into the work area it just reserved: keep it where the shell put it.
            var pos = (WINDOWPOS*)lParam;
            if ((pos->flags & SET_WINDOW_POS_FLAGS.SWP_NOMOVE) == 0)
            {
                pos->x = Bounds.Left;
                pos->y = Bounds.Top;
            }

            if ((pos->flags & SET_WINDOW_POS_FLAGS.SWP_NOSIZE) == 0)
            {
                pos->cx = Bounds.Width;
                pos->cy = Bounds.Height;
            }

            return false;
        }

        if (!registered)
            return false;
        if (msg == CallbackMessage)
        {
            if ((uint)wParam == ABN_POSCHANGED)
            {
                Reposition();
                Moved?.Invoke();
            }

            return true;
        }

        if (msg is WM_ACTIVATE or WM_WINDOWPOSCHANGED)
        {
            var data = Data();
            PInvoke.SHAppBarMessage(msg == WM_ACTIVATE ? ABM_ACTIVATE : ABM_WINDOWPOSCHANGED, ref data);
        }

        return false;
    }

    APPBARDATA Data() => new()
    {
        cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
        hWnd = handle,
        uCallbackMessage = CallbackMessage,
        uEdge = edge switch { Edge.Left => 0u, Edge.Top => 1u, Edge.Right => 2u, _ => 3u },
    };

    static RECT ToRect(PixelRect r) => new() { left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom };

    static PixelRect FromRect(RECT r) => new(r.left, r.top, r.right, r.bottom);
}
