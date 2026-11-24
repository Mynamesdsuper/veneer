using System.Text;
using System.Text.RegularExpressions;
using Veneer.Core.Themes;

namespace Veneer.Core;

/// <summary>What the diagnostics report describes. Paths and names in it are scrubbed by <see cref="DiagnosticsReport"/>.</summary>
public sealed record ReportInfo(
    string Version,
    string Windows,
    IReadOnlyList<string> Monitors,
    string? Profile,
    string ThemeId,
    string? ThemeFolder,
    IReadOnlyList<Diagnostic> ThemeDiagnostics,
    IReadOnlyList<string> LogLines);

/// <summary>
/// A plain-text report to paste in an issue: version, Windows build, monitors, theme and its load problems, and the
/// last log lines. It never shows personal paths: the user folder, the user name and the PC name are replaced.
/// The report is in English whatever the UI language, so anyone can read the issue.
/// </summary>
public static class DiagnosticsReport
{
    public static string Build(ReportInfo info, string userFolder, string userName, string machineName)
    {
        var text = new StringBuilder();
        text.AppendLine($"Veneer {info.Version}");
        text.AppendLine($"Windows: {info.Windows}");
        foreach (string monitor in info.Monitors)
            text.AppendLine($"Monitor: {monitor}");
        text.AppendLine($"Profile: {info.Profile ?? "(off: --theme)"}");
        text.AppendLine($"Theme: {info.ThemeId} ({info.ThemeFolder ?? "built-in"})");
        text.AppendLine();
        text.AppendLine(info.ThemeDiagnostics.Count == 0 ? "Theme load: no problems" : "Theme load:");
        foreach (var d in info.ThemeDiagnostics)
            text.AppendLine($"  {d}");
        text.AppendLine();
        text.AppendLine($"Last {info.LogLines.Count} log lines:");
        foreach (string line in info.LogLines)
            text.AppendLine($"  {line}");
        return Scrub(text.ToString(), userFolder, userName, machineName);
    }

    /// <summary>Replaces the user folder with <c>%USERPROFILE%</c>, and the user and PC names with placeholders.</summary>
    public static string Scrub(string text, string userFolder, string userName, string machineName)
    {
        if (userFolder.Length > 0)
        {
            string folder = Path.TrimEndingDirectorySeparator(userFolder);
            text = Regex.Replace(text, Regex.Escape(folder), "%USERPROFILE%", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, Regex.Escape(folder.Replace('\\', '/')), "%USERPROFILE%", RegexOptions.IgnoreCase);
        }

        // The 8.3 short form of a user folder (C:\Users\DOMENI~1, often in temp paths) holds most of the name.
        text = Regex.Replace(text, @"[A-Za-z]:\\Users\\[^\\/\s]*~\d+", "%USERPROFILE%", RegexOptions.IgnoreCase);

        // Whole words only: a user called "app" must not turn "Veneer.App" into "Veneer.<user>". Too-short names
        // would match ordinary words; the folder above already covers the paths that hold them. The PC name goes first:
        // it often contains the user name ("MARIO-PC").
        foreach (var (name, placeholder) in new[] { (machineName, "<pc>"), (userName, "<user>") })
        {
            if (name.Length >= 3)
                text = Regex.Replace(text, $@"(?<![\w.]){Regex.Escape(name)}(?![\w])", placeholder, RegexOptions.IgnoreCase);
        }

        return text;
    }
}
