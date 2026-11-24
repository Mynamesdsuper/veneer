using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Veneer.Core;
using Veneer.Core.Dock;
using Veneer.Core.Layout;
using Veneer.Core.Themes;
using Veneer.Interop;

namespace Veneer.App;

/// <summary>
/// The dock's icons, in a per-pixel transparent window above the plate. It is larger than the plate so magnified
/// icons can grow out of it; fully transparent pixels let clicks through. Over the plate itself it has an almost
/// invisible fill (alpha 1), so the mouse stays "inside the dock" in the gaps between icons.
/// Hover, click, right click and drag are handled here; actions go back to <see cref="DockWindow"/>.
/// </summary>
sealed class DockIconsWindow : Window
{
    const double DragThreshold = 6;
    static readonly TimeSpan Ramp = TimeSpan.FromMilliseconds(120);

    // A per-pixel transparent window gets Rendering callbacks far above the display rate (over 1000 per second
    // measured): cap the work to about 120 updates per second.
    static readonly TimeSpan MinFrame = TimeSpan.FromMilliseconds(8);
    readonly System.Windows.Threading.DispatcherTimer concealDelay = new() { Interval = TimeSpan.FromMilliseconds(600) };

    readonly DockWindow owner;
    readonly Canvas canvas = new();
    readonly Rectangle hitArea = new() { Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
    readonly List<FrameworkElement> cellViews = [];
    readonly List<FrameworkElement?> indicators = [];
    nint hwnd;
    Rect plate; // the plate inside this window, in DIPs
    double? cursor; // along the row, from its start
    double strength; // 0 = no magnification, 1 = full
    double targetStrength;
    bool animating;
    TimeSpan lastFrame;
    int pressIndex = -1;
    Point pressPoint;
    bool dragging;
    MagnifiedCell[] layout = [];

    public DockIconsWindow(DockWindow owner)
    {
        this.owner = owner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Content = canvas;
        canvas.Children.Add(hitArea);
        // 256 px icons drawn at 48: the default bilinear filter makes jagged edges.
        RenderOptions.SetBitmapScalingMode(canvas, BitmapScalingMode.HighQuality);
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            SurfaceWindow.MakeOverlay(hwnd);
        };
        MouseMove += OnMouseMove;
        MouseEnter += (_, _) => concealDelay.Stop();
        MouseLeave += (_, _) =>
        {
            if (dragging)
                return;
            Animate(0);
            if (Dock.AutoHide)
                concealDelay.Start();
        };
        concealDelay.Tick += (_, _) =>
        {
            concealDelay.Stop();
            if (!IsMouseOver)
                owner.SetRevealed(false);
        };
        MouseLeftButtonDown += OnLeftDown;
        MouseLeftButtonUp += OnLeftUp;
        MouseRightButtonUp += (_, e) =>
        {
            if (IndexAt(Along(e.GetPosition(canvas))) is >= 0 and var i)
                OpenMenu(i);
        };
        Closed += (_, _) =>
        {
            StopAnimation();
            concealDelay.Stop();
        };
        LostMouseCapture += (_, _) =>
        {
            // Alt+Tab or a menu took the mouse mid-drag: drop the drag instead of staying stuck in it.
            if (!dragging)
                return;
            dragging = false;
            pressIndex = -1;
            Relayout();
        };
    }

    DockSurface Dock => owner.Dock;

    bool Horizontal => Dock.Edge == Edge.Bottom;

    double MaxScale => Dock.Magnification.Enabled ? Dock.Magnification.MaxScale : 1;

    double RowStart => (Horizontal ? plate.Left : plate.Top) + Dock.Padding;

