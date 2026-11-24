using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Veneer.Core;
using Veneer.Core.Layout;
using Veneer.Core.Settings;
using Veneer.Core.Themes;
using Veneer.Interop;

namespace Veneer.App;

/// <summary>Owns the theme, the surface windows and the taskbar. Every exit path goes through <see cref="Cleanup"/>.</summary>
sealed class App : Application
{
    const int HotKeyQuit = 1, HotKeyReload = 2, HotKeyRestoreTaskbar = 3, HotKeyScreenshot = 4, HotKeyCrash = 5, HotKeyLogoMenu = 6;

    readonly Options options;
    readonly ITaskbar taskbar;
    readonly ThemeManager themes = new();
    readonly List<BarWindow> bars = [];
    readonly List<DockWindow> docks = [];
    AppWindowTracker? appWindows;
    bool fullScreen;
    Theme? applied; // the theme the surfaces were last built from
    string? variant; // the variant the theme is loaded with
    readonly WindowsSettingsService windowsSettings = new();
    HwndSource? messages;
    HotKeys? hotKeys;
    int cleanedUp;

    public App(Options options, ITaskbar taskbar)
    {
        this.options = options;
        this.taskbar = taskbar;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
#pragma warning disable WPF0001 // Fluent theme is marked experimental; it gives menus the Windows 11 look.
        ThemeMode = ThemeMode.System;
#pragma warning restore WPF0001
        DispatcherUnhandledException += (_, e) => Crash("dispatcher", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Crash("appdomain", e.ExceptionObject as Exception);
        // Give everything back, then really exit: if another app cancels the logoff, a half-cleaned Veneer (bars no
        // longer reserved, hotkeys gone) would be worse than none. The user can start it again.
        SessionEnding += (_, e) => { Log.Write($"session ending: {e.ReasonSessionEnding}"); Cleanup(); Shutdown(); };
        Exit += (_, _) => Cleanup();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CreateMessageWindow();

        themes.Loaded += r => Dispatcher.BeginInvoke(() => OnThemeLoaded(r));
        if (options.Theme is not null)
        {
            // The folder must exist so the overrides file is watched even before it is first written.
            Directory.CreateDirectory(Path.GetDirectoryName(OverridesPath)!);
            variant = options.Variant;
            themes.Apply(options.Theme, variant, OverridesPath);
        }

        if (!options.KeepTaskbar)
        {
            Watchdog.Start();
            taskbar.Hide(); // before the bars register, so bottom bars sit on the screen edge
        }

        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
#if DEBUG
        if (options.SelfTest is not null)
            SelfTest.Run(options.SelfTest, this);
#endif
        if (options.Theme is null)
            Rebuild(); // otherwise the Loaded event of Apply() above builds the surfaces
    }

    void OnThemeLoaded(ThemeLoadResult result)
    {
        foreach (var d in result.Diagnostics)
            Log.Write($"theme: {d}");
        if (!result.Success)
            Log.Write($"theme not loaded, keeping \"{themes.Current.Id}\"");
        else if (ChooseVariant(result.Theme!))
            return; // reloading with the right light/dark variant; this handler runs again
        else if (applied is not null && ThemeChanges.OnlyDockPinnedChanged(applied, result.Theme!))
        {
            // Pinning, unpinning or reordering in the dock reloads the theme through the overrides file. Rebuilding
            // every surface would release and reserve the screen strips and resize every maximized window twice.
            applied = result.Theme!;
            foreach (var dock in docks)
            {
                if (applied.Surfaces.OfType<DockSurface>().FirstOrDefault(d => d.Id == dock.Dock.Id) is { } updated)
                    dock.UpdatePinned(updated.DefaultPinned);
            }

            return;
        }

        if (result.Success)
            ApplyWindowsSettings(result.Theme!);
        if (result.Success || bars.Count == 0)
            Rebuild();
    }

    static string PreferencesPath => Path.Combine(AppPaths.Profiles, "default", WindowsPreferences.FileName);

    WindowsPreferences Preferences()
    {
        var warnings = new List<string>();
        string? json = null;
        try
        {
            if (File.Exists(PreferencesPath))
                json = File.ReadAllText(PreferencesPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"{WindowsPreferences.FileName}: {e.Message}");
        }

        var preferences = WindowsPreferences.Parse(json, warnings);
        foreach (string w in warnings)
            Log.Write(w);
        return preferences;
    }

    /// <summary>
    /// Light or dark variant: the mode the preferences or the theme ask for, or Windows' own mode. Returns true when
    /// the theme is being reloaded with another variant. --variant on the command line wins.
    /// </summary>
    bool ChooseVariant(Theme theme)
    {
        if (options.Variant is not null || options.Theme is null)
            return false;
        bool? desired = WindowsSettingsPlan.DesiredDark(theme.Windows.Appearance, Preferences().Appearance);
        string wanted = WindowsSettingsPlan.Variant(desired, WindowsSettingsService.WindowsIsDark);
        if (wanted == variant)
            return false;
        variant = wanted;
        Log.Write($"theme variant: {variant}");
        themes.Apply(options.Theme, variant, OverridesPath);
        return true;
    }

    // Wallpaper, accent, light/dark from the theme, as the preferences allow. Never fatal: a failure is logged and
    // the shell keeps running with Windows as it is.
    void ApplyWindowsSettings(Theme theme)
    {
        var plan = WindowsSettingsPlan.For(theme, Preferences());
        // Off the UI thread: each change notification waits for every app to answer (seconds with a slow app).
        Task.Run(() =>
        {
            try
            {
                windowsSettings.Apply(plan);
            }
            catch (Exception e)
            {
                Log.Write($"windows settings not applied: {e}");
            }
        });
    }

    void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Rebuild);

