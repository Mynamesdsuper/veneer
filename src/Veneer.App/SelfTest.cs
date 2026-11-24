#if DEBUG
using System.Diagnostics;
using System.Windows.Threading;
using Veneer.Core;

namespace Veneer.App;

/// <summary>
/// Debug builds only: drives the running app from the command line, without simulated input. Steps run in order:
/// <c>wait:&lt;seconds&gt;</c>, <c>menu</c> (open the logo menu), <c>close-menu</c>, <c>screenshot</c>,
/// <c>dock-click:&lt;text&gt;</c> and <c>dock-close:&lt;text&gt;</c> (the first dock icon whose key contains the text), <c>app</c> (start winver.exe so the foreground app changes; it holds no user data), <c>close-app</c>, <c>quit</c>.
/// Example: <c>--self-test "wait:2,menu,wait:1,screenshot,close-menu,wait:1,quit"</c>.
/// </summary>
static class SelfTest
{
    public static void Run(string script, App app)
    {
        var steps = new Queue<string>(script.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        Process? started = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.Zero };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!steps.TryDequeue(out string? step))
                return;
            Log.Write($"self-test: {step}");
            var next = TimeSpan.FromMilliseconds(50);
            switch (step.Split(':'))
            {
                case ["wait", var seconds]:
                    next = TimeSpan.FromSeconds(double.Parse(seconds, System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case ["menu"]:
                    app.OpenLogoMenu();
                    break;
                case ["close-menu"]:
                    if (Modules.LastMenu is { } menu)
                        menu.IsOpen = false;
                    break;
                case ["screenshot"]:
                    app.SaveScreenshot();
                    break;
                case ["app"]:
                    started = Process.Start("winver.exe");
                    break;
                case ["close-app"]:
                    if (started is not null)
                        Veneer.Interop.WindowTools.Quit(started.Id);
                    break;
                case ["dock-click", var key]:
                    app.ClickDockItem(key);
                    break;
                case ["dock-close", var key]:
                    app.CloseDockItem(key);
                    break;
                case ["quit"]:
                    app.Shutdown();
                    return;
                default:
                    Log.Write($"self-test: unknown step \"{step}\"");
                    break;
            }

            timer.Interval = next;
            timer.Start();
        };
        timer.Start();
    }
}
#endif
