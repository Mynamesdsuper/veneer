using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Veneer.Core;
using Veneer.Core.Layout;
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
        SessionEnding += (_, e) => { Log.Write($"session ending: {e.ReasonSessionEnding}"); Cleanup(); };
        Exit += (_, _) => Cleanup();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CreateMessageWindow();

        themes.Loaded += r => Dispatcher.BeginInvoke(() => OnThemeLoaded(r));
        if (options.Theme is not null)
            themes.Apply(options.Theme, options.Variant, options.Overrides);

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
        if (result.Success || bars.Count == 0)
            Rebuild();
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

        var theme = themes.Current;
        var monitor = Monitors.Primary();
        var barSurfaces = theme.Surfaces.OfType<BarSurface>().ToList();
        var rects = SurfaceLayout.Compute(monitor.Bounds,
            barSurfaces.Select(b => new EdgeItem(b.Id, b.Edge, SurfaceLayout.ToPhysical(b.Thickness, monitor.Dpi))));

        foreach (var surface in barSurfaces)
        {
            var window = new BarWindow(theme, surface, rects[surface.Id], monitor.Dpi);
            window.Show();
            window.Place();
            bars.Add(window);
        }

        Log.Write($"theme \"{theme.Id}\": {bars.Count} bar(s) on a {monitor.Bounds.Width}x{monitor.Bounds.Height} monitor at {monitor.Dpi} dpi");
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
        if (msg == Taskbar.TaskbarCreatedMessage)
        {
            Log.Write("Explorer restarted");
            taskbar.OnShellRestarted();
            foreach (var bar in bars)
                bar.OnShellRestarted();
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
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        foreach (var bar in bars)
            bar.RemoveAppBar();
        taskbar.Restore("exit");
        Modules.Shutdown();
        hotKeys?.Dispose();
        themes.Dispose();
        Log.Write("exit");
    }
}