    /// <summary>Closes every surface and builds them again from the current theme.</summary>
    void Rebuild()
    {
        foreach (var bar in bars)
        {
            bar.RemoveAppBar();
            bar.Close();
        }

        bars.Clear();
        foreach (var dock in docks)
        {
            dock.RemoveAppBar();
            dock.Close();
        }

        docks.Clear();

        var theme = themes.Current;
        if (!ReferenceEquals(theme, applied))
            DockIcons.Clear(); // a reloaded theme may have new icon files: no stale images
        applied = theme;
        var monitor = Monitors.Primary();
        var barSurfaces = theme.Surfaces.OfType<BarSurface>().ToList();
        var edgeItems = barSurfaces.Select(b => new EdgeItem(b.Id, b.Edge, SurfaceLayout.ToPhysical(b.Thickness, monitor.Dpi))).ToList();
        var rects = SurfaceLayout.Compute(monitor.Bounds, edgeItems);

        foreach (var surface in barSurfaces)
        {
            var window = new BarWindow(theme, surface, rects[surface.Id], monitor.Dpi);
            window.FullScreenChanged += OnFullScreenChanged;
            window.Show();
            window.Place();
            if (fullScreen)
                window.Visibility = Visibility.Hidden; // a resolution change during a game must not bring the bars back
            bars.Add(window);
        }

        var remaining = SurfaceLayout.Remaining(monitor.Bounds, edgeItems);
        var dockSurfaces = theme.Surfaces.OfType<DockSurface>().ToList();
        if (dockSurfaces.Count == 0)
        {
            // No dock: drop the system-wide window hooks, they would only cost CPU.
            appWindows?.Dispose();
            appWindows = null;
        }

        foreach (var surface in dockSurfaces)
        {
            appWindows ??= new AppWindowTracker();
            var window = new DockWindow(theme, surface, remaining, monitor.Dpi, appWindows, options.Theme is null ? null : OverridesPath);
            window.FullScreenChanged += OnFullScreenChanged;
            window.Show();
            window.Start();
            window.SetFullScreen(fullScreen);
            docks.Add(window);
        }

        Log.Write($"theme \"{theme.Id}\": {bars.Count} bar(s), {docks.Count} dock(s) on a {monitor.Bounds.Width}x{monitor.Bounds.Height} monitor at {monitor.Dpi} dpi");
    }

    // A hidden top-level window: it receives the shell's TaskbarCreated broadcast (message-only windows do not) and hotkeys.
    void CreateMessageWindow()
    {
        messages = new HwndSource(new HwndSourceParameters("Veneer") { Width = 0, Height = 0, WindowStyle = 0 });
        messages.AddHook(MessageHook);
        hotKeys = new HotKeys(messages.Handle);
        hotKeys.Pressed += OnHotKey;
        RegisterHotKey(HotKeyQuit, 'Q');
        RegisterHotKey(HotKeyReload, 'R');
        RegisterHotKey(HotKeyRestoreTaskbar, 'T');
#if DEBUG
        RegisterHotKey(HotKeyScreenshot, 'S');
        RegisterHotKey(HotKeyCrash, 'X'); // tests the crash path: the taskbar must come back
        RegisterHotKey(HotKeyLogoMenu, 'M'); // opens the first logo menu, for tests without the mouse
#endif
    }

