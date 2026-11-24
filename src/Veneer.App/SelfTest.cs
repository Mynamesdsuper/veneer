#if DEBUG
using System.Diagnostics;
using System.Windows.Threading;
using Veneer.Core;

namespace Veneer.App;

/// <summary>
/// Debug builds only: drives the running app from the command line, without simulated input. Steps run in order:
/// <c>wait:&lt;seconds&gt;</c>, <c>menu</c> (open the logo menu), <c>close-menu</c>, <c>screenshot</c>,
/// <c>dock-click</c>, <c>dock-close</c>, <c>dock-pin</c>, <c>dock-unpin</c>, <c>dock-menu</c> with <c>:&lt;text&gt;</c>
/// (the first dock icon whose key contains the text), <c>demo</c> (dock shows pinned apps only), <c>dock-hover:&lt;0..1|off&gt;</c>, <c>dock-reveal:on|off</c>, <c>dock-sweep</c> (magnification frame rate), <c>app</c> (start winver.exe so the foreground app changes; it holds no user data), <c>close-app</c>, <c>crash</c> (unhandled exception), <c>menu-item:&lt;text&gt;</c> (clicks the open menu's first item containing the text), <c>veneer-menu</c> (Veneer's own menu at the mouse, as from the notification-area icon),
/// <c>profile:&lt;name&gt;</c>, <c>theme:&lt;folder name&gt;</c>, <c>autostart:on|off</c> (writes HKCU Run), <c>quit</c>.
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
                    app.FirstDock?.ClickItem(key);
                    break;
                case ["dock-close", var key]:
                    app.FirstDock?.CloseItem(key);
                    break;
                case ["dock-pin", var key]:
                    app.FirstDock?.PinItem(key);
                    break;
                case ["dock-unpin", var key]:
                    app.FirstDock?.UnpinItem(key);
                    break;
                case ["dock-menu", var key]:
                    app.FirstDock?.OpenMenu(key);
                    break;
                case ["dock-hover", var fraction]:
                    app.FirstDock?.Hover(fraction == "off" ? null : double.Parse(fraction, System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case ["dock-reveal", var on]:
                    app.FirstDock?.SetRevealed(on == "on");
                    Log.Write($"self-test: dock revealed = {app.FirstDock?.Revealed}, visible = {app.FirstDock?.IsVisible}");
                    break;
                case ["dock-state"]:
                    Log.Write($"self-test: dock revealed = {app.FirstDock?.Revealed}, visible = {app.FirstDock?.IsVisible}");
                    break;
                case ["demo"]:
                    DockWindow.Demo = true; // screenshots for the public repository: no personal running apps
                    app.FirstDock?.Refresh(force: true);
                    break;
                case ["dock-sweep"]:
                    app.FirstDock?.Sweep();
                    break;
                case ["crash"]:
                    throw new InvalidOperationException("simulated crash (self-test)");
                case ["menu-item", var text]:
                    if (Modules.LastMenu is { } open && open.Items.OfType<System.Windows.Controls.MenuItem>().FirstOrDefault(i => (i.Header as string)?.Contains(text) == true) is { } item)
                    {
                        open.IsOpen = false; // a real click closes the menu first
                        item.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
                    }

                    break;
                case ["veneer-menu"]:
                    app.OpenVeneerMenu();
                    break;
                case ["profile", var name]:
                    app.SwitchProfile(name);
                    break;
                case ["theme", var name]:
                    app.SetTheme(name);
                    break;
                case ["autostart", var on]:
                    Veneer.Interop.Autostart.Set(on == "on");
                    Log.Write($"self-test: autostart = {Veneer.Interop.Autostart.IsEnabled}");
                    break;
                case ["quit"]:
                    app.Quit();
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
