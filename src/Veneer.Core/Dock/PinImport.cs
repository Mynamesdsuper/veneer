using System.Text.RegularExpressions;

namespace Veneer.Core.Dock;

/// <summary>Turns the Windows taskbar's pinned apps into dock entries (the first-run import).</summary>
public static partial class PinImport
{
    // Desktop apps whose taskbar pin has only an AppUserModelID (the shortcut points at a shell folder, not an exe),
    // and the exe their windows run, which is what the dock matches them by.
    static readonly Dictionary<string, string> ShellApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.Windows.Explorer"] = "explorer.exe",
    };

    /// <summary>
    /// The dock entry for one taskbar pin, or null when the dock cannot use it. Store apps by AppUserModelID (their
    /// windows carry it); desktop apps by the full path of their exe, because their windows often do not carry the
    /// id their shortcut sets (Discord, Spotify); Squirrel apps (Discord) by the stub exe next to <c>Update.exe</c>,
    /// which survives updates.
    /// </summary>
    public static string? EntryFor(string? aumid, string? target, string? arguments)
    {
        if (aumid is not null && DockModel.IsAumid(aumid))
            return aumid;
        if (target is not null && Path.IsPathFullyQualified(target) && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(Path.GetFileName(target), "Update.exe", StringComparison.OrdinalIgnoreCase))
                return target;
            // Squirrel: "Update.exe --processStart Discord.exe" starts the newest app-<version>\Discord.exe. The bare
            // updater is no app, and the name may not leave the updater's folder.
            var match = ProcessStart().Match(arguments ?? "");
            string? stub = match.Success ? Path.Combine(Path.GetDirectoryName(target)!, match.Groups[1].Value) : null;
            return stub is not null && File.Exists(stub) ? stub : null;
        }

        return aumid is not null ? ShellApps.GetValueOrDefault(aumid) : null;
    }

    /// <summary>The entries in taskbar order, without the ones the dock cannot use and without duplicates.</summary>
    public static List<string> Entries(IEnumerable<(string? Aumid, string? Target, string? Arguments)> pins) =>
        pins.Select(p => EntryFor(p.Aumid, p.Target, p.Arguments))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    [GeneratedRegex("""--processStart\s+"?([^"\\/:*?<>|]+?\.exe)"?(\s|$)""", RegexOptions.IgnoreCase)]
    private static partial Regex ProcessStart();
}
