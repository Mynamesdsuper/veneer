using System.Text.Json;

namespace Veneer.Core.Settings;

/// <summary>A registry value as it was: kind (<c>DWord</c>, <c>QWord</c>, <c>Binary</c>, <c>String</c>) and data, or missing.</summary>
public sealed record SavedValue(string Key, string Name, string? Kind, long? Number, string? Base64, string? Text)
{
    public bool Existed => Kind is not null;
}

public sealed record MonitorWallpaper(string MonitorId, string Path);

public sealed record WallpaperSnapshot(List<MonitorWallpaper> Monitors, int Position);

/// <summary><see cref="PreferenceStart"/>/<see cref="PreferenceAccent"/>: the uxtheme user color preference (COLORREF).</summary>
public sealed record AccentSnapshot(uint PreferenceStart, uint PreferenceAccent, List<SavedValue> Values);

public sealed record AppearanceSnapshot(List<SavedValue> Values);

/// <summary>
/// The Windows settings as they were before Veneer first changed them. Each part is captured right before its
/// setting is first changed and never overwritten while the file exists, so it always holds the user's own values.
/// "Restore original Windows settings" writes them back and deletes the file.
/// </summary>
public sealed record WindowsSnapshot(WallpaperSnapshot? Wallpaper, AccentSnapshot? Accent, AppearanceSnapshot? Appearance)
{
    public const string FileName = "windows-settings.json";

    public static WindowsSnapshot Empty { get; } = new(null, null, null);

    public bool IsEmpty => Wallpaper is null && Accent is null && Appearance is null;

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string Serialize() => JsonSerializer.Serialize(this, Options);

    /// <summary>The snapshot, or null when the text is missing or damaged (the caller must then not overwrite the file).</summary>
    public static WindowsSnapshot? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try
        {
            return JsonSerializer.Deserialize<WindowsSnapshot>(text, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
