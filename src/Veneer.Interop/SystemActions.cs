using System.Diagnostics;
using Veneer.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Veneer.Interop;

/// <summary>Power and session actions for the logo menu. Callers must confirm the destructive ones with the user first.</summary>
public static class SystemActions
{
    public static void Lock() => PInvoke.LockWorkStation();

    public static void Sleep() => PInvoke.SetSuspendState(false, false, false);

    // shutdown.exe enables the shutdown privilege itself; ExitWindowsEx would need it adjusted on our token.
    public static void Restart() => Run("shutdown.exe", "/r /t 0");

    public static void ShutDown() => Run("shutdown.exe", "/s /t 0");

    /// <summary>
    /// Starts a dock entry: an AppUserModelID through <c>shell:AppsFolder</c>; an exe name, full path or URI through
    /// the shell, which also resolves App Paths (e.g. <c>msedge.exe</c>).
    /// </summary>
    public static void Launch(string entry, bool isAumid) =>
        Start(isAumid
            ? new ProcessStartInfo("explorer.exe", @"shell:AppsFolder\" + entry) { UseShellExecute = true }
            : new ProcessStartInfo(entry) { UseShellExecute = true });

    /// <summary>Opens a URI such as <c>ms-settings:about</c> with its registered handler.</summary>
    public static void Open(string uri) => Start(new ProcessStartInfo(uri) { UseShellExecute = true });

    static void Run(string exe, string args) => Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true });

    static void Start(ProcessStartInfo info)
    {
        try
        {
            Process.Start(info)?.Dispose();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Write($"could not start {info.FileName}: {e.Message}");
        }
    }
}

/// <summary>What <c>active-app</c> needs to know about a top-level window.</summary>
public sealed record WindowDetails(nint Handle, int ProcessId, string? ExePath, string? Description, string ClassName, string Title);

public static class WindowTools
{
    const uint WM_CLOSE = 0x0010;
    const uint WM_NCHITTEST = 0x0084;

    /// <summary>
    /// Asks DWM for the Windows 11 rounded corners on a window that draws its own frame: DWM does not round a window
    /// whose client area covers the whole frame unless asked.
    /// </summary>
    public static unsafe void RoundCorners(nint hwnd)
    {
        var corner = Windows.Win32.Graphics.Dwm.DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        PInvoke.DwmSetWindowAttribute((HWND)hwnd, Windows.Win32.Graphics.Dwm.DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &corner, sizeof(Windows.Win32.Graphics.Dwm.DWM_WINDOW_CORNER_PREFERENCE));
    }

    /// <summary>
    /// What a window says is at a screen point, in physical pixels (an <c>HT*</c> value: 2 caption, 9 maximize button),
    /// as Windows asks it before a hover or a click. Self-tests use it to check a custom title bar without a mouse.
    /// </summary>
    public static int HitTest(nint hwnd, int x, int y) =>
        (int)PInvoke.SendMessage((HWND)hwnd, WM_NCHITTEST, 0, (nint)((y << 16) | (x & 0xFFFF))).Value;

