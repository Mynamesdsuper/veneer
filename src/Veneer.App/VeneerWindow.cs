using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace Veneer.App;

/// <summary>
/// A top-level window with Veneer's own title bar instead of the Windows one. <see cref="WindowChrome"/> keeps the
/// native frame's behavior (move, resize from the edges, snap, rounded corners, shadow, Alt+Space, Win+arrows);
/// Veneer draws the icon, the title and the caption buttons. Subclasses put their content in <see cref="Body"/>.
/// </summary>
class VeneerWindow : Window
{
    const double TitleBarHeight = 40;
    const int WM_NCHITTEST = 0x0084, WM_NCMOUSEMOVE = 0x00A0, WM_NCLBUTTONDOWN = 0x00A1, WM_NCLBUTTONUP = 0x00A2,
        WM_NCLBUTTONDBLCLK = 0x00A3, WM_NCMOUSELEAVE = 0x02A2, HTMAXBUTTON = 9;

    static readonly ImageSource? AppIcon = LoadIcon();

    readonly WindowChrome chrome = new()
    {
        CaptionHeight = TitleBarHeight,
        ResizeBorderThickness = SystemParameters.WindowResizeBorderThickness,
        // No DWM glass in the client area: through it DWM paints its own caption (accent strip, buttons) under Veneer's.
        GlassFrameThickness = new Thickness(0),
        UseAeroCaptionButtons = false,
    };

    readonly Grid root = new();
    readonly Border body = new();
    readonly TextBlock title = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly CaptionButton minimize, maximize, close;
    bool maximizeHover, maximizePressed;

