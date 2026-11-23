// Phase 0 spike: two app bars (top + bottom) and a taskbar that is hidden and always comes back.
// Throwaway code. Usage:
//   AppBars [--hide-taskbar] [--no-watchdog] [--seconds N] [--crash-after N] [--failfast-after N]
//   AppBars --watchdog <pid>   (started by the main process; restores the taskbar if <pid> dies dirty)
//   AppBars --restore          (manual recovery: restore from the state file)
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--watchdog") return Watchdog(int.Parse(args[1]));
        if (args.Length == 1 && args[0] == "--restore") { Taskbar.Restore("manual"); return 0; }

        bool hide = args.Contains("--hide-taskbar");
        bool watchdog = !args.Contains("--no-watchdog");
        int seconds = Arg(args, "--seconds") ?? 30;
        int? crashAfter = Arg(args, "--crash-after");
        int? failFastAfter = Arg(args, "--failfast-after");

        // Next-start recovery: a state file left by a previous run means the taskbar was never restored.
        if (Taskbar.IsDirty) Taskbar.Restore("next start (dirty state)");

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) => { Log("DispatcherUnhandledException: " + e.Exception.Message); Shutdown(); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { Log("AppDomain.UnhandledException"); Shutdown(); };
        app.SessionEnding += (_, e) => { Log("SessionEnding " + e.ReasonSessionEnding); Shutdown(); };
        app.Exit += (_, _) => { Log("Exit"); Shutdown(); };

        app.Startup += (_, _) =>
        {
            if (hide)
            {
                if (watchdog) Process.Start(Environment.ProcessPath!, $"--watchdog {Environment.ProcessId}");
                Taskbar.Hide();
            }
            Bars.Add(new AppBar("top", ABE_TOP, 28, Brushes.SteelBlue));
            Bars.Add(new AppBar("bottom", ABE_BOTTOM, 64, Brushes.IndianRed));
            foreach (var b in Bars) b.Show();
            Log("work area after register: " + WorkArea());

            After(seconds, app.Shutdown);
            if (crashAfter is int c) After(c, () => throw new InvalidOperationException("simulated crash"));
            if (failFastAfter is int f) After(f, () => Environment.FailFast("simulated hard crash"));
        };
        app.Run();
        return 0;
    }

    static readonly List<AppBar> Bars = [];
    static int shutdownDone;

    // Single exit path used by every handler: unregister bars, then give the taskbar back.
    static void Shutdown()
    {
        if (Interlocked.Exchange(ref shutdownDone, 1) == 1) return;
        foreach (var b in Bars) b.Unregister();
        if (Taskbar.IsDirty) Taskbar.Restore("in-process");
        Log("work area after cleanup: " + WorkArea());
    }

    internal static void OnTaskbarCreated()
    {
        Log("TaskbarCreated: Explorer restarted");
        // A killed Explorer does not save the auto-hide bit we set, so the new one reserves space again: redo everything.
        if (Taskbar.IsDirty) Taskbar.Hide();
        foreach (var b in Bars) b.Register();
        Log("work area after re-register: " + WorkArea());
    }

    static int Watchdog(int pid)
    {
        Log($"watchdog started for pid {pid}");
        try { Process.GetProcessById(pid).WaitForExit(); } catch (ArgumentException) { /* already gone */ }
        if (Taskbar.IsDirty) Taskbar.Restore("watchdog");
        else Log("watchdog: clean exit, nothing to do");
        return 0;
    }

    static void After(int seconds, Action action)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        t.Tick += (_, _) => { t.Stop(); action(); };
        t.Start();
    }

    static int? Arg(string[] args, string name) { int i = Array.IndexOf(args, name); return i >= 0 ? int.Parse(args[i + 1]) : null; }

    internal static unsafe string WorkArea()
    {
        RECT r;
        PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETWORKAREA, 0, &r, 0);
        return $"{r.left},{r.top},{r.right},{r.bottom}";
    }

    static readonly string LogPath = Path.Combine(Path.GetTempPath(), "veneer-spike-appbars.log");
    internal static void Log(string s) => File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} pid={Environment.ProcessId} {s}{Environment.NewLine}");

    internal const uint ABE_TOP = 1, ABE_BOTTOM = 3;
}

