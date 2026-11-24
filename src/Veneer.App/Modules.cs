using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Veneer.Core;
using Veneer.Core.Modules;
using Veneer.Core.Themes;
using Veneer.Interop;

namespace Veneer.App;

/// <summary>Where a module lives: the theme (fonts) and the edge of its bar (menus open away from the edge).</summary>
sealed record ModuleContext(Theme Theme, Edge Edge)
{
    public bool Horizontal => Edge is Edge.Top or Edge.Bottom;
}

/// <summary>
/// Module views by name. A new module adds one entry here (and its option checks in ThemeBinder.ModuleOptions);
/// the bar itself does not change.
/// </summary>
static class Modules
{
    static readonly Dictionary<string, Func<ModuleSpec, ModuleContext, FrameworkElement>> Factories = new()
    {
        ["clock"] = (spec, _) => new ClockModule(spec),
        ["spacer"] = (spec, ctx) => Spacer(spec, ctx.Horizontal),
        ["logo-menu"] = (spec, ctx) => new LogoMenuModule(spec, ctx),
        ["active-app"] = (spec, ctx) => new ActiveAppModule(spec, ctx),
        ["volume"] = (spec, _) => new VolumeModule(spec),
        ["network"] = (spec, _) => new NetworkModule(spec),
        ["battery"] = (spec, _) => new BatteryModule(spec),
    };

    // Shared by every module instance; created on first use, disposed at exit.
    static readonly Lazy<ForegroundWatcher> foreground = new(() => new ForegroundWatcher());
    static readonly Lazy<AudioEndpoint?> audio = new(() =>
    {
        try
        {
            return new AudioEndpoint();
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidCastException)
        {
            Log.Write($"volume: Core Audio unavailable: {e.Message}");
            return null;
        }
    });

    public static ForegroundWatcher Foreground => foreground.Value;

    public static AudioEndpoint? Audio => audio.Value;

    public static FrameworkElement? Create(ModuleSpec spec, ModuleContext ctx)
    {
        if (Factories.TryGetValue(spec.Module, out var create))
            return create(spec, ctx);
        Log.Write($"module \"{spec.Module}\" has no view, skipped");
        return null;
    }

    public static void Shutdown()
    {
        if (foreground.IsValueCreated)
            foreground.Value.Dispose();
        if (audio.IsValueCreated)
            audio.Value?.Dispose();
    }

    static FrameworkElement Spacer(ModuleSpec spec, bool horizontal)
    {
        double size = spec.Options["size"]?.GetValue<double>() ?? 8;
        return horizontal ? new Border { Width = size } : new Border { Height = size };
    }

    public static bool Flag(ModuleSpec spec, string name, bool fallback) => spec.Options[name]?.GetValue<bool>() ?? fallback;

    /// <summary>Opens a menu next to <paramref name="target"/>, on the side away from the bar's edge.</summary>
    public static void Open(ContextMenu menu, UIElement target, Edge edge)
    {
        LastMenu = menu;
        menu.PlacementTarget = target;
        menu.Placement = edge switch
        {
            Edge.Top => PlacementMode.Bottom,
            Edge.Bottom => PlacementMode.Top,
            Edge.Left => PlacementMode.Right,
            _ => PlacementMode.Left,
        };
        menu.IsOpen = true;
    }

    /// <summary>The menu opened most recently (used by the debug self-test to close it).</summary>
    public static ContextMenu? LastMenu { get; private set; }

    public static MenuItem Item(string header, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>Destructive actions always go through here.</summary>
    public static bool Confirm(string question) =>
        MessageBox.Show(question, "Veneer", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
}

/// <summary>A module that reacts to clicks: transparent background so the whole area is hit-testable.</summary>
abstract class ClickableModule : Border
{
    protected ClickableModule()
    {
        Background = Brushes.Transparent;
        MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            OnClick();
        };
    }

    protected abstract void OnClick();
}

/// <summary>
/// An icon that changes with a state: the theme's image for that state (module option <c>icons</c>), otherwise a
/// glyph from the Windows icon font in the text color.
/// </summary>
sealed class StateIcon : ContentControl
{
    static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    readonly IReadOnlyDictionary<string, string> images;
    string? state;

