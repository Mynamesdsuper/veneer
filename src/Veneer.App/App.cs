using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Veneer.Core;
using Veneer.Core.Layout;
using Veneer.Core.Profiles;
using Veneer.Core.Settings;
using Veneer.Core.Themes;
using Veneer.Interop;

namespace Veneer.App;

/// <summary>Owns the theme, the surface windows and the taskbar. Every exit path goes through <see cref="Cleanup"/>.</summary>
sealed class App : Application
{
    const int HotKeyQuit = 1, HotKeyReload = 2, HotKeyRestoreTaskbar = 3, HotKeyScreenshot = 4, HotKeyCrash = 5, HotKeyLogoMenu = 6, HotKeyNextProfile = 7;

    /// <summary>Where themes are found by name: the user's installed themes first, then the bundled ones.</summary>
    static readonly string[] ThemeRoots = [AppPaths.Themes, Path.Combine(AppContext.BaseDirectory, "themes")];

    /// <summary>
    /// What the surfaces are built from: the active profile's theme, overrides and Windows preferences. Profile is
    /// null when <c>--theme</c> on the command line turns profiles off; ThemeFolder is null for the built-in theme.
    /// </summary>
    sealed record Session(string? Profile, string? ThemeFolder, string OverridesPath, string PreferencesPath);

    readonly Options options;
    readonly ProfileStore profiles = new(AppPaths.Root);
    Session session = null!; // set in OnStartup
    TrayIcon? tray;
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
        SessionEnding += (_, e) => { Log.Write($"session ending: {e.ReasonSessionEnding}"); Cleanup(); Quit(); };
        Exit += (_, _) => Cleanup();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CreateMessageWindow();

