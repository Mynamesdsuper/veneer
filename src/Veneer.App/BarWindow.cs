using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Veneer.Core;
using Veneer.Core.Layout;
using Veneer.Core.Themes;
using Veneer.Interop;

namespace Veneer.App;

/// <summary>
/// One bar from the theme: an edge window with start / center / end module zones. A bar with a <c>margin</c> floats
/// inside its screen strip; above the radius DWM can round, it is drawn like the dock's plate (per-pixel transparent
/// window, blur from a <see cref="GlassPlate"/>).
/// </summary>
sealed class BarWindow : Window
{
    readonly BarSurface bar;
    readonly PixelRect strip;
    readonly int margin;
    readonly int radius;
    readonly uint dpi;
    readonly Border root;
    AppBar? appBar;
    GlassPlate? plate;

    public BarWindow(Theme theme, BarSurface bar, PixelRect strip, uint dpi, AppWindowTracker? apps, string? overridesPath)
    {
        this.bar = bar;
        this.strip = strip;
        this.dpi = dpi;
        margin = SurfaceLayout.ToPhysical(bar.Margin, dpi);
        radius = SurfaceLayout.ToPhysical(bar.CornerRadius, dpi);
        Title = $"Veneer {bar.Id}";
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Transparent;
        if (Layered)
        {
            // Per-pixel transparent, so the corners outside the rounded bar stay empty.
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
        }
        else
        {
            // WS_CAPTION is needed for the blurred backdrop; the WindowChrome hides the frame. See SurfaceWindow.
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                GlassFrameThickness = new Thickness(-1),
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(0),
                UseAeroCaptionButtons = false,
            });
        }

        // Start where the bar goes (in DIPs) so it does not flash elsewhere before Place().
        var rect = SurfaceLayout.Float(strip, bar.Edge, margin);
        double scale = dpi / 96.0;
        Left = rect.Left / scale;
        Top = rect.Top / scale;
        Width = rect.Width / scale;
        Height = rect.Height / scale;

        // WPF lays out a captioned window at least as large as the system minimum it saw at creation (SM_CYMINTRACK,
        // 34 px here), whatever the real window size: pin the content to the bar's thickness, or a thin bar's modules
        // sit off-center and its edge line falls outside the window. The length still follows the window.
        var ctx = new ModuleContext(theme, bar.Edge, bar, apps, overridesPath, dpi);
        root = BuildContent(theme, bar, ctx);
        Paint(glass: Layered && bar.Style.Blur);
        if (bar.Edge is Edge.Top or Edge.Bottom)
        {
            root.Height = Height;
            root.VerticalAlignment = VerticalAlignment.Top;
        }
        else
        {
            root.Width = Width;
            root.HorizontalAlignment = HorizontalAlignment.Left;
        }

        if (Layered && bar.Style.Blur)
        {
            // A GlassPlate hides its window's WPF content, so the modules live in a transparent window on top (as the
            // dock's icons do); this window keeps only an almost invisible shape and reserves the screen strip.
            Content = new Border { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), CornerRadius = new CornerRadius(bar.CornerRadius) };
            modules = new BarModulesWindow(root);
            // An owned window does not hide with its owner (full-screen apps hide the bar).
            IsVisibleChanged += (_, _) =>
            {
                if (!IsVisible)
                    modules.Hide();
                else
                    Follow(); // shows it again where the bar is (before the first Place there is nowhere to show it)
            };
            Closed += (_, _) => modules.Close();
        }
        else
        {
            Content = root;
        }

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(hwnd);
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            source.AddHook(Hook);
            if (Layered)
            {
                SurfaceWindow.MakeOverlay(hwnd);
                if (bar.Style.Blur)
                    TryGlass(hwnd);
            }
            else
            {
                SurfaceWindow.Apply(hwnd, bar.Style.Blur, radius);
                // A bar on the edge casts no shadow (a forced-active window would darken the desktop); a floating one keeps it.
                if (!Floating)
                    SurfaceWindow.RemoveShadow(hwnd);
            }

            appBar = new AppBar(hwnd);
            appBar.FullScreenChanged += full => FullScreenChanged?.Invoke(full);
            appBar.Moved += Follow;
            // An owned window always stays above its owner, even when the shell moves the bar.
            if (modules is not null)
                modules.Owner = this;
        };
    }

    BarModulesWindow? modules;

    bool Floating => margin > 0 || radius > 0;

    // Above the radius DWM can round, DWM's backdrop and frame would fill the square corners (see DockWindow).
    bool Layered => radius > SurfaceWindow.MaxNativeCornerRadius;

    void TryGlass(nint hwnd)
    {
        try
        {
            var style = bar.Style;
            plate = new GlassPlate(hwnd, radius, style.Background, style.Border, style.BorderHighlight, SurfaceLayout.ToPhysical(1, dpi));
        }
        catch (Exception e)
        {
            // Keep a working bar: the WPF background over a plain blur, with square blurred corners.
            Log.Write($"bar \"{bar.Id}\": rounded glass not available, using the plain blur: {e.Message}");
            Paint(glass: false);
            SurfaceWindow.ApplyPopupBlur(hwnd, 0, round: false, acrylic: false);
        }
    }

    /// <summary>
    /// The bar's background and border, drawn by WPF; with <paramref name="glass"/> the GlassPlate draws them and WPF
    /// keeps only an almost invisible shape, so the per-pixel transparent bar stays clickable everywhere.
    /// </summary>
    void Paint(bool glass)
    {
        var style = bar.Style;
        root.CornerRadius = new CornerRadius(bar.CornerRadius);
        if (glass)
        {
            root.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            root.BorderThickness = new Thickness(0);
            return;
        }

        root.Background = ThemeVisuals.Brush(style.Background);
        root.BorderBrush = ThemeVisuals.BorderBrush(style.Border, style.BorderHighlight);
        bool border = style.Border is not null || (Floating && style.BorderHighlight is not null);
        // A bar on the edge has one line, on the side facing the desktop; a floating bar is outlined all around.
        root.BorderThickness = !border ? new Thickness(0) : Floating ? new Thickness(1) : InnerEdge(bar.Edge);
    }

    public T? FindModule<T>() where T : FrameworkElement => FindAll<T>(root).FirstOrDefault();

    static IEnumerable<T> FindAll<T>(DependencyObject node) where T : FrameworkElement
    {
        if (node is T match)
            yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            foreach (var found in FindAll<T>(VisualTreeHelper.GetChild(node, i)))
                yield return found;
        }
    }

    public BarSurface Bar => bar;

    /// <summary>Where the bar is, in physical pixels (empty before the first Place()).</summary>
    public PixelRect Bounds => appBar?.Bounds ?? default;

    /// <summary>The theme reloaded and only the pinned list changed: the bar's <c>apps</c> modules update in place.</summary>
    public void UpdatePinned(IReadOnlyList<string> pinned)
    {
        foreach (var apps in FindAll<AppsModule>(root))
            apps.UpdatePinned(pinned);
    }

    public event Action<bool>? FullScreenChanged;

    public void Place()
    {
        if (appBar is null)
            return;
        appBar.Place(bar.Edge, strip, bar.ReserveSpace, margin > 0 ? granted => SurfaceLayout.Float(granted, bar.Edge, margin) : null);
        Follow();
    }

    // The glass and the modules window follow the bar wherever the shell put it.
    void Follow()
    {
        if (appBar is null || appBar.Bounds.Width == 0)
            return;
        plate?.Resize(appBar.Bounds.Width, appBar.Bounds.Height);
        modules?.Place(appBar.Bounds, IsVisible, dpi);
    }

    public void RemoveAppBar() => appBar?.Remove();

    /// <summary>
    /// The modules of a bar drawn by a <see cref="GlassPlate"/>: a per-pixel transparent window owned by the bar and
    /// laid exactly over it. Its almost invisible background keeps the whole bar clickable.
    /// </summary>
    sealed class BarModulesWindow : Window
    {
        nint hwnd;

        public BarModulesWindow(FrameworkElement content)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Content = content;
            SourceInitialized += (_, _) =>
            {
                hwnd = new WindowInteropHelper(this).Handle;
                SurfaceWindow.MakeOverlay(hwnd);
            };
        }

        public void Place(PixelRect bounds, bool visible, uint dpi)
        {
            // Move first, show after: shown before its first move, it would flash at the default place and size.
            new WindowInteropHelper(this).EnsureHandle();
            SurfaceWindow.PlaceTopmost(hwnd, bounds);
            double scale = dpi / 96.0;
            Left = bounds.Left / scale;
            Top = bounds.Top / scale;
            Width = bounds.Width / scale;
            Height = bounds.Height / scale;
            if (visible && !IsVisible)
                Show();
        }
    }

    public void OnShellRestarted()
    {
        appBar?.OnShellRestarted();
        Follow(); // the shell may have given the bar another strip; it raises no Moved for this
    }

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

    static readonly FontFamily DefaultFont = new("Segoe UI Variable Text, Segoe UI");

    static Border BuildContent(Theme theme, BarSurface bar, ModuleContext ctx)
    {
        var style = bar.Style;
        bool horizontal = bar.Edge is Edge.Top or Edge.Bottom;
        var zones = new Grid
        {
            Margin = horizontal ? new Thickness(style.Padding, 0, style.Padding, 0) : new Thickness(0, style.Padding, 0, style.Padding),
        };
        // Three overlapping panels: the center zone stays centered on the whole bar whatever the sides hold.
        zones.Children.Add(Zone(theme, bar, ctx, bar.Start, horizontal, HorizontalAlignment.Left, VerticalAlignment.Top));
        zones.Children.Add(Zone(theme, bar, ctx, bar.Center, horizontal, HorizontalAlignment.Center, VerticalAlignment.Center));
        zones.Children.Add(Zone(theme, bar, ctx, bar.End, horizontal, HorizontalAlignment.Right, VerticalAlignment.Bottom));

        var root = new Border { Child = zones };
        // Windows 11 UI font unless the theme says otherwise. SystemFonts.MessageFontFamily can be an old
        // NONCLIENTMETRICS value (Tahoma on the test PC), so it is not used.
        TextElement.SetFontFamily(root, DefaultFont);
        TextElement.SetFontSize(root, 12);
        ThemeVisuals.ApplyText(root, theme, style.Text, style.Foreground ?? new ThemeColor(0xFF, 0, 0, 0));
        return root;
    }

    // A border line on the side that faces the desktop.
    static Thickness InnerEdge(Edge edge) => edge switch
    {
        Edge.Top => new Thickness(0, 0, 0, 1),
        Edge.Bottom => new Thickness(0, 1, 0, 0),
        Edge.Left => new Thickness(0, 0, 1, 0),
        _ => new Thickness(1, 0, 0, 0),
    };

    static StackPanel Zone(Theme theme, BarSurface bar, ModuleContext ctx, IReadOnlyList<ModuleSpec> modules, bool horizontal, HorizontalAlignment h, VerticalAlignment v)
    {
        var panel = new StackPanel
        {
            Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical,
            HorizontalAlignment = horizontal ? h : HorizontalAlignment.Stretch,
            VerticalAlignment = horizontal ? VerticalAlignment.Stretch : v,
        };
        foreach (var spec in modules)
        {
            if (Modules.Create(spec, ctx) is not { } element)
                continue;
            double pad = spec.Style.Padding ?? 0;
            double gap = panel.Children.Count == 0 ? 0 : bar.Style.Spacing;
            double inset = bar.Style.HighlightInset;
            if (element is ClickableModule clickable)
            {
                // Padding inside the highlight; the highlight spans the bar minus the inset.
                clickable.UseHighlight(bar.Style, horizontal ? new Thickness(pad, 0, pad, 0) : new Thickness(0, pad, 0, pad));
                element.Margin = horizontal ? new Thickness(gap, inset, 0, inset) : new Thickness(inset, gap, inset, 0);
                element.VerticalAlignment = horizontal ? VerticalAlignment.Stretch : VerticalAlignment.Top;
                element.HorizontalAlignment = horizontal ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
                if (element is Border { Child: FrameworkElement content })
                    content.VerticalAlignment = VerticalAlignment.Center;
            }
            else
            {
                element.Margin = horizontal ? new Thickness(gap + pad, 0, pad, 0) : new Thickness(0, gap + pad, 0, pad);
                element.VerticalAlignment = horizontal ? VerticalAlignment.Center : VerticalAlignment.Top;
                element.HorizontalAlignment = horizontal ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            }

            ThemeVisuals.ApplyText(element, theme, spec.Style.Text, spec.Style.Foreground);
            panel.Children.Add(spec.Style.Background is { } tile ? Tile(element, tile, spec, gap, pad, inset, horizontal) : element);
        }

        return panel;
    }

    /// <summary>
    /// A module on its own tile (module <c>style.background</c>): the tile spans the bar minus the highlight inset and
    /// takes over the module's gap; a clickable module's highlight fills the tile, other modules keep their padding.
    /// </summary>
    static Border Tile(FrameworkElement element, ThemeColor color, ModuleSpec spec, double gap, double pad, double inset, bool horizontal)
    {
        bool clickable = element is ClickableModule;
        element.Margin = new Thickness(0);
        return new Border
        {
            Background = ThemeVisuals.Brush(color),
            CornerRadius = new CornerRadius(spec.Style.CornerRadius),
            Child = element,
            Margin = horizontal ? new Thickness(gap, inset, 0, inset) : new Thickness(inset, gap, inset, 0),
            Padding = clickable ? new Thickness(0) : horizontal ? new Thickness(pad, 0, pad, 0) : new Thickness(0, pad, 0, pad),
            VerticalAlignment = horizontal ? VerticalAlignment.Stretch : VerticalAlignment.Top,
            HorizontalAlignment = horizontal ? HorizontalAlignment.Left : HorizontalAlignment.Stretch,
        };
    }
}
