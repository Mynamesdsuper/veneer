using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
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

    /// <summary>
    /// Gives the taskbar back as it was. Safe to call many times and from any exit path. Returns false (and keeps
    /// the state file, so a later attempt can finish the job) when the taskbar could not be restored.
    /// </summary>
    bool Restore(string reason);

    /// <summary>Call on the shell's <c>TaskbarCreated</c> message: a new Explorer shows its taskbar again.</summary>
    void OnShellRestarted();
}

/// <summary>
/// Hides the Windows taskbar and always gives it back. Recipe and test results: docs/decisions.md, Phase 0 and the
/// code review of 2026-11-24. Safety nets, in order: in-process restore on every exit path, the watchdog process,
/// the dirty state file at next start, and a RunOnce entry that restores at the next sign-in (power loss, BSOD).
/// </summary>
public sealed class Taskbar : ITaskbar
{
    const uint ABM_GETSTATE = 4, ABM_SETSTATE = 10;
    const uint EVENT_OBJECT_SHOW = 0x8002;
    const string RunOnceKey = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    const string RunOnceValue = "VeneerRestoreTaskbar";

    /// <summary>The argument the RunOnce entry starts Veneer with (see <see cref="SetRunOnce"/>).</summary>
    public const string SignInRestoreArgument = "--restore-taskbar-at-signin";

    readonly string statePath;
    readonly WINEVENTPROC onShow; // kept in a field: the hook holds only a native pointer to it
    UnhookWinEventSafeHandle? showHook;
    volatile bool keepHidden; // in memory, so the show hook never touches the disk

    public Taskbar(string? statePath = null)
    {
        this.statePath = statePath ?? Path.Combine(AppPaths.State, "taskbar.json");
        onShow = OnShow;
    }

    /// <summary>Message id of the shell's "TaskbarCreated" broadcast.</summary>
    public static uint TaskbarCreatedMessage { get; } = PInvoke.RegisterWindowMessage("TaskbarCreated");

    public bool IsHidden => File.Exists(statePath);

    public void Hide()
    {
        // Snapshot first, on disk and atomically; an existing file already holds the true original.
        if (!IsHidden)
            AtomicFile.Write(statePath, TaskbarState.Serialize(GetState()));
        SetRunOnce(true);

        keepHidden = true;
        SetState(GetState() | TaskbarState.AutoHide); // frees the work area
        foreach (var w in Windows())
            PInvoke.ShowWindow(w, SHOW_WINDOW_CMD.SW_HIDE); // stops the auto-hidden taskbar from popping up
        WatchShow();
        Log.Write("taskbar hidden");
    }

    public bool Restore(string reason)
    {
        keepHidden = false;
        showHook?.Dispose();
        showHook = null;
        if (!IsHidden)
        {
            // Nothing to undo in the settings, but never leave the windows hidden: something else (an older run, a
            // manual restore) may have reset the state while our show hook kept hiding the taskbar.
            foreach (var w in Windows())
                PInvoke.ShowWindow(w, SHOW_WINDOW_CMD.SW_SHOW);
            return true;
        }
        try
        {
            // Without Explorer the request would go nowhere (logoff, Explorer restarting): keep the file for later.
            if (PInvoke.FindWindow("Shell_TrayWnd", null).IsNull)
            {
                Log.Write($"taskbar restore ({reason}) postponed: Explorer's taskbar is not there");
                return false;
            }

            string? text = null;
            try
            {
                text = File.ReadAllText(statePath);
            }
            catch (IOException e)
            {
                Log.Write($"taskbar state unreadable: {e.Message}");
            }

            uint? saved = TaskbarState.Parse(text);
            if (saved is null)
                Log.Write("taskbar state damaged: turning auto-hide off (Windows default)");
            uint target = TaskbarState.RestoreTarget(saved, GetState());
            SetState(target);
            foreach (var w in Windows())
                PInvoke.ShowWindow(w, SHOW_WINDOW_CMD.SW_SHOW);

            if (!TaskbarState.IsRestored(target, GetState()))
            {
                Log.Write($"taskbar restore ({reason}) not confirmed by the shell; state file kept");
                return false;
            }

            File.Delete(statePath);
            SetRunOnce(false);
            Log.Write($"taskbar restored ({reason})");
            return true;
        }
        catch (Exception e)
        {
            Log.Write($"taskbar restore ({reason}) FAILED: {e}");
            return false;
        }
    }

    /// <summary>
    /// For <c>--restore-taskbar</c>, which RunOnce starts at sign-in, possibly before Explorer has created the
    /// taskbar: tries for up to <paramref name="wait"/> (a one-off startup wait, not polling while running).
    /// </summary>
    public bool RestoreWhenShellReady(string reason, TimeSpan wait)
    {
        var deadline = DateTime.UtcNow + wait;
        while (true)
        {
            if (Restore(reason) || DateTime.UtcNow >= deadline)
                return !IsHidden;
            Thread.Sleep(500);
        }
    }

