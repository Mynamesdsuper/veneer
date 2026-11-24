using Veneer.Core.Layout;
using Veneer.Core.Themes;

namespace Veneer.Core.Dock;

/// <summary>A running app as the dock sees it: its windows grouped by AppUserModelID, or by exe when it has none.</summary>
public sealed record RunningApp(string Key, string? Aumid, string? ExePath, IReadOnlyList<nint> Windows);

/// <summary>One dock icon: a pinned entry, a running app, or both.</summary>
public sealed record DockItem(string Key, string? Pinned, RunningApp? Running)
{
    public bool IsRunning => Running is not null;
}

public static class DockModel
{
    // URIs whose app is a known Store app, so a pinned "ms-settings:" lights up when Settings is open.
    static readonly Dictionary<string, string> UriApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ms-settings:"] = "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel",
    };

    /// <summary>The AppUserModelID behind a known URI entry (e.g. <c>ms-settings:</c>), for its icon and name.</summary>
    public static string? AumidForUri(string entry) => UriApps.GetValueOrDefault(entry);

    public static string KeyFor(string? aumid, string? exePath) =>
        aumid is not null ? "aumid:" + aumid.ToLowerInvariant() : "exe:" + (exePath ?? "").ToLowerInvariant();

    /// <summary>True when an AppUserModelID string (e.g. <c>Microsoft.WindowsTerminal_8wekyb3d8bbwe!App</c>) is meant.</summary>
    public static bool IsAumid(string entry) => entry.Contains('!') && !entry.Contains('\\') && !entry.Contains('/');

    /// <summary>
    /// Whether a pinned entry is this running app. Entries: an AppUserModelID, a full exe path, an exe file name
    /// (<c>explorer.exe</c>) or a known URI (<c>ms-settings:</c>).
    /// </summary>
    public static bool Matches(string pinned, RunningApp app)
    {
        if (UriApps.TryGetValue(pinned, out string? uriAumid))
            return string.Equals(uriAumid, app.Aumid, StringComparison.OrdinalIgnoreCase);
        if (IsAumid(pinned))
            return string.Equals(pinned, app.Aumid, StringComparison.OrdinalIgnoreCase);
        if (app.ExePath is null)
            return false;
        return Path.IsPathRooted(pinned)
            ? string.Equals(Path.GetFullPath(pinned), app.ExePath, StringComparison.OrdinalIgnoreCase)
            : string.Equals(pinned, Path.GetFileName(app.ExePath), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Pinned entries first, in their order, each joined with its running app; then the other running apps in the order given.</summary>
    public static List<DockItem> Build(IReadOnlyList<string> pinned, IReadOnlyList<RunningApp> running)
    {
        var items = new List<DockItem>();
        var used = new HashSet<string>();
        foreach (string entry in pinned)
        {
            var app = running.FirstOrDefault(a => !used.Contains(a.Key) && Matches(entry, a));
            if (app is not null)
                used.Add(app.Key);
            items.Add(new DockItem(app?.Key ?? "pin:" + entry.ToLowerInvariant(), entry, app));
        }

        items.AddRange(running.Where(a => !used.Contains(a.Key)).Select(a => new DockItem(a.Key, null, a)));
        return items;
    }

    /// <summary>
    /// The dock's rectangle: centered along <paramref name="edge"/> of <paramref name="area"/> (the space the bars leave),
    /// <paramref name="margin"/> pixels away from that edge.
    /// </summary>
    public static PixelRect Place(PixelRect area, Edge edge, int length, int thickness, int margin)
    {
        length = Math.Min(length, edge is Edge.Left or Edge.Right ? area.Height : area.Width);
        int x = area.Left + (area.Width - length) / 2;
        int y = area.Top + (area.Height - length) / 2;
        return edge switch
        {
            Edge.Bottom => new PixelRect(x, area.Bottom - margin - thickness, x + length, area.Bottom - margin),
            Edge.Left => new PixelRect(area.Left + margin, y, area.Left + margin + thickness, y + length),
            Edge.Right => new PixelRect(area.Right - margin - thickness, y, area.Right - margin, y + length),
            _ => new PixelRect(x, area.Top + margin, x + length, area.Top + margin + thickness),
        };
    }
}