    public StateIcon(IReadOnlyDictionary<string, string> images)
    {
        this.images = images;
        Focusable = false;
        VerticalAlignment = VerticalAlignment.Center;
    }

    public void Show(string newState, string glyph)
    {
        if (newState + glyph == state)
            return;
        state = newState + glyph;
        double size = TextElement.GetFontSize(this) * 1.15;
        if (images.TryGetValue(newState, out string? path))
        {
            Content = new Image { Source = new BitmapImage(new Uri(path)), Height = size, Stretch = Stretch.Uniform };
        }
        else
        {
            Content = new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        }
    }
}

/// <summary>Current date/time in the theme's <c>format</c>. Wakes once per minute (per second if the format shows seconds).</summary>
sealed class ClockModule : TextBlock
{
    readonly string format;
    readonly DispatcherTimer timer = new();

    public ClockModule(ModuleSpec spec)
    {
        format = spec.Options["format"]?.GetValue<string>() ?? "HH:mm";
        timer.Tick += (_, _) => Update();
        Loaded += (_, _) =>
        {
            SystemEvents.TimeChanged += OnSystemChange;
            SystemEvents.UserPreferenceChanged += OnSystemChange; // locale and regional format changes
            Update();
        };
        Unloaded += (_, _) =>
        {
            // SystemEvents is static: unsubscribe or the module leaks after a theme reload.
            SystemEvents.TimeChanged -= OnSystemChange;
            SystemEvents.UserPreferenceChanged -= OnSystemChange;
            timer.Stop();
        };
    }

    bool ShowsSeconds => format.Contains('s') || format is "T" or "G" or "F" or "o" or "O" or "r" or "R" or "s" or "u" or "U";

    void OnSystemChange(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Update);

    void Update()
    {
        var now = DateTime.Now;
        Text = now.ToString(format, CultureInfo.CurrentCulture);
        var next = ShowsSeconds
            ? now.AddTicks(TimeSpan.TicksPerSecond - now.Ticks % TimeSpan.TicksPerSecond)
            : now.AddTicks(TimeSpan.TicksPerMinute - now.Ticks % TimeSpan.TicksPerMinute);
        timer.Interval = next - now + TimeSpan.FromMilliseconds(20);
        timer.Start();
    }
}

/// <summary>The theme's logo; a click opens the system menu.</summary>
sealed class LogoMenuModule : ClickableModule
{
    readonly ModuleContext ctx;

    public LogoMenuModule(ModuleSpec spec, ModuleContext ctx)
    {
        this.ctx = ctx;
        Child = spec.Icon is not null
            ? new Image { Source = new BitmapImage(new Uri(spec.Icon)), Height = 16, Stretch = Stretch.Uniform }
            : new TextBlock { Text = "⊞", FontSize = 16 }; // placeholder when the theme has no logo
    }

    protected override void OnClick() => Open();

    public void Open()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Modules.Item("About this PC", () => SystemActions.Open("ms-settings:about")));
        menu.Items.Add(Modules.Item("Settings…", () => SystemActions.Open("ms-settings:")));
        menu.Items.Add(new Separator());
        menu.Items.Add(Modules.Item("Sleep…", () => { if (Modules.Confirm("Put the computer to sleep now?")) SystemActions.Sleep(); }));
        menu.Items.Add(Modules.Item("Restart…", () => { if (Modules.Confirm("Restart the computer now? Unsaved work in open apps may be lost.")) SystemActions.Restart(); }));
        menu.Items.Add(Modules.Item("Shut Down…", () => { if (Modules.Confirm("Shut down the computer now? Unsaved work in open apps may be lost.")) SystemActions.ShutDown(); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Modules.Item("Lock Screen", SystemActions.Lock));
        Modules.Open(menu, this, ctx.Edge);
    }
}

/// <summary>Name of the foreground app; a click opens a fixed menu. The app's own menus are never read.</summary>
sealed class ActiveAppModule : ClickableModule
{
    readonly ModuleContext ctx;
    readonly string desktopName;
    readonly Dictionary<string, string> names;
    readonly TextBlock text = new();
    WindowDetails? target;

