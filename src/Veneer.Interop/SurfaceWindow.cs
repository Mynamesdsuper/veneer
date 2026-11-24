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

    /// <summary>
    /// Above this radius DWM cannot round the window, and its backdrop and frame fill the whole rectangle whatever
    /// the window region or alpha: such surfaces must be per-pixel transparent (see <see cref="GlassPlate"/>).
    /// </summary>
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

    /// <summary>
    /// Blur behind a per-pixel transparent pop-up (a WPF menu or label): the DWM backdrop does not work on those, so
    /// this uses the undocumented <c>SetWindowCompositionAttribute</c> acrylic (the Phase 0 fallback), plus Windows
    /// 11 rounding. <paramref name="tintAbgr"/> is a faint base tint; the theme's color is painted by WPF on top.
    /// </summary>
    public static unsafe void ApplyPopupBlur(nint hwnd, uint tintAbgr, bool round, bool acrylic = true)
    {
        SetAccent(hwnd, acrylic ? 4 /* ACCENT_ENABLE_ACRYLICBLURBEHIND */ : 3 /* ACCENT_ENABLE_BLURBEHIND */, tintAbgr);
        var corner = round ? DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND : DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND;
        PInvoke.DwmSetWindowAttribute((HWND)hwnd, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &corner, sizeof(DWM_WINDOW_CORNER_PREFERENCE));
    }

    /// <summary>Sets the undocumented <c>SetWindowCompositionAttribute</c> accent state of a window.</summary>
    internal static unsafe void SetAccent(nint hwnd, int state, uint tintAbgr)
    {
        var accent = new AccentPolicy { AccentState = state, GradientColor = tintAbgr };
        var data = new CompositionData { Attribute = 19 /* WCA_ACCENT_POLICY */, Data = (nint)(&accent), SizeOfData = sizeof(AccentPolicy) };
        SetWindowCompositionAttribute(hwnd, ref data);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct CompositionData
    {
        public int Attribute;
        public nint Data;
        public int SizeOfData;
    }

    // Undocumented (user32), declared by hand.
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetWindowCompositionAttribute(nint hwnd, ref CompositionData data);

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

    /// <summary>
    /// Turns off DWM's window shadow: a forced-active bar would otherwise cast a dark band about 60 px deep onto the
    /// desktop. DWM draws no shadow around a window that has a region; this one is larger than any window, so it clips
    /// nothing and never needs updating when the window is resized.
    /// </summary>
    public static void RemoveShadow(nint hwnd) =>
        PInvoke.SetWindowRgn((HWND)hwnd, PInvoke.CreateRectRgn(0, 0, short.MaxValue, short.MaxValue), true); // the system owns the region

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