        themes.Loaded += r => Dispatcher.BeginInvoke(() => OnThemeLoaded(r));
        Modules.VeneerMenu = VeneerMenu;
        LoadSession(rebuildBuiltIn: false); // the surfaces are built below, once the taskbar is hidden

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
        if (session.ThemeFolder is null)
            Rebuild(); // otherwise the Loaded event of Apply() above builds the surfaces
    }

    Session NewSession()
    {
        string active = profiles.Active;
        string overrides = options.Overrides ?? profiles.OverridesPath(active);
        if (options.Theme is not null)
            return new Session(null, options.Theme, overrides, profiles.PreferencesPath(active));

        var warnings = new List<string>();
        var profile = profiles.Load(active, warnings);
        foreach (string w in warnings)
            Log.Write($"profile \"{active}\": {w}");
        string? folder = ThemeFolders.Find(profile.Theme, ThemeRoots);
        if (folder is null)
        {
            Log.Write($"profile \"{active}\": theme \"{profile.Theme}\" not found; using \"{Profile.DefaultTheme}\"");
            folder = ThemeFolders.Find(Profile.DefaultTheme, ThemeRoots); // null: the built-in fallback theme
        }

        return new Session(active, folder, overrides, profiles.PreferencesPath(active));
    }

    /// <summary>Loads the active profile (or the command line's theme). The theme's Loaded event rebuilds the surfaces.</summary>
    void LoadSession(bool rebuildBuiltIn = true)
    {
        session = NewSession();
        Log.Write($"profile: {session.Profile ?? "(none: --theme)"}, theme: {session.ThemeFolder ?? "(built-in)"}");
        variant = options.Variant;
        applied = null; // rebuild everything and apply the profile's Windows settings, even if only the pins differ
        if (session.ThemeFolder is null)
        {
            // shortcut: the theme manager cannot go back to the built-in theme; the current theme stays on screen.
            if (rebuildBuiltIn)
            {
                Rebuild();
                Changed?.Invoke();
            }

            return;
        }

        // The folder must exist so the overrides file is watched even before it is first written.
        Directory.CreateDirectory(Path.GetDirectoryName(session.OverridesPath)!);
        themes.Apply(session.ThemeFolder, variant, session.OverridesPath);
    }

    /// <summary>Makes <paramref name="name"/> the active profile and switches to it.</summary>
    public void SwitchProfile(string name)
    {
        if (session.Profile is null)
            return; // --theme: profiles are off
        Try($"switch to profile \"{name}\"", () =>
        {
            profiles.SetActive(name);
            LoadSession();
        });
    }

    /// <summary>Gives the active profile another theme.</summary>
    public void SetTheme(string name)
    {
        if (session.Profile is not { } active)
            return;
        Try($"switch to theme \"{name}\"", () =>
        {
            profiles.Save(active, profiles.Load(active, []) with { Theme = name });
            LoadSession();
        });
    }

    // A menu action that writes files: a full disk or a locked file is logged, never fatal.
    static void Try(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"could not {what}: {e.Message}");
        }
    }

    /// <summary>Veneer's own menu: in the bar (logo menu → Veneer) and on the notification-area icon.</summary>
    System.Windows.Controls.ContextMenu VeneerMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();
        bool profilesOn = session.Profile is not null;
        menu.Items.Add(Modules.Header(Strings.MenuProfile));
        foreach (string name in profiles.Names())
            menu.Items.Add(Modules.Item(Modules.Checked(name, name == session.Profile), () => SwitchProfile(name), profilesOn));
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(Modules.Header(Strings.MenuTheme));
        foreach (var theme in ThemeFolders.List(ThemeRoots))
        {
            bool current = string.Equals(theme.Folder, session.ThemeFolder, StringComparison.OrdinalIgnoreCase);
            menu.Items.Add(Modules.Item(Modules.Checked(theme.DisplayName, current), () => SetTheme(theme.Name), profilesOn));
        }

        menu.Items.Add(new System.Windows.Controls.Separator());
        bool autostart = Autostart.IsEnabled;
        menu.Items.Add(Modules.Item(Modules.Checked(Strings.MenuStartWithWindows, autostart), () => Try("change autostart", () => Autostart.Set(!autostart))));
        menu.Items.Add(Modules.Item(Strings.MenuRestoreTaskbar, () => taskbar.Restore("menu")));
        menu.Items.Add(Modules.Item(Strings.MenuRestoreWindowsSettings, () => RestoreWindowsSettings(), windowsSettings.HasSnapshot));
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(Modules.Item(Strings.MenuSettings, () => OpenSettings()));
        menu.Items.Add(Modules.Item(Strings.MenuQuit, Quit));
        return menu;
    }

    SettingsWindow? settings;

    public SettingsWindow? Settings => settings;

    /// <summary>Opens the settings window on <paramref name="page"/>, or brings it to the front when it is already open.</summary>
    public void OpenSettings(string? page = null)
    {
        if (settings is null)
        {
            settings = new SettingsWindow(this);
            settings.Closed += (_, _) => settings = null;
        }

        settings.ShowPage(page);
    }

    // What the settings window reads and calls.
    internal ProfileStore Profiles => profiles;

    internal string? ActiveProfile => session.Profile;

    internal string? ThemeFolder => session.ThemeFolder;

    internal string OverridesPath => session.OverridesPath;

    internal string PreferencesPath => session.PreferencesPath;

    internal Theme CurrentTheme => themes.Current;

    internal ThemeLoadResult? LastLoad => themes.LastResult;

    internal bool HasWindowsSnapshot => windowsSettings.HasSnapshot;

    internal IReadOnlyList<ThemeEntry> InstalledThemes() => ThemeFolders.List(ThemeRoots);

    internal void RestoreTaskbar() => taskbar.Restore("settings");

    /// <summary>Raised on the UI thread after every theme load: the settings window refreshes.</summary>
    internal event Action? Changed;

    /// <summary>The active profile's folder moved (renamed): load it again from its new place.</summary>
    internal void ReloadSession() => Try("reload the profile", () => LoadSession());

    /// <summary>windows.json changed: apply the Windows settings again, giving back what is now left to Windows.</summary>
    internal void PreferencesChanged()
    {
        applied = null;
        if (session.ThemeFolder is null)
        {
            Rebuild(); // the built-in theme: no Windows settings to apply
            Changed?.Invoke();
        }
        else
        {
            themes.Reload();
        }
    }

    // Slow (every app answers the change notification): off the UI thread.
    internal Task RestoreWindowsSettings() => Task.Run(() =>
    {
        try
        {
            windowsSettings.Restore();
        }
        catch (Exception e)
        {
            Log.Write($"windows settings not restored: {e}");
        }
    });

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

            Changed?.Invoke();
            return;
        }

        if (result.Success)
            ApplyWindowsSettings(result.Theme!);
        if (result.Success || bars.Count == 0)
            Rebuild();
        Changed?.Invoke(); // after a failed load too: the settings window lists its problems
    }

    internal WindowsPreferences Preferences()
    {
        var warnings = new List<string>();
        string? json = null;
        try
        {
            if (File.Exists(session.PreferencesPath))
                json = File.ReadAllText(session.PreferencesPath);
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
        if (options.Variant is not null || session.ThemeFolder is null)
            return false;
        bool? desired = WindowsSettingsPlan.DesiredDark(theme.Windows.Appearance, Preferences().Appearance);
        string wanted = WindowsSettingsPlan.Variant(desired, WindowsSettingsService.WindowsIsDark);
        if (wanted == variant)
            return false;
        variant = wanted;
        Log.Write($"theme variant: {variant}");
        themes.Apply(session.ThemeFolder, variant, session.OverridesPath);
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
        ThemeVisuals.Current = theme;
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
            var window = new DockWindow(theme, surface, remaining, monitor.Dpi, appWindows, session.ThemeFolder is null ? null : session.OverridesPath);
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
        RegisterHotKey(HotKeyNextProfile, 'P');
        tray = new TrayIcon(messages.Handle, "Veneer");
        tray.Clicked += () => Modules.OpenAtMouse(VeneerMenu());
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
            tray?.OnShellRestarted();
            foreach (var bar in bars)
                bar.OnShellRestarted();
            foreach (var dock in docks)
                dock.OnShellRestarted();
        }
        else if (hotKeys!.WndProc(msg, wParam) || tray?.WndProc(msg, lParam) == true)
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
                Quit();
                break;
            case HotKeyReload:
                if (themes.Reload() is null)
                    Rebuild();
                break;
            case HotKeyRestoreTaskbar:
                taskbar.Restore("hotkey");
                break;
            case HotKeyNextProfile:
                if (session.Profile is { } current)
                    SwitchProfile(profiles.Next(current));
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

    /// <summary>
    /// Every way out goes through here. An open menu is closed first, and the shutdown waits until WPF has finished
    /// closing it: a blurred (acrylic) menu still open while WPF shuts down its dispatcher keeps <c>Run</c> from ever
    /// returning, and the process hangs.
    /// </summary>
    public void Quit()
    {
        Modules.CloseMenu();
        Dispatcher.BeginInvoke(() => Shutdown(), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    public DockWindow? FirstDock => docks.FirstOrDefault();

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

    public void OpenVeneerMenu() => Modules.OpenAtMouse(VeneerMenu());

    public void OpenLogoMenu() => bars.Select(b => b.FindModule<LogoMenuModule>()).FirstOrDefault(m => m is not null)?.Open();

    /// <summary>Debug aid: the whole primary monitor as composed by DWM (blur included), for comparison with references/.</summary>
    public void SaveScreenshot(PixelRect? only = null)
    {
        var area = only ?? Monitors.Primary().Bounds;
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
            Step(() => tray?.Dispose());
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
