using System.Text.Json;
using System.Text.Json.Serialization;
using Veneer.Core.Themes;

namespace Veneer.Core.Settings;

/// <summary>Where a Windows setting comes from: the theme, or Windows itself (Veneer leaves it alone).</summary>
public enum SettingSource
{
    Theme,
    System,
}

/// <summary>Light or dark mode: as the theme says, always light, always dark, or follow Windows.</summary>
public enum AppearanceChoice
{
    Theme,
    Light,
    Dark,
    System,
}

/// <summary>
/// The user's choices for the three Windows settings a theme may change. All start at "from the theme" (author's
/// decision, 2026-11-24). Stored in the profile as <c>windows.json</c>; the settings window edits it in Phase 6.
/// </summary>
public sealed record WindowsPreferences(
    SettingSource Wallpaper = SettingSource.Theme,
    SettingSource Accent = SettingSource.Theme,
    AppearanceChoice Appearance = AppearanceChoice.Theme)
{
    public const string FileName = "windows.json";

    static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string Serialize() => JsonSerializer.Serialize(this, Options);

    /// <summary>
    /// Reads the file leniently: a missing file gives the defaults; each unreadable value falls back to its default
    /// with a warning, so a typo never changes the other choices.
    /// </summary>
    public static WindowsPreferences Parse(string? json, List<string> warnings)
    {
        var result = new WindowsPreferences();
        if (string.IsNullOrWhiteSpace(json))
            return result;
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            root = doc.RootElement.Clone();
        }
        catch (JsonException e)
        {
            warnings.Add($"{FileName}: invalid JSON ({e.Message}); using the defaults");
            return result;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            warnings.Add($"{FileName}: must be an object; using the defaults");
            return result;
        }

        return new WindowsPreferences(
            Read(root, "wallpaper", result.Wallpaper, warnings),
            Read(root, "accent", result.Accent, warnings),
            Read(root, "appearance", result.Appearance, warnings));
    }

    static T Read<T>(JsonElement root, string name, T fallback, List<string> warnings) where T : struct, Enum
    {
        if (!root.TryGetProperty(name, out var value))
            return fallback;
        if (value.ValueKind == JsonValueKind.String
            && Enum.TryParse(value.GetString(), ignoreCase: true, out T parsed)
            && Enum.IsDefined(parsed)
            && !char.IsDigit(value.GetString()![0]))
            return parsed;
        string allowed = string.Join(", ", Enum.GetNames<T>().Select(n => n.ToLowerInvariant()));
        warnings.Add($"{FileName}: \"{name}\" must be one of {allowed}; using \"{fallback.ToString().ToLowerInvariant()}\"");
        return fallback;
    }
}

/// <summary>What Veneer should do to Windows for a theme: null values mean "leave it as it is".</summary>
public sealed record WindowsPlan(string? Wallpaper, ThemeColor? Accent, bool? Dark);

public static class WindowsSettingsPlan
{
    /// <summary>The variant used for dark mode; light mode uses the theme without a variant (or a "light" one).</summary>
    public const string DarkVariant = "dark";

    public const string LightVariant = "light";

    /// <summary>The dark mode Veneer should set, or null to follow Windows.</summary>
    public static bool? DesiredDark(Appearance? themeAppearance, AppearanceChoice choice) => choice switch
    {
        AppearanceChoice.Light => false,
        AppearanceChoice.Dark => true,
        AppearanceChoice.System => null,
        _ => themeAppearance switch
        {
            Appearance.Light => false,
            Appearance.Dark => true,
            _ => null,
        },
    };

    /// <summary>The theme variant to load: dark or light, from the mode Veneer sets or, when it follows Windows, Windows' own mode.</summary>
    public static string Variant(bool? desiredDark, bool windowsIsDark) => desiredDark ?? windowsIsDark ? DarkVariant : LightVariant;

    public static WindowsPlan For(Theme theme, WindowsPreferences preferences) => new(
        preferences.Wallpaper == SettingSource.Theme ? theme.Windows.Wallpaper : null,
        preferences.Accent == SettingSource.Theme ? theme.Windows.AccentColor : null,
        DesiredDark(theme.Windows.Appearance, preferences.Appearance));
}
