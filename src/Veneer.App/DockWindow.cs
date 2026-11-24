using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using Veneer.Core;
using Veneer.Core.Dock;
using Veneer.Core.Layout;
using Veneer.Core.Themes;
using Veneer.Interop;

namespace Veneer.App;

/// <summary>
/// The dock: pinned and running apps on a blurred plate, centered on its edge. The item list is rebuilt
/// (debounced) when app windows change; the window resizes to fit and keeps its reserved strip.
/// </summary>
sealed class DockWindow : Window
{
    readonly Theme theme;
    readonly DockSurface dock;
    readonly PixelRect area;
    readonly uint dpi;
    readonly AppWindowTracker tracker;
    readonly StackPanel items;
    readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(150) };
    AppBar? appBar;
    nint hwnd;
    string signature = "";
    List<DockItem> current = [];

    public DockWindow(Theme theme, DockSurface dock, PixelRect area, uint dpi, AppWindowTracker tracker)
    {
        this.theme = theme;
        this.dock = dock;
        this.area = area;
        this.dpi = dpi;
        this.tracker = tracker;
        Title = $"Veneer {dock.Id}";
        WindowStyle = WindowStyle.SingleBorderWindow; // WS_CAPTION for the blurred backdrop, see SurfaceWindow
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Transparent;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            GlassFrameThickness = new Thickness(-1),
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(0),
            UseAeroCaptionButtons = false,
        });

        bool horizontal = dock.Edge == Edge.Bottom;
        items = new StackPanel { Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical };
        var plate = new Border
        {
            Background = ThemeVisuals.Brush(dock.Style.Background),
            BorderBrush = ThemeVisuals.Brush(dock.Style.Border),
            BorderThickness = new Thickness(dock.Style.Border is null ? 0 : 1),
            CornerRadius = new CornerRadius(dock.CornerRadius),
            // No padding on the screen-edge side: each icon cell includes that strip and draws its indicator there.
            Padding = dock.Edge switch
            {
                Edge.Left => new Thickness(0, dock.Padding, dock.Padding, dock.Padding),
                Edge.Right => new Thickness(dock.Padding, dock.Padding, 0, dock.Padding),
                _ => new Thickness(dock.Padding, dock.Padding, dock.Padding, 0),
            },
            Child = items,
        };
        Content = plate;

        debounce.Tick += (_, _) =>
        {
            debounce.Stop();
            Refresh();
        };
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(hwnd);
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            source.AddHook(Hook);
            SurfaceWindow.Apply(hwnd, dock.Style.Blur, CornerRadiusPx);
            appBar = new AppBar(hwnd);
        };
        tracker.Changed += OnWindowsChanged;
        Closed += (_, _) => tracker.Changed -= OnWindowsChanged;
    }

    int CornerRadiusPx => SurfaceLayout.ToPhysical(dock.CornerRadius, dpi);

    int Thickness => SurfaceLayout.ToPhysical(dock.IconSize + 2 * dock.Padding, dpi);

    void OnWindowsChanged() => debounce.Start(); // restarts the countdown on every event of a burst

    /// <summary>Builds the items for the current windows and places the dock. Call once after Show().</summary>
    public void Refresh()
    {
        var running = Running();
        var list = DockModel.Build(dock.DefaultPinned, running);
        string sig = string.Join('|', list.Select(i => $"{i.Key}:{i.IsRunning}"));
        current = list;
        if (sig == signature)
            return; // same icons, only window handles changed
        signature = sig;
#if DEBUG
        Log.Write($"dock: {sig}");
#endif
        items.Children.Clear();
        foreach (var item in list)
            items.Children.Add(new DockIcon(this, item));
        Place();
    }

    // Running apps grouped by AUMID (or exe), in the order their first window appears; windows most recent first.
    List<RunningApp> Running()
    {
        var groups = new List<(string Key, string? Aumid, string? Exe, List<nint> Windows)>();
        foreach (var w in AppWindowTracker.Enumerate())
        {
            string key = DockModel.KeyFor(w.Aumid, w.ExePath);
            var group = groups.FirstOrDefault(g => g.Key == key);
            if (group.Key is null)
                groups.Add(group = (key, w.Aumid, w.ExePath, []));
            group.Windows.Add(w.Handle);
        }

        return groups
            .Select(g => new RunningApp(g.Key, g.Aumid, g.Exe, g.Windows.OrderByDescending(tracker.LastActive).ToList()))
            .ToList();
    }

    void Place()
    {
        if (appBar is null)
            return;
        int count = items.Children.Count;
        double lengthDips = count * dock.IconSize + Math.Max(0, count - 1) * dock.Style.Spacing + 2 * dock.Padding;
        int length = SurfaceLayout.ToPhysical(Math.Max(lengthDips, dock.IconSize + 2 * dock.Padding), dpi);
        int margin = SurfaceLayout.ToPhysical(dock.Margin, dpi);
        int strip = Thickness + margin;
        var desired = dock.Edge switch
        {
            Edge.Left => area with { Right = area.Left + strip },
            Edge.Right => area with { Left = area.Right - strip },
            _ => area with { Top = area.Bottom - strip },
        };
        appBar.Place(dock.Edge, desired, dock.ReserveSpace, granted => DockModel.Place(granted, dock.Edge, length, Thickness, margin));
        SurfaceWindow.UpdateRegion(hwnd, appBar.Bounds, CornerRadiusPx);
    }

    public void RemoveAppBar() => appBar?.Remove();

    public void OnShellRestarted() => appBar?.OnShellRestarted();

    nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (SurfaceWindow.WndProc(hwnd, msg, wParam, lParam, out nint result))
        {
            handled = true;
            return result;
        }

        if (appBar?.WndProc(msg, wParam, lParam) == true)
            handled = true;
        return 0;
    }

    /// <summary>Debug self-test: the first icon whose key contains <paramref name="text"/>.</summary>
    DockIcon? Find(string text) =>
        items.Children.OfType<DockIcon>().FirstOrDefault(i => i.Item.Key.Contains(text, StringComparison.OrdinalIgnoreCase));

    public void ClickItem(string text) => Find(text)?.Click();

    /// <summary>Debug self-test: closes the windows of the app behind an icon (WM_CLOSE, one window at a time).</summary>
    public void CloseItem(string text)
    {
        foreach (nint w in Find(text)?.Item.Running?.Windows ?? [])
            WindowTools.Close(w);
    }

    /// <summary>One app in the dock: icon plus running indicator. Click activates or launches.</summary>
    sealed class DockIcon : Grid
    {
        readonly DockWindow owner;
        readonly DockItem item;

        public DockItem Item => item;

        public DockIcon(DockWindow owner, DockItem item)
        {
            this.owner = owner;
            this.item = item;
            var dock = owner.dock;
            bool bottom = dock.Edge == Edge.Bottom;
            Width = dock.IconSize + (bottom ? 0 : dock.Padding);
            Height = dock.IconSize + (bottom ? dock.Padding : 0);
            Background = Brushes.Transparent;
            if (owner.items.Children.Count > 0)
            {
                Margin = dock.Edge == Edge.Bottom ? new Thickness(dock.Style.Spacing, 0, 0, 0) : new Thickness(0, dock.Style.Spacing, 0, 0);
            }

            Children.Add(new Image
            {
                Source = Icons.For(owner.theme, item, owner.dpi),
                Stretch = Stretch.Uniform,
                Width = dock.IconSize,
                Height = dock.IconSize,
                HorizontalAlignment = dock.Edge switch { Edge.Left => HorizontalAlignment.Right, Edge.Right => HorizontalAlignment.Left, _ => HorizontalAlignment.Center },
                VerticalAlignment = bottom ? VerticalAlignment.Top : VerticalAlignment.Center,
            });
            // 256 px icons shown at 48: the default bilinear filter makes jagged edges.
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            if (item.IsRunning)
                Children.Add(Indicator(dock));
            ToolTip = Icons.Name(item);
            MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                Click();
            };
        }

        // Centered in the cell's strip between the icon and the screen edge.
        static FrameworkElement Indicator(DockSurface dock)
        {
            var ind = dock.RunningIndicator;
            var brush = ThemeVisuals.Brush(ind.Color ?? dock.Style.Foreground) ?? Brushes.Black;
            FrameworkElement shape = ind.Shape == IndicatorShape.Line
                ? new Rectangle { Fill = brush, Width = dock.Edge == Edge.Bottom ? dock.IconSize * 0.4 : ind.Size, Height = dock.Edge == Edge.Bottom ? ind.Size : dock.IconSize * 0.4, RadiusX = ind.Size / 2, RadiusY = ind.Size / 2 }
                : new Ellipse { Fill = brush, Width = ind.Size, Height = ind.Size };
            double gap = Math.Max(0, (dock.Padding - (dock.Edge == Edge.Bottom ? shape.Height : shape.Width)) / 2);
            (shape.HorizontalAlignment, shape.VerticalAlignment, shape.Margin) = dock.Edge switch
            {
                Edge.Left => (HorizontalAlignment.Left, VerticalAlignment.Center, new Thickness(gap, 0, 0, 0)),
                Edge.Right => (HorizontalAlignment.Right, VerticalAlignment.Center, new Thickness(0, 0, gap, 0)),
                _ => (HorizontalAlignment.Center, VerticalAlignment.Bottom, new Thickness(0, 0, 0, gap)),
            };
            shape.IsHitTestVisible = false;
            return shape;
        }

        public void Click()
        {
            if (item.Running is { Windows.Count: > 0 } app)
            {
                Log.Write($"dock: activate {item.Key}");
                WindowTools.Activate(app.Windows[0]);
            }
            else if (item.Pinned is { } entry)
            {
                Log.Write($"dock: launch {entry}");
                SystemActions.Launch(entry, DockModel.IsAumid(entry));
            }
        }
    }
}