static class Taskbar
{
    static readonly string StatePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Veneer-spike", "taskbar-state.json");
    record State(uint AppBarState);

    const uint ABM_GETSTATE = 4, ABM_SETSTATE = 10, ABS_AUTOHIDE = 1;

    public static bool IsDirty => File.Exists(StatePath);

    public static void Hide()
    {
        // Snapshot first, on disk, before touching anything. An existing file already holds the true original.
        if (!IsDirty)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(new State(GetState())));
        }
        SetState(GetState() | ABS_AUTOHIDE); // frees the work area
        HideWindows();                        // stops the auto-hidden taskbar from popping up on hover
        WatchShow();
        Program.Log($"taskbar hidden; trayVisible={TrayVisible}");
    }

    public static void HideWindows() { foreach (var h in Windows()) PInvoke.ShowWindow(h, SHOW_WINDOW_CMD.SW_HIDE); }

    // Explorer re-shows Shell_TrayWnd on its own (auto-hide animation, hover, Win key...).
    // Event-driven: listen for EVENT_OBJECT_SHOW from Explorer only, and hide again.
    static UnhookWinEventSafeHandle? showHook;
    static readonly WINEVENTPROC OnShow = (hook, ev, hwnd, idObject, idChild, thread, time) =>
    {
        if (idObject != 0 || !IsDirty) return; // OBJID_WINDOW
        if (!Windows().Contains(hwnd)) return;
        PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_HIDE);
        Program.Log("re-hid taskbar after EVENT_OBJECT_SHOW");
    };

    public static unsafe void WatchShow()
    {
        showHook?.Dispose();
        uint pid;
        PInvoke.GetWindowThreadProcessId(PInvoke.FindWindow("Shell_TrayWnd", null), &pid);
        const uint EVENT_OBJECT_SHOW = 0x8002;
        showHook = PInvoke.SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, default, OnShow, pid, 0, 0 /* WINEVENT_OUTOFCONTEXT */);
        Program.Log($"show hook on explorer pid {pid}: {!showHook.IsInvalid}");
    }

    public static void StopWatching() { showHook?.Dispose(); showHook = null; }

    public static void Restore(string who)
    {
        try
        {
            StopWatching();
            var s = JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath))!;
            SetState(s.AppBarState);
            foreach (var h in Windows()) PInvoke.ShowWindow(h, SHOW_WINDOW_CMD.SW_SHOW);
            File.Delete(StatePath);
            Program.Log($"taskbar restored by {who}; autoHide={(GetState() & ABS_AUTOHIDE) != 0} trayVisible={TrayVisible}");
        }
        catch (Exception e) { Program.Log($"restore by {who} FAILED: {e}"); }
    }

    static bool TrayVisible => PInvoke.IsWindowVisible(PInvoke.FindWindow("Shell_TrayWnd", null));

    static IEnumerable<HWND> Windows()
    {
        var tray = PInvoke.FindWindow("Shell_TrayWnd", null);
        if (!tray.IsNull) yield return tray;
        // Untested here (single monitor): one Shell_SecondaryTrayWnd per extra monitor.
        HWND prev = default;
        while (!(prev = PInvoke.FindWindowEx(default, prev, "Shell_SecondaryTrayWnd", null)).IsNull) yield return prev;
    }

    static uint GetState()
    {
        var d = new APPBARDATA { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>() };
        return (uint)PInvoke.SHAppBarMessage(ABM_GETSTATE, ref d);
    }

    static void SetState(uint state)
    {
        var d = new APPBARDATA { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(), hWnd = PInvoke.FindWindow("Shell_TrayWnd", null), lParam = (nint)state };
        PInvoke.SHAppBarMessage(ABM_SETSTATE, ref d);
    }
}

sealed class AppBar : Window
{
    const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3, ABM_ACTIVATE = 6, ABM_WINDOWPOSCHANGED = 9;
    const uint ABN_POSCHANGED = 1, ABN_FULLSCREENAPP = 2;
    const int WM_ACTIVATE = 0x06, WM_WINDOWPOSCHANGING = 0x46, WM_WINDOWPOSCHANGED = 0x47;
    static readonly uint CallbackMsg = PInvoke.RegisterWindowMessage("VeneerSpikeAppBar");
    static readonly uint TaskbarCreatedMsg = PInvoke.RegisterWindowMessage("TaskbarCreated");

