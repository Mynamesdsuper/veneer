using Veneer.Core.Layout;
using Veneer.Core.Themes;

namespace Veneer.Core.Tests.Layout;

public class SurfaceLayoutTests
{
    static readonly PixelRect Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void Top_and_bottom_bars_span_the_full_width()
    {
        var r = SurfaceLayout.Compute(Screen, [new("top", Edge.Top, 24), new("bottom", Edge.Bottom, 40)]);

        Assert.Equal(new PixelRect(0, 0, 1920, 24), r["top"]);
        Assert.Equal(new PixelRect(0, 1040, 1920, 1080), r["bottom"]);
    }

    [Theory]
    [InlineData(Edge.Bottom, 8, 1032, 1912, 1072)]
    [InlineData(Edge.Top, 8, 8, 1912, 48)]
    public void A_floating_bar_keeps_its_margin_from_the_edge_and_the_sides(Edge edge, int left, int top, int right, int bottom)
    {
        var strip = SurfaceLayout.Compute(Screen, [new("bar", edge, 48)])["bar"]; // thickness 40 + margin 8

        Assert.Equal(new PixelRect(left, top, right, bottom), SurfaceLayout.Float(strip, edge, 8));
    }

    [Fact]
    public void A_floating_side_bar_keeps_its_margin_along_its_length()
    {
        Assert.Equal(new PixelRect(8, 8, 48, 1072), SurfaceLayout.Float(new PixelRect(0, 0, 48, 1080), Edge.Left, 8));
        Assert.Equal(new PixelRect(1872, 8, 1912, 1072), SurfaceLayout.Float(new PixelRect(1872, 0, 1920, 1080), Edge.Right, 8));
    }

    [Fact]
    public void Bars_on_the_same_edge_stack_in_theme_order()
    {
        var r = SurfaceLayout.Compute(Screen, [new("a", Edge.Top, 24), new("b", Edge.Top, 30), new("c", Edge.Right, 50), new("d", Edge.Right, 10)]);

        Assert.Equal(new PixelRect(0, 0, 1920, 24), r["a"]);
        Assert.Equal(new PixelRect(0, 24, 1920, 54), r["b"]);
        Assert.Equal(new PixelRect(1870, 54, 1920, 1080), r["c"]);
        Assert.Equal(new PixelRect(1860, 54, 1870, 1080), r["d"]);
    }

    [Fact]
    public void Side_bars_fit_between_top_and_bottom_bars_whatever_the_theme_order()
    {
        var r = SurfaceLayout.Compute(Screen, [new("left", Edge.Left, 48), new("top", Edge.Top, 24), new("bottom", Edge.Bottom, 40)]);

        Assert.Equal(new PixelRect(0, 24, 48, 1040), r["left"]);
    }

    [Fact]
    public void Works_on_a_monitor_that_is_not_at_the_origin()
    {
        var second = new PixelRect(-2560, 0, 0, 1440);
        var r = SurfaceLayout.Compute(second, [new("top", Edge.Top, 30), new("left", Edge.Left, 60)]);

        Assert.Equal(new PixelRect(-2560, 0, 0, 30), r["top"]);
        Assert.Equal(new PixelRect(-2560, 30, -2500, 1440), r["left"]);
    }

    static readonly PixelRect Work = new(0, 0, 1920, 1032);

    [Fact]
    public void A_maximized_window_that_fits_the_work_area_is_left_alone()
    {
        Assert.False(SurfaceLayout.NeedsRefit(new PixelRect(-4, -4, 1924, 1036), Work)); // 4 px frame overhang
        Assert.False(SurfaceLayout.NeedsRefit(new PixelRect(0, 0, 1920, 1032), Work));   // no overhang
    }

    [Fact]
    public void A_maximized_window_sized_for_old_bars_is_moved_back()
    {
        // Zen after a crash: still sized for a 24 px top bar and a hidden taskbar.
        var stale = new PixelRect(-4, 20, 1924, 1108);

        Assert.True(SurfaceLayout.NeedsRefit(stale, Work));
        Assert.Equal(new PixelRect(-4, -4, 1924, 1036), SurfaceLayout.MaximizedBounds(stale, Work));
    }

    [Theory]
    [InlineData(24, 96u, 24)]
    [InlineData(24, 144u, 36)]
    [InlineData(25, 120u, 31)] // 31.25
    [InlineData(26, 120u, 33)] // 32.5 rounds away from zero
    public void Converts_dips_to_physical_pixels(double dips, uint dpi, int expected) =>
        Assert.Equal(expected, SurfaceLayout.ToPhysical(dips, dpi));
}
