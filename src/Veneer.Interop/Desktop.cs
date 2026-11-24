using Veneer.Core.Layout;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Veneer.Interop;

public readonly record struct MonitorInfo(PixelRect Bounds, uint Dpi);

public static class Monitors
{
    /// <summary>The primary monitor: full bounds in physical pixels and its effective DPI (96 = 100%).</summary>
    public static unsafe MonitorInfo Primary()
    {
        var monitor = PInvoke.MonitorFromPoint(default, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        PInvoke.GetMonitorInfo(monitor, &info);
        if (PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out uint dpi, out _).Failed)
            dpi = 96;
        var r = info.rcMonitor;
        return new MonitorInfo(new PixelRect(r.left, r.top, r.right, r.bottom), dpi);
    }
}

/// <summary>Reports the foreground window as it changes (WinEvent hook, no polling). Create it on a thread with a message loop.</summary>
public sealed class ForegroundWatcher : IDisposable
{
    const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    readonly WINEVENTPROC callback; // kept in a field: the hook holds only a native pointer to it
    readonly UnhookWinEventSafeHandle hook;

    public ForegroundWatcher()
    {
        callback = (_, _, hwnd, _, _, _, _) => Changed?.Invoke(hwnd);
        hook = PInvoke.SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, default, callback, 0, 0, 0 /* WINEVENT_OUTOFCONTEXT */);
    }

    public event Action<nint>? Changed;

    public static nint Current => PInvoke.GetForegroundWindow();

    public void Dispose() => hook.Dispose();
}

/// <summary>System-wide hotkeys bound to one window. Feed its messages to <see cref="WndProc"/>.</summary>
public sealed class HotKeys(nint hwnd) : IDisposable
{
    const int WM_HOTKEY = 0x0312;
    readonly List<int> ids = [];

    public event Action<int>? Pressed;

    /// <summary>Registers Ctrl+Alt+Shift+<paramref name="key"/> (a virtual-key code, e.g. 'Q'). False when another app owns it.</summary>
    public bool Register(int id, char key)
    {
        var mods = HOT_KEY_MODIFIERS.MOD_CONTROL | HOT_KEY_MODIFIERS.MOD_ALT | HOT_KEY_MODIFIERS.MOD_SHIFT | HOT_KEY_MODIFIERS.MOD_NOREPEAT;
        bool ok = PInvoke.RegisterHotKey((HWND)hwnd, id, mods, key);
        if (ok)
            ids.Add(id);
        return ok;
    }

    public bool WndProc(int msg, nint wParam)
    {
        if (msg != WM_HOTKEY || !ids.Contains((int)wParam))
            return false;
        Pressed?.Invoke((int)wParam);
        return true;
    }

    public void Dispose()
    {
        foreach (int id in ids)
            PInvoke.UnregisterHotKey((HWND)hwnd, id);
        ids.Clear();
    }
}

public static class ScreenCapture
{
    /// <summary>Copies a screen area as composed by DWM (blur included). Returns top-down BGRA pixels.</summary>
    public static unsafe byte[] Capture(PixelRect area)
    {
        var screen = PInvoke.GetDC(default);
        var memory = PInvoke.CreateCompatibleDC(screen);
        var bitmap = PInvoke.CreateCompatibleBitmap(screen, area.Width, area.Height);
        var old = PInvoke.SelectObject(memory, bitmap);
        try
        {
            PInvoke.BitBlt(memory, 0, 0, area.Width, area.Height, screen, area.Left, area.Top, ROP_CODE.SRCCOPY | ROP_CODE.CAPTUREBLT);
            var info = new BITMAPINFO();
            info.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
            info.bmiHeader.biWidth = area.Width;
            info.bmiHeader.biHeight = -area.Height; // top-down
            info.bmiHeader.biPlanes = 1;
            info.bmiHeader.biBitCount = 32;
            var pixels = new byte[area.Width * area.Height * 4];
            fixed (byte* p = pixels)
                PInvoke.GetDIBits(memory, bitmap, 0, (uint)area.Height, p, &info, DIB_USAGE.DIB_RGB_COLORS);
            return pixels;
        }
        finally
        {
            PInvoke.SelectObject(memory, old);
            PInvoke.DeleteObject(bitmap);
            PInvoke.DeleteDC(memory);
            PInvoke.ReleaseDC(default, screen);
        }
    }
}

public readonly record struct BatteryStatus(bool Present, int Percent, bool Charging);

public static class Power
{
    public static BatteryStatus Battery()
    {
        if (!PInvoke.GetSystemPowerStatus(out var s))
            return default;
        const byte NoSystemBattery = 128, UnknownStatus = 255, UnknownPercent = 255;
        bool present = s.BatteryFlag != UnknownStatus && (s.BatteryFlag & NoSystemBattery) == 0 && s.BatteryLifePercent != UnknownPercent;
        return new BatteryStatus(present, s.BatteryLifePercent, s.ACLineStatus == 1 && s.BatteryLifePercent < 100);
    }
}
