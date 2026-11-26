namespace Veneer.Core.Themes;

/// <summary>
/// The cheap look used while costly effects are off: no blur anywhere, and every glass surface gets its tint as a
/// solid color (like Windows' own acrylic fallback). Surfaces without blur keep their look.
/// </summary>
public static class ReducedEffects
{
    /// <summary>
    /// Effects are reduced when Windows "Transparency effects" is off or energy saver is on. A full-screen app needs
    /// no rule: the surfaces are hidden then.
    /// </summary>
    public static bool Active(bool transparencyEffects, bool energySaver) => !transparencyEffects || energySaver;

    public static Theme Apply(Theme theme) => theme with
    {
        Surfaces = theme.Surfaces.Select(Surface).ToList(),
        Menu = Popup(theme.Menu),
    };

    static Surface Surface(Surface surface) => surface switch
    {
        BarSurface bar => bar with { Style = Style(bar.Style) },
        DockSurface dock => dock with { Style = Style(dock.Style), Label = Popup(dock.Label) },
        _ => surface,
    };

    static SurfaceStyle Style(SurfaceStyle style) =>
        style.Blur ? style with { Blur = false, Background = Solid(style.Background) } : style;

    static PopupStyle? Popup(PopupStyle? style) =>
        style is { Blur: true } ? style with { Blur = false, Background = Solid(style.Background) } : style;

    // shortcut: a glass surface without a background stays see-through; add a theme fallback color if a theme needs one.
    static ThemeColor? Solid(ThemeColor? color) => color is { } c ? c with { A = 255 } : null;
}
