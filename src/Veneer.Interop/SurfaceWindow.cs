using Veneer.Core.Layout;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Veneer.Interop;

/// <summary>
/// Window setup for bars and docks: topmost, hidden from Alt+Tab, never activated, with real blur and rounded
/// corners. Recipe and its reasons: docs/decisions.md, "Blurred overlay windows".
/// The window must have WS_CAPTION (WPF: SingleBorderWindow with a WindowChrome, GlassFrameThickness -1).
/// </summary>
public static class SurfaceWindow
{
    const int WM_NCACTIVATE = 0x0086;
    const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

    /// <summary>Above this radius DWM cannot round the window; a window region is used instead (aliased edge, no shadow).</summary>
    public const int MaxNativeCornerRadius = 8;

    public static unsafe void Apply(nint hwnd, bool blur, int cornerRadiusPx)
    {
        var h = (HWND)hwnd;
        const nint dropStyles = (nint)(WINDOW_STYLE.WS_SYSMENU | WINDOW_STYLE.WS_MINIMIZEBOX | WINDOW_STYLE.WS_MAXIMIZEBOX);
        PInvoke.SetWindowLongPtr(h, WINDOW_LONG_PTR_INDEX.GWL_STYLE, PInvoke.GetWindowLongPtr(h, WINDOW_LONG_PTR_INDEX.GWL_STYLE) & ~dropStyles);
        PInvoke.SetWindowLongPtr(h, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE,
            PInvoke.GetWindowLongPtr(h, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE) | (nint)(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE));

        var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
        PInvoke.DwmExtendFrameIntoClientArea(h, &margins);

        var backdrop = blur ? DWM_SYSTEMBACKDROP_TYPE.DWMSBT_TRANSIENTWINDOW : DWM_SYSTEMBACKDROP_TYPE.DWMSBT_NONE;
        PInvoke.DwmSetWindowAttribute(h, DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE, &backdrop, sizeof(DWM_SYSTEMBACKDROP_TYPE));

        var corner = cornerRadiusPx switch
        {
            0 or > MaxNativeCornerRadius => DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND,
            <= 4 => DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUNDSMALL,
            _ => DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND,
        };
        PInvoke.DwmSetWindowAttribute(h, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &corner, sizeof(DWM_WINDOW_CORNER_PREFERENCE));

        uint none = DWMWA_COLOR_NONE;
        PInvoke.DwmSetWindowAttribute(h, DWMWINDOWATTRIBUTE.DWMWA_BORDER_COLOR, &none, sizeof(uint));
        PInvoke.DwmSetWindowAttribute(h, DWMWINDOWATTRIBUTE.DWMWA_CAPTION_COLOR, &none, sizeof(uint));

        // Keep DWM drawing the window as active: an inactive window gets a solid backdrop instead of blur.
        PInvoke.SendMessage(h, WM_NCACTIVATE, 1, 0);
    }

    /// <summary>For a plain overlay window (no blur): hidden from Alt+Tab, never activated.</summary>
    public static void MakeOverlay(nint hwnd)
    {
        var h = (HWND)hwnd;
        PInvoke.SetWindowLongPtr(h, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE,
            PInvoke.GetWindowLongPtr(h, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE) | (nint)(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE));
    }

    /// <summary>Moves a window and puts it at the top of the topmost band, without activating it.</summary>
    public static void PlaceTopmost(nint hwnd, PixelRect bounds) =>
        PInvoke.SetWindowPos((HWND)hwnd, (HWND)(nint)(-1) /* HWND_TOPMOST */, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    /// <summary>Clips the window to a rounded rectangle when the radius is larger than DWM can draw. Call after every resize.</summary>
    public static void UpdateRegion(nint hwnd, PixelRect bounds, int cornerRadiusPx)
    {
        if (cornerRadiusPx <= MaxNativeCornerRadius)
            return;
        var region = PInvoke.CreateRoundRectRgn(0, 0, bounds.Width + 1, bounds.Height + 1, cornerRadiusPx * 2, cornerRadiusPx * 2);
        PInvoke.SetWindowRgn((HWND)hwnd, region, true); // the system owns the region from now on
    }

    /// <summary>Call for every window message. Returns true (with <paramref name="result"/>) when handled.</summary>
    public static bool WndProc(nint hwnd, int msg, nint wParam, nint lParam, out nint result)
    {
        result = 0;
        if (msg != WM_NCACTIVATE || wParam != 0)
            return false;
        result = PInvoke.DefWindowProc((HWND)hwnd, WM_NCACTIVATE, 1, lParam);
        return true;
    }
}
