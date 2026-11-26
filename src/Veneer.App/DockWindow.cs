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
    readonly AppList apps;
    readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(150) };
    readonly DockIconsWindow icons;
    readonly DispatcherTimer trashCheck = new() { Interval = TimeSpan.FromMilliseconds(300) };
    uint[] trashWatch = [];
    EdgeTrigger? trigger;
    AppBar? appBar;
    GlassPlate? plate;
    nint hwnd;
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
        var extras = new List<string>();
        if (dock.Downloads)
            extras.Add(DockModel.Downloads);
        if (dock.Trash)
            extras.Add(DockModel.Trash);
        apps = new AppList(dock.Id, dock.DefaultPinned, tracker, overridesPath, dock.SeparateRunning, () => Refresh(force: true), extras);
        trashCheck.Tick += (_, _) =>
        {
            trashCheck.Stop();
            CheckTrash();
        };
        Title = $"Veneer {dock.Id}";
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Transparent;
        if (Layered)
        {
            // Per-pixel transparent, so the corners outside the rounded plate stay empty.
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow; // WS_CAPTION for the blurred backdrop, see SurfaceWindow
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                GlassFrameThickness = new Thickness(-1),
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(0),
                UseAeroCaptionButtons = false,
            });
        }

        // With a GlassPlate the plate is drawn by composition; this almost invisible shape only keeps the plate
        // clickable (a per-pixel transparent window lets clicks through where it is fully transparent).
        Content = Layered && dock.Style.Blur ? HitShape() : Plate();
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
            if (Layered)
                SurfaceWindow.MakeOverlay(hwnd);
            else
                SurfaceWindow.Apply(hwnd, dock.Style.Blur, CornerRadiusPx);

            if (Layered && dock.Style.Blur)
            {
                try
                {
                    var style = dock.Style;
                    plate = new GlassPlate(hwnd, CornerRadiusPx, style.Background, style.Border, style.BorderHighlight, SurfaceLayout.ToPhysical(1, dpi));
                }
                catch (Exception e)
                {
                    // Keep a working dock: the WPF plate over a plain blur, with square blurred corners.
                    Log.Write($"dock: rounded glass not available, using the plain blur: {e.Message}");
                    Content = Plate();
                    SurfaceWindow.ApplyPopupBlur(hwnd, 0, round: false, acrylic: false);
                }
            }

            if (dock.Trash)
            {
                trashWatch = RecycleBin.Watch(hwnd);
                CheckTrash();
            }

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
            apps.Closed = true;
            debounce.Stop();
            trashCheck.Stop();
            RecycleBin.Unwatch(trashWatch);
            tracker.Changed -= OnWindowsChanged;
            icons.Close();
            trigger?.Close();
        };
    }

    public Theme Theme => theme;

    public DockSurface Dock => dock;

    public uint Dpi => dpi;

    /// <summary>The pinned list in use: the theme's, with the user's changes from the overrides file.</summary>
    public List<string> Pinned => apps.Pinned;

    public List<DockItem> Items => apps.Items;

    public event Action<bool>? FullScreenChanged;

    int CornerRadiusPx => SurfaceLayout.ToPhysical(dock.CornerRadius, dpi);

    // Above the radius DWM can round, DWM's backdrop and frame would fill the square corners: the window is per-pixel
    // transparent instead, and the plate is drawn by WPF, or by a GlassPlate when it is blurred.
    bool Layered => CornerRadiusPx > SurfaceWindow.MaxNativeCornerRadius;

    System.Windows.Controls.Border Plate() => new()
    {
        Background = ThemeVisuals.Brush(dock.Style.Background),
        BorderBrush = ThemeVisuals.BorderBrush(dock.Style.Border, dock.Style.BorderHighlight),
        BorderThickness = new Thickness(dock.Style.Border is null && dock.Style.BorderHighlight is null ? 0 : 1),
        CornerRadius = new CornerRadius(dock.CornerRadius),
    };

    System.Windows.Controls.Border HitShape() => new()
    {
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
        CornerRadius = new CornerRadius(dock.CornerRadius),
    };

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
        if (!apps.Update(force))
            return; // same icons; the items now carry the new window handles
        Place();
        icons.Rebuild();
    }

    /// <summary>Debug self-test "demo": ignore running apps, so public screenshots show only the theme's pinned apps.</summary>
    public static bool Demo { get; set; }

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
        plate?.Resize(appBar.Bounds.Width, appBar.Bounds.Height);
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
        plate?.Resize(rect.Width, rect.Height);
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
        if (!closed && apps.SetPinned(pinned))
            Refresh(force: true);
    }

    public void RemoveAppBar() => appBar?.Remove();

    public void OnShellRestarted()
    {
        appBar?.OnShellRestarted();
        if (!dock.Trash || closed)
            return;
        // The notifications may have gone with the old Explorer: register again and ask once.
        RecycleBin.Unwatch(trashWatch);
        trashWatch = RecycleBin.Watch(hwnd);
        CheckTrash();
    }

    nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (SurfaceWindow.WndProc(hwnd, msg, wParam, lParam, out nint result))
        {
            handled = true;
            return result;
        }

        if (msg == RecycleBin.ChangedMessage)
        {
            trashCheck.Stop(); // emptying a full bin sends a burst: ask once it is over
            trashCheck.Start();
            handled = true;
            return 0;
        }

        if (appBar?.WndProc(msg, wParam, lParam) == true)
            handled = true;
        return 0;
    }

    // ----- Recycle Bin -----

    /// <summary>The Recycle Bin holds files: its dock item shows the full icon.</summary>
    public bool TrashFull { get; private set; }

    // The query asks every drive, so it runs off the UI thread; the icon changes when the answer arrives.
    void CheckTrash() => Task.Run(() =>
    {
        bool full = !RecycleBin.IsEmpty();
        Dispatcher.InvokeAsync(() =>
        {
            if (closed || full == TrashFull)
                return;
            Log.Write($"{dock.Id}: recycle bin {(full ? "full" : "empty")}");
            TrashFull = full;
            icons.Rebuild();
        });
    });

    /// <summary>Empties the Recycle Bin after Windows' own confirmation, which may take a while: off the UI thread.</summary>
    public static void EmptyTrash() => RunShellOperation(RecycleBin.Empty);

    /// <summary>Files dropped on the Recycle Bin item go to the bin through Windows, with its progress and undo.</summary>
    public static void MoveToTrash(IReadOnlyList<string> paths) => RunShellOperation(() => RecycleBin.Delete(paths));

    // Shell file operations show their own windows and want a single-threaded apartment.
    static void RunShellOperation(Action action)
    {
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Log.Write($"recycle bin: {e.Message}");
            }
        }) { IsBackground = true, Name = "Veneer shell operation" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    // ----- actions on items -----

    public void Activate(DockItem item)
    {
        switch (item.Extra)
        {
            case DockModel.Trash:
                RecycleBin.Open();
                return;
            case DockModel.Downloads:
                if (Items.IndexOf(item) is >= 0 and var i)
                    icons.OpenMenu(i);
                return;
        }

        if (apps.Activate(item) && dock.BounceOnLaunch && Items.IndexOf(item) is >= 0 and var index)
            icons.Bounce(index);
    }

    public void CloseWindows(DockItem item) => apps.CloseWindows(item);

    public void KeepInDock(DockItem item) => apps.Pin(item);

    public void RemoveFromDock(DockItem item) => apps.Unpin(item);

    public void MovePinned(int from, int to) => apps.Move(from, to);

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

    /// <summary>The icons' drawing surface, for a WPF-only rendering in self-tests.</summary>
    public FrameworkElement? IconsView => icons.Content as FrameworkElement;
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