    /// <summary>Covers the plate, plus room for magnified icons toward the screen and beyond both ends.</summary>
    public void Place(PixelRect plateBounds, PixelRect area)
    {
        double scale = owner.Dpi / 96.0;
        double grow = Dock.IconSize * (MaxScale - 1);
        int across = SurfaceLayout.ToPhysical(grow, owner.Dpi);
        int along = SurfaceLayout.ToPhysical(grow * (Dock.Magnification.Radius + 1), owner.Dpi);
        var r = Dock.Edge switch
        {
            Edge.Left => new PixelRect(plateBounds.Left, plateBounds.Top - along, plateBounds.Right + across, plateBounds.Bottom + along),
            Edge.Right => new PixelRect(plateBounds.Left - across, plateBounds.Top - along, plateBounds.Right, plateBounds.Bottom + along),
            _ => new PixelRect(plateBounds.Left - along, plateBounds.Top - across, plateBounds.Right + along, plateBounds.Bottom),
        };
        r = new PixelRect(Math.Max(r.Left, area.Left), Math.Max(r.Top, area.Top), Math.Min(r.Right, area.Right), Math.Min(r.Bottom, area.Bottom));

        // Create the window without showing it: the plate decides visibility (auto-hide, full screen).
        new WindowInteropHelper(this).EnsureHandle();
        SurfaceWindow.PlaceTopmost(hwnd, r); // also lifts it above the plate, which just moved to the top too
        Width = r.Width / scale;
        Height = r.Height / scale;
        plate = new Rect((plateBounds.Left - r.Left) / scale, (plateBounds.Top - r.Top) / scale, plateBounds.Width / scale, plateBounds.Height / scale);
        canvas.Width = Width;
        canvas.Height = Height;
        Canvas.SetLeft(hitArea, plate.Left);
        Canvas.SetTop(hitArea, plate.Top);
        hitArea.Width = plate.Width;
        hitArea.Height = plate.Height;
        Relayout();
    }

    /// <summary>Recreates the icon views for <see cref="DockWindow.Items"/>.</summary>
    public void Rebuild()
    {
        foreach (var v in cellViews.Concat(indicators.OfType<FrameworkElement>()))
            canvas.Children.Remove(v);
        cellViews.Clear();
        indicators.Clear();
        foreach (var item in owner.Items)
        {
            FrameworkElement view = item.IsSeparator
                ? new Rectangle { Fill = ThemeVisuals.Brush(Dock.Style.Border ?? Dock.Style.Foreground) ?? Brushes.Gray, Opacity = 0.6, IsHitTestVisible = false }
                : new Image { Source = DockIcons.For(owner.Theme, item, owner.Dpi), Stretch = Stretch.Uniform, ToolTip = DockIcons.Name(item) };
            cellViews.Add(view);
            canvas.Children.Add(view);
            var indicator = item.IsRunning ? Indicator() : null;
            indicators.Add(indicator);
            if (indicator is not null)
                canvas.Children.Add(indicator);
        }

        Relayout();
    }

    FrameworkElement Indicator()
    {
        var ind = Dock.RunningIndicator;
        var brush = ThemeVisuals.Brush(ind.Color ?? Dock.Style.Foreground) ?? Brushes.Black;
        bool line = ind.Shape == IndicatorShape.Line;
        double longSide = Dock.IconSize * 0.4;
        return line
            ? new Rectangle { Fill = brush, Width = Horizontal ? longSide : ind.Size, Height = Horizontal ? ind.Size : longSide, RadiusX = ind.Size / 2, RadiusY = ind.Size / 2, IsHitTestVisible = false }
            : new Ellipse { Fill = brush, Width = ind.Size, Height = ind.Size, IsHitTestVisible = false };
    }

    // ----- layout -----

    double Along(Point p) => (Horizontal ? p.X : p.Y) - RowStart;

    int IndexAt(double along)
    {
        for (int i = 0; i < layout.Length; i++)
        {
            if (along >= layout[i].Start && along <= layout[i].Start + layout[i].Length)
                return owner.Items[i].IsSeparator ? -1 : i;
        }

        return -1;
    }