    public ActiveAppModule(ModuleSpec spec, ModuleContext ctx)
    {
        this.ctx = ctx;
        desktopName = spec.Options["desktopName"]?.GetValue<string>() ?? "Desktop";
        names = spec.Options["names"] is System.Text.Json.Nodes.JsonObject map
            ? map.ToDictionary(kv => kv.Key.ToLowerInvariant(), kv => kv.Value!.GetValue<string>())
            : [];
        Child = text;
        Loaded += (_, _) =>
        {
            Modules.Foreground.Changed += OnForeground;
            Show(ForegroundWatcher.Current);
        };
        Unloaded += (_, _) => Modules.Foreground.Changed -= OnForeground;
    }

    void OnForeground(nint hwnd) => Show(hwnd);

    void Show(nint hwnd)
    {
        if (hwnd == 0)
            return;
        var details = WindowTools.Describe(hwnd);
        if (details.ProcessId == Environment.ProcessId)
            return; // our own menus and dialogs: keep showing the app the user was in
        target = details;
        text.Text = ModuleStates.ActiveAppName(details.ClassName, details.ExePath, details.Description, details.Title, desktopName, names);
#if DEBUG
        Log.Write($"active-app: \"{text.Text}\" ({details.ClassName}, {details.ExePath})");
#endif
    }

    protected override void OnClick()
    {
        bool isApp = target is not null && text.Text != desktopName;
        var menu = new ContextMenu();
        menu.Items.Add(Modules.Item("Close Window", () => WindowTools.Close(target!.Handle), isApp));
        menu.Items.Add(Modules.Item("Minimize", () => WindowTools.Minimize(target!.Handle), isApp));
        menu.Items.Add(new Separator());
        menu.Items.Add(Modules.Item($"Quit {text.Text}", () => WindowTools.Quit(target!.ProcessId), isApp));
        Modules.Open(menu, this, ctx.Edge);
    }
}

/// <summary>Master volume: icon (and percent if <c>showPercent</c>). Click mutes, mouse wheel changes the level.</summary>
sealed class VolumeModule : ClickableModule
{
    static readonly Dictionary<string, string> Glyphs = new()
    {
        ["muted"] = "", ["off"] = "", ["low"] = "", ["medium"] = "", ["high"] = "",
    };

    readonly StateIcon icon;
    readonly TextBlock percent = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };

    public VolumeModule(ModuleSpec spec)
    {
        icon = new StateIcon(spec.StateIcons);
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(icon);
        if (Modules.Flag(spec, "showPercent", false))
            panel.Children.Add(percent);
        Child = panel;
        MouseWheel += (_, e) =>
        {
            if (Modules.Audio is { } audio)
                audio.SetLevel(audio.Level + 0.02f * e.Delta / 120);
        };
        Loaded += (_, _) =>
        {
            if (Modules.Audio is { } audio)
                audio.Changed += OnChanged;
            Update();
        };
        Unloaded += (_, _) =>
        {
            if (Modules.Audio is { } audio)
                audio.Changed -= OnChanged;
        };
    }

    protected override void OnClick() => Modules.Audio?.ToggleMute();

    void OnChanged() => Dispatcher.BeginInvoke(Update);

