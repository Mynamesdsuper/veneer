using System.Runtime.InteropServices;
using Microsoft.Win32;
using Veneer.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Threading;
using Windows.Win32.System.Variant;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.PropertiesSystem;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Veneer.Interop;

/// <summary>A top-level window that would get a taskbar button.</summary>
public sealed record AppWindow(nint Handle, int ProcessId, string? ExePath, string? Aumid, string Title);

/// <summary>
/// Lists app windows the way the taskbar does and reports changes through WinEvent hooks (no polling):
/// windows created, destroyed, shown, hidden, cloaked or uncloaked, and foreground changes (for "most recently used").
/// <see cref="Changed"/> can fire in bursts; consumers should debounce. Create it on a thread with a message loop.
/// </summary>
public sealed class AppWindowTracker : IDisposable
{
    const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    const uint EVENT_OBJECT_CREATE = 0x8000, EVENT_OBJECT_HIDE = 0x8003;
    const uint EVENT_OBJECT_CLOAKED = 0x8017, EVENT_OBJECT_UNCLOAKED = 0x8018;
    const int OBJID_WINDOW = 0;

    readonly WINEVENTPROC callback; // kept in a field: the hooks hold only a native pointer to it
    readonly List<UnhookWinEventSafeHandle> hooks = [];
    readonly Dictionary<nint, long> lastActive = [];

