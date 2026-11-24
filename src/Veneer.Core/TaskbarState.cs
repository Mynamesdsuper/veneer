using System.Text.Json;

namespace Veneer.Core;

/// <summary>
/// The taskbar snapshot Veneer saves before hiding the taskbar, and how to read it back. The file is the "dirty"
/// flag: it exists exactly while the taskbar is hidden by Veneer. Pure logic, so the restore rules are tested.
/// </summary>
public static class TaskbarState
{
    /// <summary>The <c>ABS_AUTOHIDE</c> bit of the shell's app bar state.</summary>
    public const uint AutoHide = 1;

    public static string Serialize(uint appBarState) => JsonSerializer.Serialize(new { AppBarState = appBarState });

    /// <summary>The saved app bar state, or null when the file is empty, damaged or from an unknown format.</summary>
    public static uint? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("AppBarState", out var v)
                && v.ValueKind == JsonValueKind.Number // TryGetUInt32 throws on a string instead of returning false
                && v.TryGetUInt32(out uint state)
                ? state
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The state to put back. When the snapshot cannot be read, the safe choice is the current state without
    /// auto-hide (the Windows default): a visible taskbar is always better than one stuck on auto-hide.
    /// </summary>
    public static uint RestoreTarget(uint? saved, uint current) => saved ?? (current & ~AutoHide);

    /// <summary>True when the shell reports the auto-hide setting we asked for.</summary>
    public static bool IsRestored(uint target, uint actual) => (target & AutoHide) == (actual & AutoHide);
}
