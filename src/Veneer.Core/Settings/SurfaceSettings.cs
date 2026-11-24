using System.Text.Json.Nodes;
using Veneer.Core.Themes;

namespace Veneer.Core.Settings;

public enum SettingKind
{
    Choice,
    Number,
    Toggle,
}

/// <summary>
/// One value of a surface that the settings window lets the user change. <see cref="Key"/> is the theme key inside the
/// surface (dotted for nested values), <see cref="Value"/> the current merged value (string, double or bool), and
/// <see cref="Origin"/> where it came from (null: Veneer's default). <see cref="WriteWith"/> are values that must be
/// written together with this one to keep their current meaning.
/// </summary>
public sealed record SurfaceSetting(
    string SurfaceId,
    string Key,
    SettingKind Kind,
    object Value,
    ThemeLayer? Origin,
    double Min = 0,
    double Max = 0,
    double Step = 1,
    IReadOnlyList<string>? Choices = null,
    IReadOnlyList<SurfaceSetting>? WriteWith = null)
{
    /// <summary>The user changed this value: "reset to theme value" has something to remove.</summary>
    public bool IsYours => Origin == ThemeLayer.User;

    /// <summary>The value as it goes into the overrides file. Numbers are clamped to the range and to the step.</summary>
    public JsonNode ToJson(object value) => Kind switch
    {
        SettingKind.Toggle => JsonValue.Create(Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture)),
        SettingKind.Number => Number(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture)),
        _ => JsonValue.Create(Choices!.Contains((string)value) ? (string)value : throw new ArgumentException($"\"{value}\" is not one of the choices", nameof(value))),
    };

    JsonNode Number(double value)
    {
        double clamped = Math.Clamp(Math.Round(value / Step) * Step, Min, Max);
        // Whole-number settings must be written as integers: the theme loader rejects 48.0000001 for iconSize.
        return Step >= 1 ? JsonValue.Create((int)Math.Round(clamped)) : JsonValue.Create(Math.Round(clamped, 2));
    }
}

/// <summary>
/// The main values of each surface that the settings window shows (an editor for every key belongs to the theme
/// editor, later). Ranges match the theme loader's checks in ThemeBinder.
/// </summary>
public static class SurfaceSettings
{
    static readonly string[] BarEdges = ["top", "bottom", "left", "right"];
    static readonly string[] DockEdges = ["bottom", "left", "right"];

    public static IReadOnlyList<SurfaceSetting> For(Theme theme, Surface surface)
    {
        string id = surface.Id;
        ThemeLayer? Origin(string key) => theme.OriginOf($"surfaces[id={id}].{key}");
        SurfaceSetting Choice(string key, string value, string[] choices) => new(id, key, SettingKind.Choice, value, Origin(key), Choices: choices);
        SurfaceSetting Number(string key, double value, double min, double max, double step = 1) => new(id, key, SettingKind.Number, value, Origin(key), min, max, step);
        SurfaceSetting Toggle(string key, bool value) => new(id, key, SettingKind.Toggle, value, Origin(key));

        string edge = surface.Edge.ToString().ToLowerInvariant();
        return surface switch
        {
            BarSurface bar =>
            [
                Choice("edge", edge, BarEdges),
                Number("thickness", bar.Thickness, 8, 200),
                Toggle("reserveSpace", bar.ReserveSpace),
            ],
            DockSurface dock => DockSettings(dock, Choice, Number, Toggle, edge, Origin),
            _ => [],
        };
    }

    static IReadOnlyList<SurfaceSetting> DockSettings(DockSurface dock, Func<string, string, string[], SurfaceSetting> choice,
        Func<string, double, double, double, double, SurfaceSetting> number, Func<string, bool, SurfaceSetting> toggle,
        string edge, Func<string, ThemeLayer?> origin)
    {
        var enabled = toggle("magnification.enabled", dock.Magnification.Enabled);
        var scale = number("magnification.maxScale", dock.Magnification.MaxScale, 1, 3, 0.1);
        // A theme without a "magnification" object has it off; writing only maxScale would create the object, and an
        // object without "enabled" means on. So the scale also writes the current on/off state.
        if (origin("magnification.enabled") is null)
            scale = scale with { WriteWith = [enabled] };
        return
        [
            choice("edge", edge, DockEdges),
            number("iconSize", dock.IconSize, 16, 256, 1),
            enabled,
            scale,
            toggle("autoHide", dock.AutoHide),
        ];
    }
}