/// <summary>Dock icons: theme images first (iconOverrides, then iconFolder), then the shell's icon. Cached per run.</summary>
static class Icons
{
    static readonly Dictionary<string, ImageSource?> Cache = [];
    static readonly Dictionary<string, string> Names = [];

    public static ImageSource? For(Theme theme, DockItem item, uint dpi)
    {
        string key = $"{theme.Id}|{item.Key}";
        if (Cache.TryGetValue(key, out var cached))
            return cached;
        var image = FromTheme(theme, item)
            ?? (Aumid(item) is { } aumid && ShellItems.PackagedIcon(aumid) is { } packaged ? Load(packaged) : null)
            ?? FromShell(item, SurfaceLayout.ToPhysical(256, Math.Max(dpi, 96u)));
        Cache[key] = image;
        return image;
    }

    static string? Exe(DockItem item) =>
        item.Running?.ExePath is { } path ? System.IO.Path.GetFileName(path)
        : item.Pinned is { } p && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? System.IO.Path.GetFileName(p)
        : null;

    static string? Aumid(DockItem item) =>
        item.Running?.Aumid ?? (item.Pinned is { } p ? (DockModel.IsAumid(p) ? p : DockModel.AumidForUri(p)) : null);

    static ImageSource? FromTheme(Theme theme, DockItem item)
    {
        string? exe = Exe(item)?.ToLowerInvariant();
        string? aumid = Aumid(item);
        string? path = null;
        if (aumid is not null)
            path = theme.IconOverrides.FirstOrDefault(kv => string.Equals(kv.Key, "aumid:" + aumid, StringComparison.OrdinalIgnoreCase)).Value;
        if (path is null && exe is not null)
            path = theme.IconOverrides.FirstOrDefault(kv => string.Equals(kv.Key, "exe:" + exe, StringComparison.OrdinalIgnoreCase)).Value;
        if (path is null && exe is not null && theme.IconFolder is { } folder)
        {
            string candidate = System.IO.Path.Combine(folder, System.IO.Path.GetFileNameWithoutExtension(exe) + ".png");
            // The folder was checked when the theme loaded; still refuse links that appeared since.
            if (System.IO.File.Exists(candidate) && !AssetPaths.IsLink(candidate))
                path = candidate;
        }

        return path is null ? null : Load(path);
    }