    readonly string name;
    readonly uint edge;
    readonly int thickness; // physical pixels; spike assumes 96 DPI
    HWND hwnd;
    bool registered;
    RECT target; // where the shell told us to be; enforced in WM_WINDOWPOSCHANGING

    public AppBar(string name, uint edge, int thickness, Brush color)
    {
        this.name = name; this.edge = edge; this.thickness = thickness;
        Title = "spike-appbar-" + name;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = true; ShowActivated = false;
        Background = color;
        Content = new TextBlock { Text = $"app bar: {name}", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        SourceInitialized += (_, _) =>
        {
            hwnd = (HWND)new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd).AddHook(Hook);
            Register();
        };
    }

    APPBARDATA Data() => new() { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd, uCallbackMessage = CallbackMsg, uEdge = edge };

    public void Register()
    {
        var d = Data();
        registered = PInvoke.SHAppBarMessage(ABM_NEW, ref d) != 0;
        Program.Log($"{name}: ABM_NEW -> {registered}");
        Reposition();
    }

    void Reposition()
    {
        if (!registered) return;
        var d = Data();
        var screen = new RECT { left = 0, top = 0, right = (int)SystemParameters.PrimaryScreenWidth, bottom = (int)SystemParameters.PrimaryScreenHeight };
        d.rc = screen;
        if (edge == Program.ABE_TOP) d.rc.bottom = d.rc.top + thickness; else d.rc.top = d.rc.bottom - thickness;
        PInvoke.SHAppBarMessage(ABM_QUERYPOS, ref d);
        // The shell may move our proposed edge; re-apply our thickness from the edge it gave back.
        if (edge == Program.ABE_TOP) d.rc.bottom = d.rc.top + thickness; else d.rc.top = d.rc.bottom - thickness;
        PInvoke.SHAppBarMessage(ABM_SETPOS, ref d);
        target = d.rc;
        PInvoke.SetWindowPos(hwnd, (HWND)(nint)(-1) /* HWND_TOPMOST */, d.rc.left, d.rc.top, d.rc.right - d.rc.left, d.rc.bottom - d.rc.top, SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        Program.Log($"{name}: placed at {d.rc.left},{d.rc.top},{d.rc.right},{d.rc.bottom}");
    }

    public void Unregister()
    {
        if (!registered) return;
        var d = Data();
        PInvoke.SHAppBarMessage(ABM_REMOVE, ref d);
        registered = false;
        Program.Log($"{name}: ABM_REMOVE");
    }

    unsafe IntPtr Hook(IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (msg == WM_WINDOWPOSCHANGING && registered && target.right > 0)
        {
            var pos = (WINDOWPOS*)lp;
            bool moves = (pos->flags & SET_WINDOW_POS_FLAGS.SWP_NOMOVE) == 0 && (pos->x != target.left || pos->y != target.top);
            bool sizes = (pos->flags & SET_WINDOW_POS_FLAGS.SWP_NOSIZE) == 0 && (pos->cx != target.right - target.left || pos->cy != target.bottom - target.top);
            if (moves || sizes)
            {
                Program.Log($"{name}: blocked move to {pos->x},{pos->y} {pos->cx}x{pos->cy}");
                pos->x = target.left; pos->y = target.top;
                pos->cx = target.right - target.left; pos->cy = target.bottom - target.top;
            }
        }
        else if (msg == CallbackMsg)
        {
            if ((uint)wp == ABN_POSCHANGED) Reposition();
            else if ((uint)wp == ABN_FULLSCREENAPP) Program.Log($"{name}: ABN_FULLSCREENAPP {lp != IntPtr.Zero}");
        }
        else if (msg == TaskbarCreatedMsg && edge == Program.ABE_TOP) // handle once, from one bar
            Dispatcher.BeginInvoke(Program.OnTaskbarCreated);
        else if (msg == WM_ACTIVATE && registered) { var d = Data(); PInvoke.SHAppBarMessage(ABM_ACTIVATE, ref d); }
        else if (msg == WM_WINDOWPOSCHANGED && registered) { var d = Data(); PInvoke.SHAppBarMessage(ABM_WINDOWPOSCHANGED, ref d); }
        return IntPtr.Zero;
    }
}
