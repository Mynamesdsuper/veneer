using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Veneer.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Veneer.Interop;

public interface ITaskbar
{
    /// <summary>True while the taskbar is hidden by Veneer (the state file exists).</summary>
    bool IsHidden { get; }

    /// <summary>Saves the original state to disk, then hides the taskbar and keeps it hidden.</summary>
    void Hide();

    /// <summary>Gives the taskbar back as it was. Safe to call many times and from any exit path.</summary>
    void Restore(string reason);

    /// <summary>Call on the shell's <c>TaskbarCreated</c> message: a new Explorer shows its taskbar again.</summary>
    void OnShellRestarted();
}

/// <summary>
/// Hides the Windows taskbar and always gives it back. Recipe and test results: docs/decisions.md, Phase 0.
/// The state file is the "dirty" flag: it exists exactly while the taskbar is hidden by Veneer.
/// </summary>
public sealed class Taskbar : ITaskbar
{
    const uint ABM_GETSTATE = 4, ABM_SETSTATE = 10, ABS_AUTOHIDE = 1;
    const uint EVENT_OBJECT_SHOW = 0x8002;

    readonly string statePath;
    readonly WINEVENTPROC onShow; // kept in a field: the hook holds only a native pointer to it
    UnhookWinEventSafeHandle? showHook;

    public Taskbar(string? statePath = null)
    {
        this.statePath = statePath ?? Path.Combine(AppPaths.State, "taskbar.json");
        onShow = OnShow;
    }

    /// <summary>Message id of the shell's "TaskbarCreated" broadcast.</summary>
    public static uint TaskbarCreatedMessage { get; } = PInvoke.RegisterWindowMessage("TaskbarCreated");

    public bool IsHidden => File.Exists(statePath);

    sealed record State(uint AppBarState);

    public void Hide()
    {
        if (!IsHidden)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            File.WriteAllText(statePath, JsonSerializer.Serialize(new State(GetState())));
        }

        SetState(GetState() | ABS_AUTOHIDE); // frees the work area
        foreach (var w in Windows())
            PInvoke.ShowWindow(w, SHOW_WINDOW_CMD.SW_HIDE); // stops the auto-hidden taskbar from popping up
        WatchShow();
        Log.Write("taskbar hidden");
    }

    public void Restore(string reason)
    {
        if (!IsHidden)
            return;
        try
        {
            showHook?.Dispose();
            showHook = null;
            var state = JsonSerializer.Deserialize<State>(File.ReadAllText(statePath))!;
            SetState(state.AppBarState);
            foreach (var w in Windows())
                PInvoke.ShowWindow(w, SHOW_WINDOW_CMD.SW_SHOW);
            File.Delete(statePath);
            Log.Write($"taskbar restored ({reason})");
        }
        catch (Exception e)
        {
            Log.Write($"taskbar restore ({reason}) FAILED: {e}");
        }
    }

    // A force-killed Explorer does not save the auto-hide bit, so the new one reserves space again: redo everything.
    public void OnShellRestarted()
    {
        if (IsHidden)
            Hide();
    }

    // Explorer re-shows Shell_TrayWnd by itself (auto-hide animation, hover, Win key...): hide it again.
    unsafe void WatchShow()
    {
        showHook?.Dispose();
        uint pid;
        PInvoke.GetWindowThreadProcessId(PInvoke.FindWindow("Shell_TrayWnd", null), &pid);
        showHook = PInvoke.SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, default, onShow, pid, 0, 0 /* WINEVENT_OUTOFCONTEXT */);
    }

    void OnShow(HWINEVENTHOOK hook, uint ev, HWND hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject == 0 /* OBJID_WINDOW */ && IsHidden && Windows().Contains(hwnd))
            PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_HIDE);
    }

    static IEnumerable<HWND> Windows()
    {
        var tray = PInvoke.FindWindow("Shell_TrayWnd", null);
        if (!tray.IsNull)
            yield return tray;
        HWND prev = default;
        while (!(prev = PInvoke.FindWindowEx(default, prev, "Shell_SecondaryTrayWnd", null)).IsNull)
            yield return prev;
    }

    static uint GetState()
    {
        var d = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
        return (uint)PInvoke.SHAppBarMessage(ABM_GETSTATE, ref d);
    }

    static void SetState(uint state)
    {
        var d = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = PInvoke.FindWindow("Shell_TrayWnd", null), lParam = (nint)state };
        PInvoke.SHAppBarMessage(ABM_SETSTATE, ref d);
    }
}

/// <summary>
/// A second copy of Veneer that waits for the main process and restores the taskbar if it dies without doing so
/// (FailFast, kill, crash in native code). Measured cost: about 30 MB, no CPU.
/// </summary>
public static class Watchdog
{
    public const string Argument = "--watchdog";

    public static void Start()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"{Argument} {Environment.ProcessId}") { UseShellExecute = false });
        }
        catch (Exception e)
        {
            Log.Write($"watchdog could not start: {e.Message}");
        }
    }

    /// <summary>Body of the watchdog process.</summary>
    public static int Run(int pid, ITaskbar taskbar)
    {
        try
        {
            Process.GetProcessById(pid).WaitForExit();
        }
        catch (ArgumentException)
        {
            // already gone
        }

        if (taskbar.IsHidden)
            taskbar.Restore("watchdog");
        return 0;
    }
}
