using System.Text.Json.Nodes;

namespace Veneer.Core.Themes;

/// <summary>Which layer set a value: the theme itself, the selected variant, or the user's overrides.</summary>
public enum ThemeLayer
{
    Theme,
    Variant,
    User,
}

public enum Edge
{
    Top,
    Bottom,
    Left,
    Right,
}

public enum Appearance
{
    Light,
    Dark,
    System,
}

public enum IndicatorShape
{
    Dot,
    Line,
}

/// <summary>
/// A fully merged (theme → variant → user overrides), token-resolved and validated theme.
/// Asset paths are absolute and guaranteed to be inside <see cref="RootDirectory"/>.
/// </summary>
public sealed record Theme(
    string Id,
    string Name,
    string? Author,
    string? Version,
    string? License,
    string RootDirectory,
    WindowsSettings Windows,
    IReadOnlyDictionary<string, string> Fonts,
    IReadOnlyList<Surface> Surfaces,
    IReadOnlyDictionary<string, string> IconOverrides,
    string? IconFolder,
    IReadOnlyDictionary<string, ThemeLayer> Origins)
{
    /// <summary>Where a value came from, by path (e.g. <c>surfaces[id=dock].iconSize</c>). Null when the value is a default.</summary>
    public ThemeLayer? OriginOf(string path) => Origins.TryGetValue(path, out var layer) ? layer : null;
}

/// <summary>Windows settings a theme may change. Each one is optional and the user can turn it off.</summary>
public sealed record WindowsSettings(string? Wallpaper, Appearance? Appearance, ThemeColor? AccentColor);

/// <summary>
/// Text settings. <see cref="Font"/> is a key of the theme's <c>fonts</c> map or an installed font family name.
/// <see cref="FontWeight"/> is 100–900 (400 regular, 700 bold). Null means "inherit".
/// </summary>
public sealed record TextStyle(string? Font, double? FontSize, int? FontWeight);

/// <summary>
/// <see cref="Padding"/> is the inner space at both ends of a bar; <see cref="Spacing"/> is the gap between modules.
/// Both are in device-independent pixels (1/96 inch).
/// </summary>
public sealed record SurfaceStyle(
    ThemeColor? Background,
    ThemeColor? Foreground,
    ThemeColor? Border,
    bool Blur,
    TextStyle Text,
    double Padding,
    double Spacing);

/// <summary>A module's own look. Null values inherit from the surface.</summary>
public sealed record ModuleStyle(ThemeColor? Foreground, TextStyle Text, double? Padding);

public abstract record Surface(string Id, Edge Edge, string Monitor, SurfaceStyle Style);

/// <summary>
/// <see cref="Thickness"/> is the size across the edge in device-independent pixels: height for top/bottom bars,
/// width for left/right.
/// </summary>
public sealed record BarSurface(
    string Id,
    Edge Edge,
    string Monitor,
    SurfaceStyle Style,
    int Thickness,
    bool ReserveSpace,
    IReadOnlyList<ModuleSpec> Start,
    IReadOnlyList<ModuleSpec> Center,
    IReadOnlyList<ModuleSpec> End) : Surface(Id, Edge, Monitor, Style);

public sealed record DockSurface(
    string Id,
    Edge Edge,
    string Monitor,
    SurfaceStyle Style,
    int IconSize,
    Magnification Magnification,
    double CornerRadius,
    double Padding,
    RunningIndicator RunningIndicator,
    bool AutoHide,
    IReadOnlyList<string> DefaultPinned,
    double Margin,
    bool ReserveSpace,
    bool SeparateRunning) : Surface(Id, Edge, Monitor, Style);

public sealed record Magnification(bool Enabled, double MaxScale, int Radius);

public sealed record RunningIndicator(IndicatorShape Shape, ThemeColor? Color, double Size);

/// <summary>
/// A module in a bar. <see cref="Options"/> holds the module's own settings (already token-resolved and checked).
/// <see cref="Icon"/> is the resolved absolute path of an <c>icon</c> option; <see cref="StateIcons"/> maps a
/// state name (e.g. <c>muted</c>, <c>wifi</c>) to the resolved path of the theme's image for it.
/// </summary>
public sealed record ModuleSpec(
    string Module,
    string Path,
    string? Icon,
    IReadOnlyDictionary<string, string> StateIcons,
    ModuleStyle Style,
    JsonObject Options);