    void Relayout()
    {
        var items = owner.Items;
        if (items.Count != cellViews.Count)
            return;
        var cells = owner.Cells(items);
        double effective = 1 + (MaxScale - 1) * strength;
        layout = dragging ? DragLayout(cells) : Magnifier.Layout(cells, Dock.Style.Spacing, cursor, effective, Dock.Magnification.Radius);
        for (int i = 0; i < layout.Length; i++)
            Arrange(i, layout[i]);
    }

    // Places one cell. Icons sit on the screen-edge side of the plate (inside its padding) and grow away from the edge.
    void Arrange(int i, MagnifiedCell cell)
    {
        double start = RowStart + cell.Start, len = cell.Length, pad = Dock.Padding, size = Dock.IconSize;
        var view = cellViews[i];
        if (owner.Items[i].IsSeparator)
        {
            double inset = size * 0.15;
            if (Horizontal)
                Set(view, start + len / 2 - 0.5, plate.Bottom - pad - size + inset, 1, size - 2 * inset);
            else if (Dock.Edge == Edge.Left)
                Set(view, plate.Left + pad + inset, start + len / 2 - 0.5, size - 2 * inset, 1);
            else
                Set(view, plate.Right - pad - size + inset, start + len / 2 - 0.5, size - 2 * inset, 1);
            return;
        }

        switch (Dock.Edge)
        {
            case Edge.Left:
                Set(view, plate.Left + pad, start, len, len);
                break;
            case Edge.Right:
                Set(view, plate.Right - pad - len, start, len, len);
                break;
            default:
                Set(view, start, plate.Bottom - pad - len, len, len);
                break;
        }

        if (indicators[i] is { } ind)
        {
            double center = start + len / 2;
            switch (Dock.Edge)
            {
                case Edge.Left:
                    Set(ind, plate.Left + (pad - ind.Width) / 2, center - ind.Height / 2, ind.Width, ind.Height);
                    break;
                case Edge.Right:
                    Set(ind, plate.Right - pad + (pad - ind.Width) / 2, center - ind.Height / 2, ind.Width, ind.Height);
                    break;
                default:
                    Set(ind, center - ind.Width / 2, plate.Bottom - pad + (pad - ind.Height) / 2, ind.Width, ind.Height);
                    break;
            }
        }
    }

    static void Set(FrameworkElement e, double left, double top, double width, double height)
    {
        Canvas.SetLeft(e, left);
        Canvas.SetTop(e, top);
        e.Width = width;
        e.Height = height;
    }

    // ----- hover magnification -----

    void OnMouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(canvas);
        if (pressIndex >= 0 && e.LeftButton == MouseButtonState.Pressed && !dragging && (p - pressPoint).Length > DragThreshold && pressIndex < owner.Pinned.Count)
        {
            dragging = true;
            CaptureMouse();
        }