    public AppWindowTracker()
    {
        callback = OnEvent;
        hooks.Add(PInvoke.SetWinEventHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_HIDE, default, callback, 0, 0, 0));
        hooks.Add(PInvoke.SetWinEventHook(EVENT_OBJECT_CLOAKED, EVENT_OBJECT_UNCLOAKED, default, callback, 0, 0, 0));
        hooks.Add(PInvoke.SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, default, callback, 0, 0, 0));
    }

    public event Action? Changed;

    void OnEvent(HWINEVENTHOOK hook, uint ev, HWND hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != OBJID_WINDOW || idChild != 0 || hwnd.IsNull)
            return;
        if (ev == EVENT_SYSTEM_FOREGROUND)
            lastActive[hwnd] = Environment.TickCount64;
        else if (!PInvoke.GetAncestor(hwnd, GET_ANCESTOR_FLAGS.GA_PARENT).Equals(PInvoke.GetDesktopWindow()))
            return; // only top-level windows matter
        Changed?.Invoke();
    }

    /// <summary>When the window was last in the foreground (0 if never seen).</summary>
    public long LastActive(nint hwnd) => lastActive.GetValueOrDefault(hwnd);

    /// <summary>The app windows open now, in z-order (front first). Veneer's own windows are left out.</summary>
    public static List<AppWindow> Enumerate()
    {
        var handles = new List<HWND>();
        PInvoke.EnumWindows((h, _) =>
        {
            if (IsAppWindow(h))
                handles.Add(h);
            return true;
        }, 0);

        var result = new List<AppWindow>();
        foreach (var h in handles)
        {
            uint pid;
            unsafe
            {
                PInvoke.GetWindowThreadProcessId(h, &pid);
            }

            if (pid == Environment.ProcessId)
                continue;
            var details = WindowTools.Describe(h);
            result.Add(new AppWindow(h, (int)pid, details.ExePath, WindowAumid(h) ?? ProcessAumid(pid), details.Title));
        }

        return result;
    }

    // The taskbar's rule: visible, not cloaked, and either WS_EX_APPWINDOW or an unowned non-tool window.
    static unsafe bool IsAppWindow(HWND h)
    {
        if (!PInvoke.IsWindowVisible(h))
            return false;
        var ex = (WINDOW_EX_STYLE)(uint)PInvoke.GetWindowLongPtr(h, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        bool appWindow = (ex & WINDOW_EX_STYLE.WS_EX_APPWINDOW) != 0;
        if (!appWindow)
        {
            if (!PInvoke.GetWindow(h, GET_WINDOW_CMD.GW_OWNER).IsNull)
                return false;
            if ((ex & (WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE)) != 0)
                return false;
        }

        int cloaked = 0;
        PInvoke.DwmGetWindowAttribute(h, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &cloaked, sizeof(int));
        return cloaked == 0;
    }

    static readonly PROPERTYKEY AppUserModelId = new() { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };

    /// <summary>The window's own AppUserModelID property (set by Store apps and apps with an explicit id).</summary>
    static unsafe string? WindowAumid(HWND h)
    {
        try
        {
            ShellItems.SHGetPropertyStoreForWindow(h, typeof(IPropertyStore).GUID, out object obj);
            var store = (IPropertyStore)obj;
            try
            {
                var key = AppUserModelId;
                store.GetValue(&key, out PROPVARIANT value);
                try
                {
                    return value.Anonymous.Anonymous.vt == VARENUM.VT_LPWSTR ? value.Anonymous.Anonymous.Anonymous.pwszVal.ToString() : null;
                }
                finally
                {
                    PInvoke.PropVariantClear(ref value);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(store);
            }
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>The AppUserModelID of a packaged process (Terminal, Notepad...); null for classic desktop apps.</summary>
    static string? ProcessAumid(uint pid)
    {
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process.IsInvalid)
            return null;
        Span<char> buffer = stackalloc char[256];
        uint length = (uint)buffer.Length;
        return PInvoke.GetApplicationUserModelId(process, ref length, buffer) == WIN32_ERROR.ERROR_SUCCESS
            ? new string(buffer[..(int)Math.Max(0, (int)length - 1)])
            : null;
    }

    public void Dispose()
    {
        foreach (var h in hooks)
            h.Dispose();
        hooks.Clear();
    }
}

/// <summary>Icons and names from the Windows shell, for exe files, shortcuts and Store apps.</summary>
public static class ShellItems
{
    /// <summary>
    /// The shell parsing name for a dock entry: <c>shell:AppsFolder\&lt;AUMID&gt;</c> for an AppUserModelID,
    /// the resolved full path for an exe name, the path itself otherwise. Null when it cannot be found.
    /// </summary>
    public static string? ParsingName(string entry, bool isAumid)
    {
        if (isAumid)
            return @"shell:AppsFolder\" + entry;
        if (Path.IsPathRooted(entry))
            return File.Exists(entry) ? entry : null;
        return FindExe(entry);
    }

    /// <summary>Finds an exe by name the way "Run" does: App Paths in the registry, then the Windows folders and PATH.</summary>
    public static string? FindExe(string name)
    {
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return null;
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{name}");
            if (key?.GetValue(null) is string path && File.Exists(path = Environment.ExpandEnvironmentVariables(path.Trim('"'))))
                return path;
        }

        var folders = new[] { Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.SystemDirectory }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries));
        foreach (string folder in folders)
        {
            try
            {
                string candidate = Path.Combine(Environment.ExpandEnvironmentVariables(folder), name);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (ArgumentException)
            {
                // a malformed PATH entry
            }
        }

        return null;
    }

    /// <summary>The shell's display name (e.g. "Microsoft Edge", "Settings").</summary>
    public static unsafe string? DisplayName(string parsingName)
    {
        try
        {
            SHCreateItemFromParsingName(parsingName, 0, typeof(IShellItem).GUID, out object obj);
            var item = (IShellItem)obj;
            try
            {
                item.GetDisplayName(SIGDN.SIGDN_NORMALDISPLAY, out PWSTR name);
                try
                {
                    return name.ToString();
                }
                finally
                {
                    Marshal.FreeCoTaskMem((nint)name.Value);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        catch (Exception e) when (e is COMException or ArgumentException or FileNotFoundException)
        {
            return null;
        }
    }

    /// <summary>The item's icon at <paramref name="size"/> pixels as top-down BGRA (premultiplied), or null.</summary>
    public static unsafe (byte[] Pixels, int Width, int Height)? Icon(string parsingName, int size)
    {
        try
        {
            SHCreateItemFromParsingName(parsingName, 0, typeof(IShellItemImageFactory).GUID, out object obj);
            var factory = (IShellItemImageFactory)obj;
            try
            {
                HBITMAP bitmap;
                factory.GetImage(new SIZE(size, size), SIIGBF.SIIGBF_ICONONLY | SIIGBF.SIIGBF_BIGGERSIZEOK, &bitmap);
                try
                {
                    return Pixels(bitmap);
                }
                finally
                {
                    PInvoke.DeleteObject(bitmap);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        catch (Exception e) when (e is COMException or ArgumentException or FileNotFoundException)
        {
            Log.Write($"icon for \"{parsingName}\": {e.Message}");
            return null;
        }
    }

    // Declared by hand: CsWin32 exposes these only with raw pointers.
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, nint bindContext, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out object item);

    [DllImport("shell32.dll", PreserveSig = false)]
    internal static extern void SHGetPropertyStoreForWindow(nint hwnd, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out object store);

    static unsafe (byte[], int, int)? Pixels(HBITMAP bitmap)
    {
        BITMAP info;
        if (PInvoke.GetObject(bitmap, sizeof(BITMAP), &info) == 0)
            return null;
        int w = info.bmWidth, h = Math.Abs(info.bmHeight);
        var header = new BITMAPINFO();
        header.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
        header.bmiHeader.biWidth = w;
        header.bmiHeader.biHeight = -h; // top-down
        header.bmiHeader.biPlanes = 1;
        header.bmiHeader.biBitCount = 32;
        var pixels = new byte[w * h * 4];
        var dc = PInvoke.CreateCompatibleDC(default);
        try
        {
            fixed (byte* p = pixels)
            {
                if (PInvoke.GetDIBits(dc, bitmap, 0, (uint)h, p, &header, DIB_USAGE.DIB_RGB_COLORS) == 0)
                    return null;
            }
        }
        finally
        {
            PInvoke.DeleteDC(dc);
        }

        return (pixels, w, h);
    }
}
