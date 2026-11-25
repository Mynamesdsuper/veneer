using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Veneer.Core.Dock;
using Veneer.Core.Themes;

namespace Veneer.App;

/// <summary>
/// Pinned and running apps in a bar, like a taskbar. A click shows the app's last window or starts it; a right click
/// opens Pin / Unpin / Close. The pinned list is the bar's <c>defaultPinned</c>, so the user's changes are one
/// override key, as for a dock.
/// </summary>
sealed class AppsModule : StackPanel
{
    readonly ModuleContext ctx;
    readonly BarSurface bar;
    readonly AppList apps;
    readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(150) };
    readonly double iconSize;
    readonly double itemPadding;
    readonly IndicatorShape indicatorShape;
    readonly Brush indicatorBrush;
    readonly double indicatorSize;

    public AppsModule(ModuleSpec spec, ModuleContext ctx, BarSurface bar, Veneer.Interop.AppWindowTracker tracker)
    {
        this.ctx = ctx;
        this.bar = bar;
        Orientation = ctx.Horizontal ? Orientation.Horizontal : Orientation.Vertical;
        iconSize = spec.Options["iconSize"]?.GetValue<double>() ?? 24;
        itemPadding = spec.Options["itemPadding"]?.GetValue<double>() ?? 6;
        var ind = spec.Options["runningIndicator"] as System.Text.Json.Nodes.JsonObject;
        indicatorShape = string.Equals(ind?["shape"]?.GetValue<string>(), "line", StringComparison.OrdinalIgnoreCase) ? IndicatorShape.Line : IndicatorShape.Dot;
        indicatorSize = ind?["size"]?.GetValue<double>() ?? 3;
        ThemeColor? color = ind?["color"]?.GetValue<string>() is { } text && ThemeColor.TryParse(text, out var c) ? c : null;
        indicatorBrush = ThemeVisuals.Brush(color ?? spec.Style.Foreground ?? bar.Style.Foreground) ?? Brushes.Gray;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality); // shell icons are 256 px

        apps = new AppList(bar.Id, bar.DefaultPinned ?? [], tracker, ctx.OverridesPath,
            Modules.Flag(spec, "separateRunning", false), () => Refresh(force: true));
        debounce.Tick += (_, _) =>
        {
            debounce.Stop();
            Refresh();
        };
        Loaded += (_, _) =>
        {
            apps.Closed = false;
            tracker.Changed += OnWindowsChanged;
            Refresh(force: true);
        };
        Unloaded += (_, _) =>
        {
            // A rebuild closes the bar: nothing queued may write a stale pinned list afterwards.
            apps.Closed = true;
            debounce.Stop();
            tracker.Changed -= OnWindowsChanged;
        };
    }

    void OnWindowsChanged() => debounce.Start(); // restarts the countdown on every event of a burst

    /// <summary>The theme reloaded and only the pinned list changed (usually our own write): update in place.</summary>
    public void UpdatePinned(IReadOnlyList<string> pinned)
    {
        if (apps.SetPinned(pinned))
            Refresh(force: true);
    }

    /// <summary>Debug self-test: unpins the first item whose key contains <paramref name="text"/>.</summary>
    public void UnpinItem(string text)
    {
        if (apps.Items.FirstOrDefault(i => i.Key.Contains(text, StringComparison.OrdinalIgnoreCase)) is { } item)
            apps.Unpin(item);
    }

    void Refresh(bool force = false)
    {
        if (!apps.Update(force))
            return;
        Children.Clear();
        foreach (var item in apps.Items)
            Children.Add(item.IsSeparator ? SeparatorView() : new AppButton(this, item));
    }

    // Each button spans the bar minus the highlight inset, like the other clickable modules.
    double Across => Math.Max(iconSize, bar.Thickness - 2 * bar.Style.HighlightInset);

    FrameworkElement SeparatorView()
    {
        var line = new Rectangle
        {
            Fill = ThemeVisuals.Brush(bar.Style.Border ?? bar.Style.Foreground) ?? Brushes.Gray,
            Opacity = 0.6,
            Margin = new Thickness(bar.Style.Spacing / 2 + 2),
            IsHitTestVisible = false,
        };
        if (ctx.Horizontal)
        {
            line.Width = 1;
            line.Height = iconSize * 0.7;
        }
        else
        {
            line.Height = 1;
            line.Width = iconSize * 0.7;
        }

        return line;
    }

    void OpenMenu(AppButton button)
    {
        var item = button.Item;
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = DockIcons.Name(item), IsEnabled = false });
        menu.Items.Add(new Separator());
        if (item.Pinned is null)
            menu.Items.Add(Modules.Item(Strings.AppsPin, () => apps.Pin(item)));
        else
            menu.Items.Add(Modules.Item(Strings.AppsUnpin, () => apps.Unpin(item)));
        if (item.IsRunning)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Modules.Item(item.Running!.Windows.Count > 1 ? Strings.DockCloseAll : Strings.DockClose, () => apps.CloseWindows(item)));
        }

        Modules.Open(menu, button, ctx.Edge);
    }

    /// <summary>One app: its icon on the bar's highlight, with the running indicator on the side facing the desktop.</summary>
    sealed class AppButton : ClickableModule
    {
        readonly AppsModule owner;

        public AppButton(AppsModule owner, DockItem item)
        {
            this.owner = owner;
            Item = item;
            bool horizontal = owner.ctx.Horizontal;
            UseHighlight(owner.bar.Style, new Thickness(0));
            var grid = new Grid();
            grid.Children.Add(new Image
            {
                Source = DockIcons.For(owner.ctx.Theme, item, owner.ctx.Dpi),
                Width = owner.iconSize,
                Height = owner.iconSize,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (item.IsRunning)
                grid.Children.Add(Indicator(horizontal));
            Child = grid;
            if (horizontal)
            {
                Width = owner.iconSize + 2 * owner.itemPadding;
                Height = owner.Across;
            }
            else
            {
                Height = owner.iconSize + 2 * owner.itemPadding;
                Width = owner.Across;
            }

            ToolTip = DockIcons.Name(item);
            MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                owner.OpenMenu(this);
            };
        }

        public DockItem Item { get; }

        protected override void OnClick() => owner.apps.Activate(Item);

        FrameworkElement Indicator(bool horizontal)
        {
            double size = owner.indicatorSize, longSide = owner.iconSize * 0.4;
            bool line = owner.indicatorShape == IndicatorShape.Line;
            Shape shape = line
                ? new Rectangle { Width = horizontal ? longSide : size, Height = horizontal ? size : longSide, RadiusX = size / 2, RadiusY = size / 2 }
                : new Ellipse { Width = size, Height = size };
            shape.Fill = owner.indicatorBrush;
            shape.IsHitTestVisible = false;
            // On the screen-edge side, as the Windows taskbar does.
            shape.HorizontalAlignment = horizontal ? HorizontalAlignment.Center : owner.ctx.Edge == Edge.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            shape.VerticalAlignment = !horizontal ? VerticalAlignment.Center : owner.ctx.Edge == Edge.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            shape.Margin = new Thickness(1);
            return shape;
        }
    }
}
