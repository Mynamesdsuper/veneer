using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Veneer.Core.Layout;
using Veneer.Core.Themes;
using Veneer.Interop;

namespace Veneer.App;

/// <summary>One bar from the theme: an edge window with start / center / end module zones.</summary>
sealed class BarWindow : Window
{
    readonly BarSurface bar;
    readonly PixelRect rect;
    AppBar? appBar;

    public BarWindow(Theme theme, BarSurface bar, PixelRect rect, uint dpi)
    {
        this.bar = bar;
        this.rect = rect;
        Title = $"Veneer {bar.Id}";
        // WS_CAPTION is needed for the blurred backdrop; the WindowChrome hides the frame. See SurfaceWindow.
        WindowStyle = WindowStyle.SingleBorderWindow;
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

        // Start where the bar goes (in DIPs) so it does not flash elsewhere before Place().
        double scale = dpi / 96.0;
        Left = rect.Left / scale;
        Top = rect.Top / scale;
        Width = rect.Width / scale;
        Height = rect.Height / scale;

        Content = BuildContent(theme, bar);
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(hwnd);
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            source.AddHook(Hook);
            SurfaceWindow.Apply(hwnd, bar.Style.Blur, cornerRadiusPx: 0);
            appBar = new AppBar(hwnd);
        };
    }

    public void Place() => appBar?.Place(bar.Edge, rect, bar.ReserveSpace);

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

    static readonly FontFamily DefaultFont = new("Segoe UI Variable Text, Segoe UI");

    static FrameworkElement BuildContent(Theme theme, BarSurface bar)
    {
        var style = bar.Style;
        bool horizontal = bar.Edge is Edge.Top or Edge.Bottom;
        var zones = new Grid
        {
            Margin = horizontal ? new Thickness(style.Padding, 0, style.Padding, 0) : new Thickness(0, style.Padding, 0, style.Padding),
        };
        // Three overlapping panels: the center zone stays centered on the whole bar whatever the sides hold.
        zones.Children.Add(Zone(theme, bar, bar.Start, horizontal, HorizontalAlignment.Left, VerticalAlignment.Top));
        zones.Children.Add(Zone(theme, bar, bar.Center, horizontal, HorizontalAlignment.Center, VerticalAlignment.Center));
        zones.Children.Add(Zone(theme, bar, bar.End, horizontal, HorizontalAlignment.Right, VerticalAlignment.Bottom));

        var root = new Border
        {
            Background = ThemeVisuals.Brush(style.Background),
            BorderBrush = ThemeVisuals.Brush(style.Border),
            BorderThickness = style.Border is null ? new Thickness(0) : InnerEdge(bar.Edge),
            Child = zones,
        };
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

    static StackPanel Zone(Theme theme, BarSurface bar, IReadOnlyList<ModuleSpec> modules, bool horizontal, HorizontalAlignment h, VerticalAlignment v)
    {
        var panel = new StackPanel
        {
            Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical,
            HorizontalAlignment = horizontal ? h : HorizontalAlignment.Stretch,
            VerticalAlignment = horizontal ? VerticalAlignment.Stretch : v,
        };
        foreach (var spec in modules)
        {
            if (Modules.Create(spec, horizontal) is not { } element)
                continue;
            double pad = spec.Style.Padding ?? 0;
            double gap = panel.Children.Count == 0 ? 0 : bar.Style.Spacing;
            element.Margin = horizontal ? new Thickness(gap + pad, 0, pad, 0) : new Thickness(0, gap + pad, 0, pad);
            element.VerticalAlignment = horizontal ? VerticalAlignment.Center : VerticalAlignment.Top;
            element.HorizontalAlignment = horizontal ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            ThemeVisuals.ApplyText(element, theme, spec.Style.Text, spec.Style.Foreground);
            panel.Children.Add(element);
        }

        return panel;
    }
}
