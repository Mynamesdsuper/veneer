#if DEBUG
using System.Diagnostics;
using System.Windows.Threading;
using Veneer.Core;

namespace Veneer.App;

/// <summary>
/// Debug builds only: drives the running app from the command line, without simulated input. Steps run in order:
/// <c>wait:&lt;seconds&gt;</c>, <c>menu</c> (open the logo menu), <c>close-menu</c>, <c>screenshot</c>,
/// <c>dock-click</c>, <c>dock-close</c>, <c>dock-pin</c>, <c>dock-unpin</c>, <c>dock-menu</c> with <c>:&lt;text&gt;</c>
/// (the first dock icon whose key contains the text), <c>demo</c> (dock shows pinned apps only), <c>dock-hover:&lt;0..1|off&gt;</c>, <c>dock-reveal:on|off</c>, <c>dock-sweep</c> (magnification frame rate), <c>app</c> (start winver.exe so the foreground app changes; it holds no user data; again for a second window), <c>close-app</c> (closes them all), <c>dock-shot</c> (the dock icons drawn by WPF: no other window), <c>menu-shot</c> (the open menu, drawn by WPF), <c>menu-count</c> (its item count to the log), <c>trash-drop:&lt;path, with | for the drive colon&gt;</c> (as if dropped on the Recycle Bin item), <c>crash</c> (unhandled exception), <c>stall:&lt;ms&gt;</c> (blocks the UI thread, for the stall watch), <c>menu-item:&lt;text&gt;</c> (clicks the open menu's first item containing the text), <c>veneer-menu</c> (Veneer's own menu at the mouse, as from the notification-area icon),
/// <c>profile:&lt;name&gt;</c>, <c>theme:&lt;folder name&gt;</c>, <c>autostart:on|off</c> (writes HKCU Run),
/// <c>settings[:&lt;page id&gt;]</c>, <c>settings-shot</c> (the settings window only), <c>settings-render</c> (the same, drawn by WPF
/// instead of copied from the screen: no other window can show in it; no frame shadow or rounded corners),
/// <c>settings-mode:light|dark</c> (that window only, the Windows mode stays), <c>settings-set:&lt;surface&gt;:&lt;key&gt;:&lt;value&gt;</c>,
/// <c>settings-reset:&lt;surface&gt;:&lt;key&gt;</c>, <c>settings-install:&lt;zip, with | for the drive colon&gt;</c>,
/// <c>settings-report</c> (first report lines to the log), <c>settings-rename:&lt;old&gt;:&lt;new&gt;</c>, <c>settings-close</c>,
/// <c>settings-maximize</c> (maximize or restore), <c>frame-hittest</c> (the title bar's hit-test answers to the log),
/// <c>pin-offer[:first]</c> (the taskbar import offer; <c>first</c> saves the first-run flag on close), <c>pin-offer-shot</c>,
/// <c>pin-offer-import</c>, <c>pin-offer-close</c>, <c>culture:&lt;name&gt;</c> (e.g. <c>en-US</c>: UI texts and clock), <c>quit</c>.
/// Example: <c>--self-test "wait:2,menu,wait:1,screenshot,close-menu,wait:1,quit"</c>.
/// </summary>
static class SelfTest
{
    public static void Run(string script, App app)
    {
        var steps = new Queue<string>(script.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        var started = new List<Process>();
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
                case ["bar-unpin", var key]:
                    app.Bars.Select(b => b.FindModule<AppsModule>()).FirstOrDefault(m => m is not null)?.UnpinItem(key);
                    break;
                case ["bar-shot"]:
                    // Only the bars and a few pixels around them: the real glass and corners, no other window.
                    foreach (var bar in app.Bars)
                    {
                        var b = bar.Bounds;
                        var screen = Veneer.Interop.Monitors.Primary().Bounds;
                        app.SaveScreenshot(new Veneer.Core.Layout.PixelRect(Math.Max(screen.Left, b.Left - 4), Math.Max(screen.Top, b.Top - 4),
                            Math.Min(screen.Right, b.Right + 4), Math.Min(screen.Bottom, b.Bottom + 4)));
                    }

                    break;
                case ["app"]:
                    started.Add(Process.Start("winver.exe"));
                    break;
                case ["close-app"]:
                    foreach (var p in started)
                        Veneer.Interop.WindowTools.Quit(p.Id);
                    break;
                case ["dock-shot"]:
                    if (app.FirstDock?.IconsView is { } icons)
                        app.SaveRendering(icons);
                    break;
                case ["menu-shot"]:
                    if (Modules.LastMenu is { IsOpen: true } openMenu)
                        app.SaveRendering(openMenu);
                    break;
                case ["trash-drop", var path]:
                    // What a file dropped on the Recycle Bin item does (| stands for the drive colon).
                    DockWindow.MoveToTrash([path.Replace('|', ':')]);
                    break;
                case ["menu-count"]:
                    Log.Write($"self-test: open menu has {(Modules.LastMenu is { IsOpen: true } m ? m.Items.Count : -1)} items");
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
                case ["stall", var ms]:
                    using (StallWatch.Doing("self-test stall"))
                        Thread.Sleep(int.Parse(ms, System.Globalization.CultureInfo.InvariantCulture)); // blocks the UI thread on purpose
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
                case ["settings"]:
                    app.OpenSettings();
                    app.Settings!.Topmost = true; // a self-test cannot take the foreground: never shoot the windows behind
                    break;
                case ["settings", var page]:
                    app.OpenSettings(page);
                    app.Settings!.Topmost = true;
                    break;
                case ["settings-shot"]:
                    if (app.Settings is { } shown)
                        app.SaveScreenshot(shown.ScreenBounds());
                    break;
                case ["settings-mode", var mode]:
                    // Light or dark for this window only, to picture both without changing the Windows mode.
#pragma warning disable WPF0001 // Fluent ThemeMode is marked experimental
                    app.Settings?.ThemeMode = mode == "light" ? System.Windows.ThemeMode.Light : System.Windows.ThemeMode.Dark;
#pragma warning restore WPF0001
                    break;
                case ["settings-render"]:
                    if (app.Settings is { } drawn)
                        app.SaveRendering(drawn);
                    break;
                case ["settings-set", var surface, var key, var value]:
                    SetSetting(app, surface, key, value);
                    break;
                case ["settings-reset", var surface, var key]:
                    SetSetting(app, surface, key, null);
                    break;
                case ["settings-install", var zip]:
                    app.Settings?.InstallFrom(zip.Replace('|', ':')); // a drive colon would split the step: write C|\...
                    break;
                case ["settings-report"]:
                    foreach (string line in app.Settings?.DiagnosticsText().Split('\n').Take(8) ?? [])
                        Log.Write($"self-test: report: {line.TrimEnd()}");
                    break;
                case ["settings-rename", var old, var name]:
                    app.Settings?.RenameProfile(old, name);
                    break;
                case ["settings-close"]:
                    app.Settings?.Close();
                    break;
                case ["settings-maximize"]:
                    if (app.Settings is { } toMaximize)
                        toMaximize.WindowState = toMaximize.WindowState == System.Windows.WindowState.Maximized ? System.Windows.WindowState.Normal : System.Windows.WindowState.Maximized;
                    break;
                case ["frame-hittest"]:
                    if (app.Settings is { } framed)
                    {
                        // The answers Windows gets from Veneer's title bar: 9 = maximize button (snap layouts), 2 = caption (drag).
                        nint hwnd = new System.Windows.Interop.WindowInteropHelper(framed).Handle;
                        var max = framed.MaximizeCenterOnScreen();
                        var bar = framed.TitleBarPointOnScreen();
                        Log.Write($"self-test: hit test maximize = {Veneer.Interop.WindowTools.HitTest(hwnd, (int)max.X, (int)max.Y)}, title bar = {Veneer.Interop.WindowTools.HitTest(hwnd, (int)bar.X, (int)bar.Y)}, state = {framed.WindowState}");
                    }

                    break;
                case ["culture", var name]:
                    // Screenshots for the public (English) README on a PC in another language: texts and clock.
                    var culture = System.Globalization.CultureInfo.GetCultureInfo(name);
                    System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.CurrentUICulture = culture;
                    System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
                    break;
                case ["pin-offer"]:
                case ["pin-offer", "first"]:
                    Log.Write($"self-test: pin offer shown = {app.OfferPinImport(firstRun: step.EndsWith(":first"))}");
                    if (app.PinImportOffer is { } offer)
                        offer.Topmost = true;
                    break;
                case ["pin-offer-shot"]:
                    if (app.PinImportOffer is { } window)
                    {
                        var scale = System.Windows.PresentationSource.FromVisual(window)?.CompositionTarget?.TransformToDevice ?? System.Windows.Media.Matrix.Identity;
                        app.SaveScreenshot(new((int)(window.Left * scale.M11), (int)(window.Top * scale.M22),
                            (int)((window.Left + window.ActualWidth) * scale.M11), (int)((window.Top + window.ActualHeight) * scale.M22)));
                    }

                    break;
                case ["pin-offer-import"]:
                    app.PinImportOffer?.Import();
                    break;
                case ["pin-offer-close"]:
                    app.PinImportOffer?.Close();
                    Log.Write($"self-test: pin import offered = {app.Profiles.PinImportOffered}");
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

    // The same path as the Customize page: the value goes through SurfaceSetting into the profile's overrides.
    static void SetSetting(App app, string surfaceId, string key, string? text)
    {
        var theme = app.CurrentTheme;
        var setting = theme.Surfaces.Where(s => s.Id == surfaceId).SelectMany(s => Veneer.Core.Settings.SurfaceSettings.For(theme, s)).FirstOrDefault(s => s.Key == key);
        if (setting is null || app.Settings is not { } window)
        {
            Log.Write($"self-test: no setting {surfaceId}.{key}, or the settings window is closed");
            return;
        }

        object? value = text is null ? null : setting.Kind switch
        {
            Veneer.Core.Settings.SettingKind.Toggle => bool.Parse(text),
            Veneer.Core.Settings.SettingKind.Number => double.Parse(text, System.Globalization.CultureInfo.InvariantCulture),
            _ => text,
        };
        window.Set(setting, value);
    }
}
#endif
