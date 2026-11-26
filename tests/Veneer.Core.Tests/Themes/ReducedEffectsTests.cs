using Veneer.Core.Themes;

namespace Veneer.Core.Tests.Themes;

public class ReducedEffectsTests
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, true, true)]
    public void Reduced_when_transparency_is_off_or_energy_saver_is_on(bool transparency, bool saver, bool expected) =>
        Assert.Equal(expected, ReducedEffects.Active(transparency, saver));

    [Fact]
    public void Glass_surfaces_get_their_tint_as_a_solid_color()
    {
        using var t = new TempTheme();
        var theme = ReducedEffects.Apply(ThemeLoader.Load(t.Root).Theme!);

        var bar = theme.Surfaces.OfType<BarSurface>().Single().Style;
        var dock = theme.Surfaces.OfType<DockSurface>().Single().Style;
        Assert.False(bar.Blur);
        Assert.False(dock.Blur);
        Assert.Equal(new ThemeColor(0xFF, 0xF5, 0xF5, 0xF5), bar.Background);
        Assert.Equal(new ThemeColor(0xFF, 0xFF, 0xFF, 0xFF), dock.Background);
        Assert.Equal(new ThemeColor(0x33, 0xFF, 0xFF, 0xFF), dock.Border); // only the background changes
    }

    [Fact]
    public void Surfaces_without_blur_keep_their_look()
    {
        using var t = new TempTheme(TempTheme.Sample.Replace("\"blur\": true", "\"blur\": false"), TempTheme.SampleAssets);
        var loaded = ThemeLoader.Load(t.Root).Theme!;
        var theme = ReducedEffects.Apply(loaded);

        Assert.Equal(loaded.Surfaces[0].Style, theme.Surfaces[0].Style);
        Assert.Equal(loaded.Surfaces[1].Style, theme.Surfaces[1].Style);
    }

    [Fact]
    public void Blurred_menus_get_a_solid_background()
    {
        using var t = new TempTheme(TempTheme.Sample.Replace("\"iconFolder\"", "\"menu\": { \"background\": \"#80101010\", \"blur\": true }, \"iconFolder\""));
        var theme = ReducedEffects.Apply(ThemeLoader.Load(t.Root).Theme!);

        Assert.False(theme.Menu!.Blur);
        Assert.Equal(new ThemeColor(0xFF, 0x10, 0x10, 0x10), theme.Menu.Background);
    }
}
