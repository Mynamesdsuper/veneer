using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Veneer.Core;
using Veneer.Core.Dock;
using Veneer.Core.Layout;
using Veneer.Core.Themes;
using Veneer.Interop;

namespace Veneer.App;

/// <summary>
/// The dock, as two windows: this one is the blurred plate (it also reserves the screen strip), and a transparent
/// <see cref="DockIconsWindow"/> on top draws the icons. Magnified icons grow out of the plate, and the empty parts
/// of the icons window let clicks through to whatever is behind.
/// </summary>
sealed class DockWindow : Window
{
    readonly Theme theme;
    readonly DockSurface dock;
    readonly PixelRect area;
    readonly uint dpi;
    readonly AppWindowTracker tracker;
    readonly string? overridesPath;
    readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(150) };
    readonly DockIconsWindow icons;
    EdgeTrigger? trigger;
    AppBar? appBar;
    nint hwnd;
    string signature = "";
    bool revealed = true;
    bool fullScreen;
    bool closed;
    PixelRect basePlate; // where Place() put the plate; it stretches from here while icons are magnified

    public DockWindow(Theme theme, DockSurface dock, PixelRect area, uint dpi, AppWindowTracker tracker, string? overridesPath)
    {
        this.theme = theme;
        this.dock = dock;
        this.area = area;
        this.dpi = dpi;
        this.tracker = tracker;
        this.overridesPath = overridesPath;
        Pinned = dock.DefaultPinned.ToList();
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

        Content = new System.Windows.Controls.Border
        {
            Background = ThemeVisuals.Brush(dock.Style.Background),
            BorderBrush = ThemeVisuals.BorderBrush(dock.Style.Border, dock.Style.BorderHighlight),
            BorderThickness = new Thickness(dock.Style.Border is null && dock.Style.BorderHighlight is null ? 0 : 1),
            CornerRadius = new CornerRadius(dock.CornerRadius),
        };
        icons = new DockIconsWindow(this);

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
            appBar.FullScreenChanged += full => FullScreenChanged?.Invoke(full);
            // An owned window always stays above its owner: the icons can never fall behind the plate, even when
            // the shell moves the plate (which puts it back on top of the topmost band).
            icons.Owner = this;
        };
        tracker.Changed += OnWindowsChanged;
        Closed += (_, _) =>
        {
            // A rebuild closes this window: nothing queued may touch it afterwards (a pending debounce tick would
            // call Show on a closed window, which throws).
            closed = true;
            debounce.Stop();
            tracker.Changed -= OnWindowsChanged;
            icons.Close();
            trigger?.Close();
        };
    }

    public Theme Theme => theme;

    public DockSurface Dock => dock;

    public uint Dpi => dpi;

    /// <summary>The pinned list in use: the theme's, with the user's changes from the overrides file.</summary>
    public List<string> Pinned { get; private set; }

    public List<DockItem> Items { get; private set; } = [];

    public event Action<bool>? FullScreenChanged;

    int CornerRadiusPx => SurfaceLayout.ToPhysical(dock.CornerRadius, dpi);

    int Thickness => SurfaceLayout.ToPhysical(dock.IconSize + 2 * dock.Padding, dpi);

    /// <summary>Length of a separator cell along the dock, in DIPs.</summary>
    public double SeparatorLength => 2 * dock.Style.Spacing + 1;

    /// <summary>Base (unmagnified) cell sizes along the dock, in DIPs.</summary>
    public List<(double Size, bool Scales)> Cells(IEnumerable<DockItem> items) =>
        items.Select(i => i.IsSeparator ? (SeparatorLength, false) : ((double)dock.IconSize, true)).ToList();

    void OnWindowsChanged() => debounce.Start(); // restarts the countdown on every event of a burst

    /// <summary>Builds the items for the current windows and places the dock. Call once after Show().</summary>
    public void Refresh(bool force = false)
    {
        if (closed)
            return;
        var list = DockModel.Build(Pinned, Running(), dock.SeparateRunning);
        string sig = string.Join('|', list.Select(i => $"{i.Key}:{i.IsRunning}"));
        Items = list;
        if (sig == signature && !force)
            return; // same icons; the items now carry the new window handles
        signature = sig;
#if DEBUG
        Log.Write($"dock: {sig}");
#endif
        Place();
        icons.Rebuild();
    }

    /// <summary>Debug self-test "demo": ignore running apps, so public screenshots show only the theme's pinned apps.</summary>
    public static bool Demo { get; set; }

    // Running apps grouped by AUMID (or exe), in the order their first window appears; windows most recent first.
    List<RunningApp> Running()
    {
        if (Demo)
            return [];
        var groups = new List<(string Key, string? Aumid, string? Exe, List<nint> Windows)>();
        foreach (var w in tracker.Enumerate())
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
        var cells = Cells(Items);
        double row = cells.Sum(c => c.Size) + Math.Max(0, cells.Count - 1) * dock.Style.Spacing;
        int length = SurfaceLayout.ToPhysical(Math.Max(row, dock.IconSize) + 2 * dock.Padding, dpi);
        int margin = SurfaceLayout.ToPhysical(dock.Margin, dpi);
        int strip = Thickness + margin;
        var desired = dock.Edge switch
        {
            Edge.Left => area with { Right = area.Left + strip },
            Edge.Right => area with { Left = area.Right - strip },
            _ => area with { Top = area.Bottom - strip },
        };
        // An auto-hidden dock never reserves space: it covers windows only while shown.
        appBar.Place(dock.Edge, desired, dock.ReserveSpace && !dock.AutoHide, granted => DockModel.Place(granted, dock.Edge, length, Thickness, margin));
        SurfaceWindow.UpdateRegion(hwnd, appBar.Bounds, CornerRadiusPx);
        basePlate = appBar.Bounds;
        icons.Place(appBar.Bounds, area);
        if (dock.AutoHide)
        {
            trigger ??= new EdgeTrigger(this);
            trigger.Place(appBar.Bounds, area, dock.Edge);
        }

        ApplyVisibility();
    }

    /// <summary>
    /// Stretches the plate along the dock by <paramref name="before"/> and <paramref name="after"/> DIPs so that it
    /// keeps holding the magnified icons (macOS does the same). Called on every magnification frame.
    /// </summary>
    public void StretchPlate(double before, double after)
    {
        if (appBar is null || closed || basePlate.Width == 0)
            return;
        int b = SurfaceLayout.ToPhysical(before, dpi), a = SurfaceLayout.ToPhysical(after, dpi);
        var rect = dock.Edge is Edge.Left or Edge.Right
            ? basePlate with { Top = basePlate.Top - b, Bottom = basePlate.Bottom + a }
            : basePlate with { Left = basePlate.Left - b, Right = basePlate.Right + a };
        if (rect == appBar.Bounds)
            return;
        appBar.MoveWindow(rect);
        SurfaceWindow.UpdateRegion(hwnd, rect, CornerRadiusPx);
    }

    /// <summary>Plate and icons are shown unless a full-screen app is in front or the auto-hidden dock is tucked away.</summary>
    public bool ShowsSurfaces => !closed && !fullScreen && (revealed || !dock.AutoHide);

    // The edge strip exists only while the dock is tucked away, so it never covers the dock's own bottom pixels.
    bool ShowsTrigger => !closed && !fullScreen && dock.AutoHide && !revealed;

    void ApplyVisibility()
    {
        if (closed)
            return;
        if (ShowsSurfaces)
        {
            if (!IsVisible)
                Show();
            icons.Show();
        }
        else
        {
            Hide();
            icons.Hide(); // an owned window does not hide with its owner
        }

        if (trigger is not null)
            trigger.Visibility = ShowsTrigger ? Visibility.Visible : Visibility.Hidden;
    }

    /// <summary>Auto-hide: shows or hides the plate and the icons.</summary>
    public void SetRevealed(bool show)
    {
        if (show == revealed || !dock.AutoHide || closed)
            return;
        revealed = show;
        ApplyVisibility();
        if (show)
        {
            Place();
            icons.StartConcealCountdown(); // hides again if the mouse leaves without ever reaching the icons
        }
    }

    /// <summary>A full-screen app (game, video) is in front: hide plate, icons and edge strip together.</summary>
    public void SetFullScreen(bool value)
    {
        fullScreen = value;
        ApplyVisibility();
    }

    /// <summary>Called once the dock is shown: an auto-hidden dock starts hidden.</summary>
    public void Start()
    {
        Refresh(force: true);
        if (dock.AutoHide)
        {
            revealed = false;
            ApplyVisibility();
        }
    }

    /// <summary>
    /// The theme reloaded and only the pinned list changed (usually our own write to the overrides file): update in
    /// place, without rebuilding every surface.
    /// </summary>
    public void UpdatePinned(IReadOnlyList<string> pinned)
    {
        if (closed || pinned.SequenceEqual(Pinned))
            return;
        Pinned = pinned.ToList();
        Refresh(force: true);
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

    // ----- actions on items -----

    public void Activate(DockItem item)
    {
        if (item.Running is { Windows.Count: > 0 } app)
        {
            // The window used most recently, looked up now (foreground changes do not trigger a refresh).
            Log.Write($"dock: activate {item.Key}");
            WindowTools.Activate(app.Windows.MaxBy(tracker.LastActive));
        }
        else if (item.Pinned is { } entry && !item.IsSeparator)
        {
            Log.Write($"dock: launch {entry}");
            SystemActions.Launch(entry, DockModel.IsAumid(entry));
            if (dock.BounceOnLaunch && Items.IndexOf(item) is >= 0 and var index)
                icons.Bounce(index);
        }
    }

    public void CloseWindows(DockItem item)
    {
        foreach (nint w in item.Running?.Windows ?? [])
            WindowTools.Close(w);
    }

    public void KeepInDock(DockItem item)
    {
        if (item.Running is { } app && DockModel.EntryFor(app) is { } entry)
            SavePinned(DockModel.Pin(Pinned, entry));
    }

    public void RemoveFromDock(DockItem item)
    {
        if (item.Pinned is { } entry)
            SavePinned(DockModel.Unpin(Pinned, entry));
    }

    public void MovePinned(int from, int to) => SavePinned(DockModel.Move(Pinned, from, to));

    /// <summary>
    /// Shows the new list at once and writes it to the user's overrides, so it survives theme switches. The theme
    /// then reloads from the overrides file by itself.
    /// </summary>
    void SavePinned(List<string> list)
    {
        if (closed)
            return; // a menu or drag that outlived a rebuild must not write a stale list
        var previous = Pinned;
        Pinned = list;
        Refresh(force: true);
        if (overridesPath is null)
        {
            Log.Write("dock: pinned apps not saved (no theme folder in use)");
            return;
        }

        var json = new JsonArray(list.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray());
        if (!OverridesFile.TrySetSurfaceValue(overridesPath, dock.Id, "defaultPinned", json, out string? error))
        {
            // Show what is really saved rather than a change that would vanish at the next reload.
            Log.Write($"dock: pinned apps not saved, change undone: {error}");
            Pinned = previous;
            Refresh(force: true);
        }
    }

    // ----- debug self-test hooks -----

    int Find(string text) => Items.FindIndex(i => i.Key.Contains(text, StringComparison.OrdinalIgnoreCase));

    public void ClickItem(string text)
    {
        if (Find(text) is >= 0 and var i)
            Activate(Items[i]);
    }

    public void CloseItem(string text)
    {
        if (Find(text) is >= 0 and var i)
            CloseWindows(Items[i]);
    }

    public void PinItem(string text)
    {
        if (Find(text) is >= 0 and var i)
            KeepInDock(Items[i]);
    }

    public void UnpinItem(string text)
    {
        if (Find(text) is >= 0 and var i)
            RemoveFromDock(Items[i]);
    }

    public void OpenMenu(string text)
    {
        if (Find(text) is >= 0 and var i)
            icons.OpenMenu(i);
    }

    public void Sweep() => icons.Sweep();

    public void Hover(double? fraction) => icons.Hover(fraction);

    public bool Revealed => revealed;
}

/// <summary>Auto-hide: a thin, almost invisible strip on the screen edge that brings the dock back on hover.</summary>
sealed class EdgeTrigger : Window
{
    nint hwnd;

    public EdgeTrigger(DockWindow dock)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)); // alpha 1: invisible but receives the mouse
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            SurfaceWindow.MakeOverlay(hwnd);
        };
        MouseEnter += (_, _) => dock.SetRevealed(true);
        Show();
    }

    /// <summary>A 2 px strip on the screen edge, as long as the dock.</summary>
    public void Place(PixelRect plate, PixelRect area, Edge edge)
    {
        const int Px = 2;
        var r = edge switch
        {
            Edge.Left => new PixelRect(area.Left, plate.Top, area.Left + Px, plate.Bottom),
            Edge.Right => new PixelRect(area.Right - Px, plate.Top, area.Right, plate.Bottom),
            _ => new PixelRect(plate.Left, area.Bottom - Px, plate.Right, area.Bottom),
        };
        SurfaceWindow.PlaceTopmost(hwnd, r);
    }
}