    public static unsafe WindowDetails Describe(nint hwnd)
    {
        var h = (HWND)hwnd;
        uint pid;
        PInvoke.GetWindowThreadProcessId(h, &pid);
        string? exe = ExePath(pid);
        string? description = exe is null ? null : Description(exe);

        return new WindowDetails(hwnd, (int)pid, exe, description, ClassName(h), Title(h));
    }

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?> Descriptions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The exe's file description, read from disk once per exe (it runs on every foreground change).</summary>
    public static string? Description(string exe) => Descriptions.GetOrAdd(exe, static path =>
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(path).FileDescription;
        }
        catch (Exception e) when (e is FileNotFoundException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    });

    /// <summary>Full exe path; works for elevated processes too (limited query rights).</summary>
    public static string? ExePath(uint pid)
    {
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process.IsInvalid)
            return null;
        Span<char> buffer = stackalloc char[1024];
        uint length = (uint)buffer.Length;
        return PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, buffer, ref length)
            ? new string(buffer[..(int)length])
            : null;
    }

    static unsafe string ClassName(HWND h)
    {
        Span<char> buffer = stackalloc char[256];
        fixed (char* p = buffer)
        {
            int n = PInvoke.GetClassName(h, p, buffer.Length);
            return new string(p, 0, n);
        }
    }

    /// <summary>A window's title. For another process's window Windows answers from its own copy: a hung app cannot block it.</summary>
    public static string TitleOf(nint hwnd) => Title((HWND)hwnd);

    static unsafe string Title(HWND h)
    {
        int length = PInvoke.GetWindowTextLength(h);
        if (length == 0)
            return "";
        var buffer = new char[length + 1];
        fixed (char* p = buffer)
        {
            int n = PInvoke.GetWindowText(h, p, buffer.Length);
            return new string(p, 0, n);
        }
    }

    /// <summary>Asks the window to close, exactly like its close button (the app may ask to save).</summary>
    public static void Close(nint hwnd) => PInvoke.PostMessage((HWND)hwnd, WM_CLOSE, 0, 0);

    public static void Minimize(nint hwnd) => PInvoke.ShowWindowAsync((HWND)hwnd, SHOW_WINDOW_CMD.SW_MINIMIZE);

    /// <summary>
    /// Brings a window to the front, restoring it if minimized. Allowed because the user just clicked one of our
    /// windows (Windows lets the process that received the last input set the foreground window).
    /// </summary>
    public static void Activate(nint hwnd)
    {
        var h = (HWND)hwnd;
        // Async: ShowWindow on another process's window waits for it, and a hung app would freeze the dock.
        if (PInvoke.IsIconic(h))
            PInvoke.ShowWindowAsync(h, SHOW_WINDOW_CMD.SW_RESTORE);
        PInvoke.SetForegroundWindow(h);
    }

    /// <summary>
    /// Re-maximizes maximized windows whose size is from an old work area (Windows does not always resize them when
    /// app bars go away or the taskbar comes back, e.g. after a crash). The window is moved onto the work area and
    /// stays maximized; async, so a hung app cannot block us. (Restore-then-maximize was tried first: Zen ignored it.)
    /// </summary>
    public static unsafe int RefitMaximizedWindows()
    {
        var targets = new List<(HWND Handle, Veneer.Core.Layout.PixelRect Bounds)>();
        PInvoke.EnumWindows((h, _) =>
        {
            if (PInvoke.IsWindowVisible(h) && PInvoke.IsZoomed(h) && PInvoke.GetWindowRect(h, out RECT r))
            {
                var info = new Windows.Win32.Graphics.Gdi.MONITORINFO { cbSize = (uint)sizeof(Windows.Win32.Graphics.Gdi.MONITORINFO) };
                var monitor = PInvoke.MonitorFromWindow(h, Windows.Win32.Graphics.Gdi.MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
                if (PInvoke.GetMonitorInfo(monitor, &info))
                {
                    var window = new Veneer.Core.Layout.PixelRect(r.left, r.top, r.right, r.bottom);
                    var work = new Veneer.Core.Layout.PixelRect(info.rcWork.left, info.rcWork.top, info.rcWork.right, info.rcWork.bottom);
                    if (Veneer.Core.Layout.SurfaceLayout.NeedsRefit(window, work))
                        targets.Add((h, Veneer.Core.Layout.SurfaceLayout.MaximizedBounds(window, work)));
                }
            }

            return true;
        }, 0);
        foreach (var (h, b) in targets)
        {
            PInvoke.SetWindowPos(h, default, b.Left, b.Top, b.Width, b.Height,
                SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_ASYNCWINDOWPOS);
        }

        return targets.Count;
    }

    /// <summary>
    /// Asks every visible top-level window of the process to close. Never kills the process: the app decides
    /// (and can ask to save), like closing it by hand.
    /// </summary>
    public static void Quit(int processId)
    {
        var windows = new List<HWND>();
        PInvoke.EnumWindows((h, _) =>
        {
            unsafe
            {
                uint pid;
                PInvoke.GetWindowThreadProcessId(h, &pid);
                if (pid == processId && PInvoke.IsWindowVisible(h) && PInvoke.GetWindow(h, GET_WINDOW_CMD.GW_OWNER).IsNull)
                    windows.Add(h);
            }

            return true;
        }, 0);
        foreach (var h in windows)
            PInvoke.PostMessage(h, WM_CLOSE, 0, 0);
    }
}
