using Veneer.Core;
using Veneer.Interop;

namespace Veneer.App;

/// <summary>
/// Command line:
/// <code>
///   Veneer.App [--theme &lt;folder&gt;] [--variant &lt;name&gt;] [--overrides &lt;file&gt;] [--keep-taskbar]
///   Veneer.App --restore-taskbar     give the taskbar back by hand and exit
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
        // The watchdog must not create any window: handle it before WPF starts.
        if (args.Length == 2 && args[0] == Watchdog.Argument && int.TryParse(args[1], out int pid))
            return Watchdog.Run(pid, new Taskbar());

        var taskbar = new Taskbar();
        if (args.Contains("--restore-taskbar"))
        {
            taskbar.Restore("command line");
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

    static string? Value(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}

sealed record Options(string? Theme, string? Variant, string? Overrides, bool KeepTaskbar, string? SelfTest);