    public VeneerWindow()
    {
        WindowChrome.SetWindowChrome(this, chrome);
        SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
        title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Title)) { Source = this });
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

        minimize = new CaptionButton("\uE921", Strings.CaptionMinimize, isClose: false);
        minimize.Click += (_, _) => SystemCommands.MinimizeWindow(this);
        maximize = new CaptionButton("\uE922", Strings.CaptionMaximize, isClose: false);
        maximize.Click += (_, _) => ToggleMaximize(); // keyboard and UI Automation; the mouse arrives as HTMAXBUTTON below
        close = new CaptionButton("\uE8BB", Strings.CaptionClose, isClose: true);
        close.Click += (_, _) => SystemCommands.CloseWindow(this);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        buttons.Children.Add(minimize);
        buttons.Children.Add(maximize);
        buttons.Children.Add(close);
        var bar = new DockPanel { Height = TitleBarHeight };
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons);
        if (AppIcon is not null)
            bar.Children.Add(new Image { Source = AppIcon, Width = 16, Height = 16, Margin = new Thickness(16, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
        bar.Children.Add(title);

        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.Children.Add(bar);
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        Content = root;

        Activated += (_, _) => title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        Deactivated += (_, _) =>
        {
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
            ClearMaximizeHover();
        };
    }

    /// <summary>What the window shows under its title bar.</summary>
    public UIElement? Body
    {
        get => body.Child;
        set => body.Child = value;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // ResizeMode is set by the subclass after this constructor: the buttons and edges follow it, like the Windows frame.
        bool resizable = ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;
        minimize.Visibility = ResizeMode == ResizeMode.NoResize ? Visibility.Collapsed : Visibility.Visible;
        maximize.Visibility = resizable ? Visibility.Visible : Visibility.Collapsed;
        if (!resizable)
            chrome.ResizeBorderThickness = new Thickness(0);
        nint hwnd = new WindowInteropHelper(this).Handle;
        Veneer.Interop.WindowTools.RoundCorners(hwnd);
        HwndSource.FromHwnd(hwnd)?.AddHook(Hook);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        bool maximized = WindowState == WindowState.Maximized;
        // A maximized window is as large as the screen plus its invisible resize edges: keep the content on screen.
        root.Margin = maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        maximize.Glyph = maximized ? "\uE923" : "\uE922";
        AutomationProperties.SetName(maximize, maximized ? Strings.CaptionRestore : Strings.CaptionMaximize);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        ClearMaximizeHover(); // back in the client area: Windows does not always send WM_NCMOUSELEAVE
    }

    void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    void ClearMaximizeHover() => PaintMaximize(hover: false, pressed: false);

    // Repaints only on a change: client-area mouse moves call this all the time.
    void PaintMaximize(bool hover, bool pressed)
    {
        if (hover == maximizeHover && pressed == maximizePressed)
            return;
        maximizeHover = hover;
        maximizePressed = pressed;
        maximize.Paint(hover, pressed);
    }

    // Windows 11 shows the snap layouts flyout only over a window's HTMAXBUTTON. Saying HTMAXBUTTON makes the clicks
    // non-client messages that WPF never sees, so the maximize button's hover, press and click are handled here.
    nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_NCHITTEST when OverMaximize(lParam):
                handled = true;
                return HTMAXBUTTON;
            case WM_NCMOUSEMOVE:
                bool over = wParam == HTMAXBUTTON;
                PaintMaximize(hover: over, pressed: over && maximizePressed);
                break;
            case WM_NCMOUSELEAVE:
                ClearMaximizeHover();
                break;
            // A double click arrives as DOWN, UP, DBLCLK, UP: the DBLCLK counts as a press, or DefWindowProc would act on it.
            case WM_NCLBUTTONDOWN or WM_NCLBUTTONDBLCLK when wParam == HTMAXBUTTON:
                PaintMaximize(hover: true, pressed: true);
                handled = true;
                break;
            case WM_NCLBUTTONUP when wParam == HTMAXBUTTON:
                bool click = maximizePressed;
                ClearMaximizeHover();
                handled = true;
                if (click)
                    ToggleMaximize();
                break;
        }

        return 0;
    }

    bool OverMaximize(nint lParam)
    {
        if (!maximize.IsVisible)
            return false;
        // Screen coordinates in physical pixels, signed (a monitor left of or above the primary one is negative).
        var point = maximize.PointFromScreen(new Point((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF)));
        return point.X >= 0 && point.Y >= 0 && point.X < maximize.ActualWidth && point.Y < maximize.ActualHeight;
    }

    /// <summary>The maximize button's center on screen, in physical pixels (for the self-test's hit test).</summary>
    public Point MaximizeCenterOnScreen() => maximize.PointToScreen(new Point(maximize.ActualWidth / 2, maximize.ActualHeight / 2));

    /// <summary>The title bar's center on screen, between the title and the buttons (for the self-test's hit test).</summary>
    public Point TitleBarPointOnScreen() => root.PointToScreen(new Point(root.ActualWidth / 2, TitleBarHeight / 2));

    static ImageSource? LoadIcon()
    {
        try
        {
            // veneer.ico is the app's icon (ApplicationIcon) and a resource: take its frame nearest to 32 px for 16 px at 200%.
            var decoder = BitmapDecoder.Create(new Uri("pack://application:,,,/veneer.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return decoder.Frames.OrderBy(f => Math.Abs(f.PixelWidth - 32)).First();
        }
        catch (Exception e) when (e is IOException or NotSupportedException or FileFormatException)
        {
            Veneer.Core.Log.Write($"title bar icon: {e.Message}");
            return null;
        }
    }

    /// <summary>A caption button drawn like the Windows 11 ones: a Segoe Fluent Icons glyph, red when Close is hovered.</summary>
    sealed class CaptionButton : Button
    {
        static readonly ControlTemplate Plain = MakeTemplate();
        static readonly Brush CloseHover = Frozen(Color.FromRgb(0xC4, 0x2B, 0x1C));
        static readonly Brush ClosePressed = Frozen(Color.FromArgb(0xE6, 0xC4, 0x2B, 0x1C));
        readonly TextBlock glyph = new() { FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10 };
        readonly bool isClose;

        public CaptionButton(string glyphText, string name, bool isClose)
        {
            this.isClose = isClose;
            glyph.Text = glyphText;
            Template = Plain;
            Content = glyph;
            Width = 46;
            Height = TitleBarHeight;
            Focusable = false; // like the Windows caption buttons: Alt+Space and Alt+F4 are the keyboard path
            AutomationProperties.SetName(this, name);
            WindowChrome.SetIsHitTestVisibleInChrome(this, true);
            Paint(hover: false, pressed: false);
        }

        public string Glyph
        {
            set => glyph.Text = value;
        }

        public void Paint(bool hover, bool pressed)
        {
            if (isClose && (hover || pressed))
            {
                Background = pressed ? ClosePressed : CloseHover;
                glyph.Foreground = Brushes.White;
                return;
            }

            if (pressed)
                SetResourceReference(BackgroundProperty, "SubtleFillColorTertiaryBrush");
            else if (hover)
                SetResourceReference(BackgroundProperty, "SubtleFillColorSecondaryBrush");
            else
                Background = Brushes.Transparent;
            glyph.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            Paint(hover: true, pressed: IsPressed);
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            Paint(hover: false, pressed: false);
        }

        protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnIsPressedChanged(e);
            Paint(hover: IsMouseOver, pressed: IsPressed);
        }

        static ControlTemplate MakeTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            return new ControlTemplate(typeof(Button)) { VisualTree = border };
        }

        static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