    void RegisterHotKey(int id, char key)
    {
        if (!hotKeys!.Register(id, key))
            Log.Write($"hotkey Ctrl+Alt+Shift+{key} is taken by another app");
    }

    nint MessageHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        const int WM_SETTINGCHANGE = 0x001A;
        if (msg == WM_SETTINGCHANGE && lParam != 0 && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet" && themes.LastResult?.Theme is { } loaded)
        {
            // Light/dark changed in Windows (or by us): when following Windows, switch the theme variant.
            Dispatcher.BeginInvoke(() => ChooseVariant(loaded));
        }
        else if (msg == Taskbar.TaskbarCreatedMessage)
        {
            Log.Write("Explorer restarted");
            taskbar.OnShellRestarted();
            foreach (var bar in bars)
                bar.OnShellRestarted();
            foreach (var dock in docks)
                dock.OnShellRestarted();
        }
        else if (hotKeys!.WndProc(msg, wParam))
        {
            handled = true;
        }

        return 0;
    }

    void OnHotKey(int id)
    {
        switch (id)
        {
            case HotKeyQuit:
                Shutdown();
                break;
            case HotKeyReload:
                if (themes.Reload() is null)
                    Rebuild();
                break;
            case HotKeyRestoreTaskbar:
                taskbar.Restore("hotkey");
                break;
            case HotKeyScreenshot:
                SaveScreenshot();
                break;
            case HotKeyLogoMenu:
                OpenLogoMenu();
                break;
            case HotKeyCrash:
                throw new InvalidOperationException("simulated crash (debug hotkey)");
        }
    }

    public DockWindow? FirstDock => docks.FirstOrDefault();

    /// <summary>The user's overrides: --overrides, or the default profile's file until profiles exist (Phase 6).</summary>
    string OverridesPath => options.Overrides ?? Path.Combine(AppPaths.Profiles, "default", "overrides.json");

    // A full-screen app (game, video) is in front: get out of its way, and come back when it closes.
    void OnFullScreenChanged(bool isFullScreen)
    {
        if (isFullScreen == fullScreen)
            return; // every registered surface reports it: act once
        fullScreen = isFullScreen;
        Log.Write($"full-screen app: {fullScreen}");
        foreach (var bar in bars)
            bar.Visibility = fullScreen ? Visibility.Hidden : Visibility.Visible;
        foreach (var dock in docks)
            dock.SetFullScreen(fullScreen); // plate, icons and auto-hide strip together
    }

    public void OpenLogoMenu() => bars.Select(b => b.FindModule<LogoMenuModule>()).FirstOrDefault(m => m is not null)?.Open();

    /// <summary>Debug aid: the whole primary monitor as composed by DWM (blur included), for comparison with references/.</summary>
    public void SaveScreenshot()
    {
        var area = Monitors.Primary().Bounds;
        var pixels = ScreenCapture.Capture(area);
        var image = BitmapSource.Create(area.Width, area.Height, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null, pixels, area.Width * 4);
        string folder = ScreenshotFolder();
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"veneer-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        using (var file = File.Create(path))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(file);
        }

        Log.Write($"screenshot saved: {path}");
    }

    // artifacts/screenshots in the repository when running from a checkout, otherwise next to the logs.
    static string ScreenshotFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Veneer.slnx")))
                return Path.Combine(dir.FullName, "artifacts", "screenshots");
        }

        return Path.Combine(AppPaths.Root, "screenshots");
    }

    void Crash(string source, Exception? e)
    {
        Log.Write($"unhandled exception ({source}): {e}");
        Cleanup();
    }

    /// <summary>Unregisters the bars and gives the taskbar back. Runs once, from whichever exit path comes first.</summary>
    void Cleanup()
    {
        if (Interlocked.Exchange(ref cleanedUp, 1) == 1)
            return;
        // Each step on its own: a failure in one (this can run from a crashing thread while the UI is mid-rebuild)
        // must never skip the taskbar restore, which runs last, in finally.
        try
        {
            Step(() => SystemEvents.DisplaySettingsChanged -= OnDisplayChanged);
            foreach (var bar in bars.ToArray())
                Step(bar.RemoveAppBar);
            foreach (var dock in docks.ToArray())
                Step(dock.RemoveAppBar);
            Step(() => appWindows?.Dispose());
            Step(Modules.Shutdown);
            Step(() => hotKeys?.Dispose());
            Step(themes.Dispose);
        }
        finally
        {
            taskbar.Restore("exit");
            Log.Write("exit");
        }

        static void Step(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Log.Write($"cleanup step failed: {e.Message}");
            }
        }
    }
}