    void Update()
    {
        var audio = Modules.Audio;
        float level = audio?.Level ?? 0;
        string state = audio is { Available: true } ? ModuleStates.Volume(level, audio.Muted) : "off";
        icon.Show(state, Glyphs[state]);
        percent.Text = $"{Math.Round(level * 100)}%";
        ToolTip = $"Volume {Math.Round(level * 100)}%{(audio?.Muted == true ? " (muted)" : "")}";
    }
}

/// <summary>Connection state from .NET network events (no polling). Click opens the network settings.</summary>
sealed class NetworkModule : ClickableModule
{
    static readonly Dictionary<string, string> Glyphs = new() { ["wifi"] = "", ["ethernet"] = "", ["offline"] = "" };
    readonly StateIcon icon;

    public NetworkModule(ModuleSpec spec)
    {
        icon = new StateIcon(spec.StateIcons);
        Child = icon;
        Loaded += (_, _) =>
        {
            NetworkChange.NetworkAddressChanged += OnChanged;
            NetworkChange.NetworkAvailabilityChanged += OnChanged;
            Update();
        };
        Unloaded += (_, _) =>
        {
            // Static events: unsubscribe or the module leaks after a theme reload.
            NetworkChange.NetworkAddressChanged -= OnChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnChanged;
        };
    }

    protected override void OnClick() => SystemActions.Open("ms-settings:network-status");

    void OnChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Update);

    void Update()
    {
        string state = ModuleStates.Network(NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => !IsVirtual(n))
            .Select(n => new ModuleStates.Adapter(
                n.OperationalStatus == OperationalStatus.Up,
                n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211,
                n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel,
                HasGateway(n))));
        icon.Show(state, Glyphs[state]);
        ToolTip = state switch { "wifi" => "Wi-Fi", "ethernet" => "Ethernet", _ => "Not connected" };
    }

    static bool HasGateway(NetworkInterface n)
    {
        try
        {
            return n.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any));
        }
        catch (NetworkInformationException)
        {
            return false;
        }
    }

    // shortcut: virtual adapters are recognized by name (Hyper-V, VPN, VM software); refine if a real adapter is misread.
    static bool IsVirtual(NetworkInterface n) =>
        new[] { "virtual", "hyper-v", "vethernet", "vmware", "virtualbox", "wsl" }.Any(w =>
            n.Description.Contains(w, StringComparison.OrdinalIgnoreCase) || n.Name.Contains(w, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Battery level and charging state; hidden on PCs without a battery. Updates on power status events.</summary>
sealed class BatteryModule : ClickableModule
{
    readonly StateIcon icon;
    readonly TextBlock percent = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };

    public BatteryModule(ModuleSpec spec)
    {
        icon = new StateIcon(spec.StateIcons);
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(icon);
        if (Modules.Flag(spec, "showPercent", true))
            panel.Children.Add(percent);
        Child = panel;
        Loaded += (_, _) =>
        {
            SystemEvents.PowerModeChanged += OnChanged;
            Update();
        };
        Unloaded += (_, _) => SystemEvents.PowerModeChanged -= OnChanged;
    }

    protected override void OnClick() => SystemActions.Open("ms-settings:batterysaver");

    void OnChanged(object? sender, PowerModeChangedEventArgs e) => Dispatcher.BeginInvoke(Update);

    void Update()
    {
        var battery = Power.Battery();
        Visibility = battery.Present ? Visibility.Visible : Visibility.Collapsed;
        if (!battery.Present)
            return;
        string state = ModuleStates.Battery(battery.Percent, battery.Charging);
        // Segoe Fluent Icons: Battery0..Battery9 are E850..E859, Battery10 is E83F, BatteryCharging is E83E (approximate levels).
        string glyph = battery.Charging ? "" : battery.Percent >= 95 ? "" : ((char)(0xE850 + battery.Percent / 10)).ToString();
        icon.Show(state, glyph);
        percent.Text = $"{battery.Percent}%";
        ToolTip = $"Battery {battery.Percent}%{(battery.Charging ? " (charging)" : "")}";
    }
}