    static ImageSource? Load(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad; // do not keep the theme file open
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception e) when (e is System.IO.IOException or NotSupportedException or System.IO.FileFormatException)
        {
            Log.Write($"dock icon {path}: {e.Message}");
            return null;
        }
    }

    static string? ParsingName(DockItem item) =>
        Aumid(item) is { } aumid ? ShellItems.ParsingName(aumid, isAumid: true)
        : item.Running?.ExePath ?? (item.Pinned is { } p ? ShellItems.ParsingName(p, isAumid: false) : null);

    static ImageSource? FromShell(DockItem item, int size)
    {
        // Some apps set an AppUserModelID that is not in the Apps folder (Steam): fall back to the exe.
        var icon = (ParsingName(item) is { } name ? ShellItems.Icon(name, size) : null)
            ?? (item.Running?.ExePath is { } exe ? ShellItems.Icon(exe, size) : null);
        if (icon is null)
            return null;
        var (pixels, width, height) = icon.Value;
        var format = IsPremultiplied(pixels) ? PixelFormats.Pbgra32 : PixelFormats.Bgra32;
        var bitmap = BitmapSource.Create(width, height, 96, 96, format, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    // The shell returns some icons premultiplied and some with straight alpha. In premultiplied data no color
    // channel can exceed its alpha; one that does means straight alpha. Guessing wrong gives light fringes.
    static bool IsPremultiplied(byte[] bgra)
    {
        for (int i = 0; i < bgra.Length; i += 4)
        {
            byte a = bgra[i + 3];
            if (bgra[i] > a || bgra[i + 1] > a || bgra[i + 2] > a)
                return false;
        }

        return true;
    }

    public static string Name(DockItem item)
    {
        if (Names.TryGetValue(item.Key, out string? name))
            return name;
        name = (ParsingName(item) is { } parsing ? ShellItems.DisplayName(parsing) : null)
            ?? (item.Running?.ExePath is { } exe ? System.IO.Path.GetFileNameWithoutExtension(exe) : item.Pinned) ?? "";
        Names[item.Key] = name;
        return name;
    }
}
