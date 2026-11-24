using Veneer.Core;
using Veneer.Interop;

namespace Veneer.App;

/// <summary>
/// Command line:
/// <code>
///   Veneer.App [--theme &lt;folder&gt;] [--variant &lt;name&gt;] [--overrides &lt;file&gt;] [--keep-taskbar]
///   Veneer.App --restore-taskbar     give the taskbar back by hand and exit
///   Veneer.App --restore-windows-settings   put back the wallpaper, accent and light/dark Veneer changed
///   Veneer.App --watchdog &lt;pid&gt;     internal: started by Veneer itself
///   --self-test "&lt;steps&gt;"          debug builds only, see SelfTest
/// </code>
/// Without <c>--theme</c> the built-in theme is used. Profiles replace these options in Phase 6.
/// </summary>
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // The watchdog and its launcher must not create any window: handle them before WPF starts.
        if (args.Length == 2 && int.TryParse(args[1], out int pid))
        {
            if (args[0] == Watchdog.LaunchArgument)
                return Watchdog.Launch(pid);
            if (args[0] == Watchdog.Argument)
                return Watchdog.Run(pid, new Taskbar(), NoOtherInstance);
        }

        var taskbar = new Taskbar();
        if (args.Contains(Taskbar.SignInRestoreArgument))
        {
            // Started by the RunOnce safety net, at sign-in or when Explorer restarts. Windows consumed the entry:
            // with a Veneer still running, only put the entry back; otherwise restore.
            if (!NoOtherInstance())
                Taskbar.SetRunOnce(true);
            else
                taskbar.RestoreWhenShellReady("sign-in safety net", TimeSpan.FromSeconds(30));
            return 0;
        }

        if (args.Contains("--restore-windows-settings"))
            return new WindowsSettingsService().Restore() ? 0 : 1; // the settings window and tray get a button in Phase 6

        if (args.Contains("--restore-taskbar"))
        {
            // Manual emergency command: restores even if a (hung) Veneer is still running.
            taskbar.RestoreWhenShellReady("command line", TimeSpan.FromSeconds(30));
            return 0;
        }

        // One Veneer at a time: two would fight over the taskbar state and reserve the bars twice.
        using var instance = new Mutex(initiallyOwned: true, InstanceMutex, out bool first);
        if (!first)
        {
            Log.Write("another Veneer is already running; exiting");
            return 0;
        }

        // A previous run died without giving the taskbar back (for example, both processes were killed).
        if (taskbar.IsHidden)
            taskbar.Restore("next start (dirty state)");

        var options = new Options(
            Value(args, "--theme"),
            Value(args, "--variant"),
            Value(args, "--overrides"),
            KeepTaskbar: args.Contains("--keep-taskbar"),
            SelfTest: Value(args, "--self-test"));
        Log.Write($"start: {string.Join(' ', args)}");
        int code = new App(options, taskbar).Run();
        Log.Write("main returned");
        return code;
    }

    const string InstanceMutex = @"Local\Veneer.App";

    // For the watchdog: restore only if no other Veneer took over in the meantime (a new instance owns the state).
    static bool NoOtherInstance()
    {
        using var mutex = new Mutex(false, InstanceMutex);
        try
        {
            if (!mutex.WaitOne(0))
                return false;
        }
        catch (AbandonedMutexException)
        {
            // the dead main process still owned it: now we do, and nobody else is running
        }

        mutex.ReleaseMutex();
        return true;
    }

    static string? Value(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}

sealed record Options(string? Theme, string? Variant, string? Overrides, bool KeepTaskbar, string? SelfTest);