        cursor = Along(p);
        if (dragging)
            Relayout();
        else if (strength < 1)
            Animate(1);
        else
            Relayout();
    }

    void Animate(double target)
    {
        targetStrength = target;
        if (animating)
            return;
        animating = true;
        lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame; // only while animating: an idle dock costs nothing
    }

    void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (lastFrame != TimeSpan.Zero && now - lastFrame < MinFrame)
            return;
        double step = lastFrame == TimeSpan.Zero ? 0.25 : (now - lastFrame).TotalMilliseconds / Ramp.TotalMilliseconds;
        lastFrame = now;
        strength = targetStrength > strength ? Math.Min(targetStrength, strength + step) : Math.Max(targetStrength, strength - step);
        if (strength == 0)
            cursor = null;
        Relayout();
        if (strength == targetStrength)
            StopAnimation();
    }

    void StopAnimation()
    {
        if (!animating)
            return;
        animating = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    // ----- click, menu, drag -----

    void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        pressPoint = e.GetPosition(canvas);
        pressIndex = IndexAt(Along(pressPoint));
    }

    void OnLeftUp(object sender, MouseButtonEventArgs e)
    {
        int index = pressIndex;
        pressIndex = -1;
        if (dragging)
        {
            dragging = false;
            ReleaseMouseCapture();
            int to = DropIndex(Along(e.GetPosition(canvas)), index);
            if (to != index)
                owner.MovePinned(index, to);
            else
                Relayout();
            return;
        }

        if (index >= 0 && index == IndexAt(Along(e.GetPosition(canvas))))
            owner.Activate(owner.Items[index]);
    }

    // While dragging: the base layout with the dragged icon moved to where it would drop, and drawn at the cursor.
    MagnifiedCell[] DragLayout(List<(double Size, bool Scales)> cells)
    {
        int from = pressIndex, to = DropIndex(cursor ?? 0, from);
        var order = DockModel.DragOrder(cells.Count, from, to);
        var placed = Magnifier.Layout(order.Select(i => cells[i]).ToList(), Dock.Style.Spacing, null, 1, 0);
        var result = new MagnifiedCell[cells.Count];
        for (int k = 0; k < order.Length; k++)
            result[order[k]] = placed[k];
        result[from] = result[from] with { Start = (cursor ?? 0) - cells[from].Size / 2 };
        return result;
    }

    /// <summary>The pinned index the dragged icon would land on, from the cursor position in the base layout.</summary>
    int DropIndex(double along, int from) =>
        DockModel.DropIndex(owner.Cells(owner.Items).Select(c => c.Size).ToList(), Dock.Style.Spacing, owner.Pinned.Count, along, from);

    /// <summary>Auto-hide: start (or restart) the countdown that tucks the dock away when the mouse is not on it.</summary>
    public void StartConcealCountdown()
    {
        concealDelay.Stop();
        concealDelay.Start();
    }

    public void OpenMenu(int index)
    {
        var item = owner.Items[index];
        if (item.IsSeparator)
            return;
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = DockIcons.Name(item), IsEnabled = false });
        menu.Items.Add(new Separator());
        if (item.Pinned is null)
            menu.Items.Add(Modules.Item("Keep in Dock", () => owner.KeepInDock(item)));
        else
            menu.Items.Add(Modules.Item("Remove from Dock", () => owner.RemoveFromDock(item)));
        if (item.IsRunning)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Modules.Item(item.Running!.Windows.Count > 1 ? "Close All Windows" : "Close", () => owner.CloseWindows(item)));
        }

        Modules.Open(menu, cellViews[index], Dock.Edge);
    }

    /// <summary>Debug self-test: magnify as if the cursor were at <paramref name="fraction"/> of the row (null: stop).</summary>
    public void Hover(double? fraction)
    {
        var cells = owner.Cells(owner.Items);
        double row = cells.Sum(c => c.Size) + Math.Max(0, cells.Count - 1) * Dock.Style.Spacing;
        cursor = fraction * row;
        strength = fraction is null ? 0 : 1;
        Relayout();
    }

    /// <summary>
    /// Debug self-test: moves a virtual cursor across the dock for two seconds at full magnification, one step per
    /// rendered frame, and logs the frame rate and the slowest frame.
    /// </summary>
    public void Sweep()
    {
        var cells = owner.Cells(owner.Items);
        double row = cells.Sum(c => c.Size) + Math.Max(0, cells.Count - 1) * Dock.Style.Spacing;
        var clock = Stopwatch.StartNew();
        var frames = new List<double>();
        double last = 0;
        strength = 1;
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            double t = clock.Elapsed.TotalMilliseconds;
            if (t - last < MinFrame.TotalMilliseconds)
                return;
            frames.Add(t - last);
            last = t;
            cursor = row * Math.Min(1, t / 2000);
            Relayout();
            if (t < 2000)
                return;
            CompositionTarget.Rendering -= handler;
            strength = 0;
            cursor = null;
            Relayout();
            var steady = frames.Skip(1).ToList();
            Log.Write($"dock sweep: {steady.Count} frames in {t:F0} ms = {steady.Count / (t / 1000):F1} fps, slowest {steady.DefaultIfEmpty(0).Max():F1} ms");
        };
        CompositionTarget.Rendering += handler;
    }
}
