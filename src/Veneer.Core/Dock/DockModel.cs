using Veneer.Core.Layout;
using Veneer.Core.Themes;

namespace Veneer.Core.Dock;

/// <summary>A running app as the dock sees it: its windows grouped by AppUserModelID, or by exe when it has none.</summary>
public sealed record RunningApp(string Key, string? Aumid, string? ExePath, IReadOnlyList<nint> Windows);

/// <summary>One dock cell: a pinned entry, a running app, both, or a separator.</summary>
public sealed record DockItem(string Key, string? Pinned, RunningApp? Running)
{
    public bool IsRunning => Running is not null;

    public bool IsSeparator => Pinned == DockModel.Separator;
}

public static class DockModel
{
    /// <summary>A pinned-list entry that draws a separator line.</summary>
    public const string Separator = "---";

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
        // Fully qualified only: "C:foo.exe" is relative to a drive's current folder and is treated as a file name.
        if (!Path.IsPathFullyQualified(pinned))
            return string.Equals(pinned, Path.GetFileName(app.ExePath), StringComparison.OrdinalIgnoreCase);
        string full = Path.GetFullPath(pinned);
        return string.Equals(full, app.ExePath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(full, SquirrelStub(app.ExePath), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// For an exe a Squirrel installer runs from a versioned folder (<c>...\Discord\app-1.0.9269\Discord.exe</c>):
    /// the stub with the same name one folder up (<c>...\Discord\Discord.exe</c>), which starts the newest version.
    /// Only the path is computed; null for any other exe.
    /// </summary>
    public static string? SquirrelStub(string exePath)
    {
        string? folder = Path.GetDirectoryName(exePath);
        string? parent = folder is null ? null : Path.GetDirectoryName(folder);
        return parent is not null && Path.GetFileName(folder)!.StartsWith("app-", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(parent, Path.GetFileName(exePath))
            : null;
    }

    /// <summary>
    /// Pinned entries first, in their order, each joined with its running app; then the other running apps in the
    /// order given, after a separator when <paramref name="separateRunning"/> is set.
    /// </summary>
    public static List<DockItem> Build(IReadOnlyList<string> pinned, IReadOnlyList<RunningApp> running, bool separateRunning = false)
    {
        var items = new List<DockItem>();
        var used = new HashSet<string>();
        for (int i = 0; i < pinned.Count; i++)
        {
            string entry = pinned[i];
            if (entry == Separator)
            {
                items.Add(new DockItem($"sep:{i}", Separator, null));
                continue;
            }

            var app = running.FirstOrDefault(a => !used.Contains(a.Key) && Matches(entry, a));
            if (app is not null)
                used.Add(app.Key);
            items.Add(new DockItem(app?.Key ?? "pin:" + entry.ToLowerInvariant(), entry, app));
        }


        var others = running.Where(a => !used.Contains(a.Key)).ToList();
        if (separateRunning && others.Count > 0 && items.Count > 0 && !items[^1].IsSeparator)
            items.Add(new DockItem("sep:running", Separator, null));
        items.AddRange(others.Select(a => new DockItem(a.Key, null, a)));
        return items;
    }

    /// <summary>
    /// The pinned-list entry for a running app: its AppUserModelID when it is a packaged app's, else its exe path (a
    /// Squirrel app's stub when there is one, so the pin survives the app's updates). Ids without "!" (Zen, Steam)
    /// would be read back as exe names, so they are not used.
    /// </summary>
    public static string? EntryFor(RunningApp app) =>
        app.Aumid is { } id && IsAumid(id) ? id
        : app.ExePath is { } exe && SquirrelStub(exe) is { } stub && File.Exists(stub) ? stub
        : app.ExePath;

    /// <summary>Pins <paramref name="entry"/> at <paramref name="index"/> (the end when out of range). No duplicates.</summary>
    public static List<string> Pin(IReadOnlyList<string> pinned, string entry, int index = int.MaxValue)
    {
        var list = pinned.Where(p => !string.Equals(p, entry, StringComparison.OrdinalIgnoreCase)).ToList();
        list.Insert(Math.Clamp(index, 0, list.Count), entry);
        return list;
    }

    public static List<string> Unpin(IReadOnlyList<string> pinned, string entry) =>
        pinned.Where(p => !string.Equals(p, entry, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// While dragging the pinned cell <paramref name="from"/>: the pinned index it would land on, from the cursor
    /// position <paramref name="along"/> in the base (unmagnified) row. Cells are <paramref name="sizes"/> apart by
    /// <paramref name="spacing"/>; only the first <paramref name="pinnedCount"/> cells are pinned.
    /// </summary>
    public static int DropIndex(IReadOnlyList<double> sizes, double spacing, int pinnedCount, double along, int from)
    {
        if (pinnedCount <= 0)
            return 0;
        double pos = 0;
        for (int i = 0; i < pinnedCount && i < sizes.Count; i++)
        {
            if (along < pos + sizes[i] / 2)
                return Math.Clamp(i > from ? i - 1 : i, 0, pinnedCount - 1);
            pos += sizes[i] + spacing;
        }

        return pinnedCount - 1;
    }

    /// <summary>Display order of <paramref name="count"/> cells while cell <paramref name="from"/> is dragged to <paramref name="to"/>.</summary>
    public static int[] DragOrder(int count, int from, int to)
    {
        var order = Enumerable.Range(0, count).ToList();
        if (from < 0 || from >= count)
            return [.. order];
        order.RemoveAt(from);
        order.Insert(Math.Clamp(to, 0, order.Count), from);
        return [.. order];
    }

    /// <summary>Moves the pinned entry at <paramref name="from"/> so it ends up at index <paramref name="to"/>.</summary>
    public static List<string> Move(IReadOnlyList<string> pinned, int from, int to)
    {
        var list = pinned.ToList();
        if (from < 0 || from >= list.Count)
            return list;
        string entry = list[from];
        list.RemoveAt(from);
        list.Insert(Math.Clamp(to, 0, list.Count), entry);
        return list;
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
