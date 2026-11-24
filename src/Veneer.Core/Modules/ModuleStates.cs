namespace Veneer.Core.Modules;

/// <summary>
/// Pure logic behind the status modules: which state to show for a reading. Themes can give an image per state
/// (module option <c>icons</c>); the state names are listed in <see cref="IconStates"/>.
/// </summary>
public static class ModuleStates
{
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> IconStates = new Dictionary<string, IReadOnlySet<string>>
    {
        ["volume"] = new HashSet<string> { "muted", "off", "low", "medium", "high" },
        ["network"] = new HashSet<string> { "wifi", "ethernet", "offline" },
        ["battery"] = new HashSet<string> { "empty", "low", "medium", "high", "full", "charging" },
    };

    /// <param name="level">Master volume, 0–1.</param>
    public static string Volume(float level, bool muted) => muted ? "muted" : level switch
    {
        < 0.005f => "off",
        < 0.34f => "low",
        < 0.67f => "medium",
        _ => "high",
    };

    public static string Battery(int percent, bool charging) => charging ? "charging" : percent switch
    {
        <= 5 => "empty",
        <= 25 => "low",
        <= 60 => "medium",
        <= 90 => "high",
        _ => "full",
    };

    public readonly record struct Adapter(bool Up, bool Wireless, bool Loopback, bool HasGateway);

    /// <summary>Wired wins over wireless, as Windows routes traffic that way. Only adapters with a gateway count as connected.</summary>
    public static string Network(IEnumerable<Adapter> adapters)
    {
        var connected = adapters.Where(a => a.Up && !a.Loopback && a.HasGateway).ToList();
        if (connected.Any(a => !a.Wireless))
            return "ethernet";
        return connected.Count > 0 ? "wifi" : "offline";
    }

    static readonly HashSet<string> DesktopClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    /// <summary>
    /// The name shown by <c>active-app</c>. Order: the desktop name for the desktop and taskbar; the theme's
    /// <paramref name="names"/> by exe file name; the window title for Store apps (hosted by ApplicationFrameHost);
    /// the exe's file description; the exe file name; the window title.
    /// </summary>
    public static string ActiveAppName(string? className, string? exePath, string? description, string? title,
        string desktopName, IReadOnlyDictionary<string, string> names)
    {
        if (className is not null && DesktopClasses.Contains(className))
            return desktopName;
        string? exe = exePath is null ? null : Path.GetFileName(exePath);
        if (exe is not null && names.TryGetValue(exe.ToLowerInvariant(), out string? custom))
            return custom;
        if (string.Equals(exe, "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(title))
            return title;
        if (!string.IsNullOrWhiteSpace(description))
        {
            // Some apps (Windows 11 Notepad) put the exe name in the description.
            string d = description.Trim();
            return d.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? d[..^4] : d;
        }
        if (exe is not null)
            return Path.GetFileNameWithoutExtension(exe);
        return title ?? "";
    }
}