    // A force-killed Explorer does not save the auto-hide bit, so the new one reserves space again: redo everything.
    public void OnShellRestarted()
    {
        if (IsHidden)
            Hide();
    }

    // HKCU RunOnce runs "Veneer.App --restore-taskbar-at-signin" at the next sign-in if Veneer never got the chance
    // to restore (power loss, BSOD, both processes killed). Removed again after every successful restore.
    // Explorer also runs RunOnce entries when it restarts: the sign-in argument does nothing while a Veneer is
    // running (found in the 2026-11-24 review tests: the entry restored the taskbar under a live Veneer).
    public static void SetRunOnce(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunOnceKey);
            if (on)
                key.SetValue(RunOnceValue, $"\"{Environment.ProcessPath}\" {SignInRestoreArgument}");
            else
                key.DeleteValue(RunOnceValue, throwOnMissingValue: false);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Log.Write($"RunOnce safety net not {(on ? "set" : "removed")}: {e.Message}");
        }
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
        if (idObject == 0 /* OBJID_WINDOW */ && keepHidden && Windows().Contains(hwnd))
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
/// It is started through a short-lived launcher, so it is not a child of the main process: "End process tree" in
/// Task Manager and <c>taskkill /T</c> do not take it down with Veneer.
/// </summary>
public static class Watchdog
{
    public const string Argument = "--watchdog";
    public const string LaunchArgument = "--launch-watchdog";

    /// <summary>Called by the main process.</summary>
    public static void Start() => Spawn(LaunchArgument, Environment.ProcessId);

    /// <summary>Body of the launcher: start the real watchdog for <paramref name="pid"/>, then exit at once.</summary>
    public static int Launch(int pid)
    {
        Spawn(Argument, pid);
        return 0;
    }

    static void Spawn(string argument, int pid)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"{argument} {pid}") { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
        }
        catch (Exception e)
        {
            Log.Write($"watchdog could not start ({argument}): {e.Message}");
        }
    }

    const int EmergencyHotKey = 1;

    /// <summary>
    /// Waits for the main process to exit. Meanwhile Ctrl+Alt+Shift+F12 ends it: the emergency exit for a hung
    /// Veneer, handled here because a hung main process cannot handle its own hotkeys (idea from FluidWin).
    /// No polling: the thread sleeps until the process exits or a message arrives.
    /// </summary>
    static unsafe void WaitWithEmergencyKey(Process process)
    {
        var mods = Windows.Win32.UI.Input.KeyboardAndMouse.HOT_KEY_MODIFIERS.MOD_CONTROL
            | Windows.Win32.UI.Input.KeyboardAndMouse.HOT_KEY_MODIFIERS.MOD_ALT
            | Windows.Win32.UI.Input.KeyboardAndMouse.HOT_KEY_MODIFIERS.MOD_SHIFT
            | Windows.Win32.UI.Input.KeyboardAndMouse.HOT_KEY_MODIFIERS.MOD_NOREPEAT;
        if (!PInvoke.RegisterHotKey(default, EmergencyHotKey, mods, 0x7B /* VK_F12 */))
            Log.Write("watchdog: emergency hotkey Ctrl+Alt+Shift+F12 is taken by another app");

        var handle = (HANDLE)process.SafeHandle.DangerousGetHandle();
        const uint WAIT_OBJECT_0 = 0, WAIT_FAILED = 0xFFFFFFFF, WM_HOTKEY = 0x0312;
        while (true)
        {
            uint result = (uint)PInvoke.MsgWaitForMultipleObjects(1, &handle, false, uint.MaxValue /* INFINITE */, QUEUE_STATUS_FLAGS.QS_HOTKEY);
            if (result is WAIT_OBJECT_0 or WAIT_FAILED)
                break; // the main process exited (or the wait cannot work: fall back to the plain wait below)
            while (PInvoke.PeekMessage(out MSG msg, default, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
            {
                if (msg.message != WM_HOTKEY || (int)msg.wParam.Value != EmergencyHotKey)
                    continue;
                Log.Write("watchdog: emergency hotkey pressed, ending Veneer");
                try
                {
                    process.Kill();
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // already exiting
                }
            }
        }

        process.WaitForExit();
    }

    /// <summary>
    /// Body of the watchdog process. <paramref name="noOtherInstance"/> tells whether a newer Veneer is running (it
    /// then owns the taskbar state, and restoring would undo its work).
    /// </summary>
    public static int Run(int pid, ITaskbar taskbar, Func<bool> noOtherInstance)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            WaitWithEmergencyKey(process);
        }
        catch (ArgumentException)
        {
            // already gone
        }

        // Explorer may be restarting at that very moment: give it up to 30 s before leaving the job to the next
        // start or the RunOnce entry.
        for (int i = 0; i < 30 && taskbar.IsHidden && noOtherInstance() && !taskbar.Restore("watchdog"); i++)
            Thread.Sleep(1000);
        return 0;
    }
}
