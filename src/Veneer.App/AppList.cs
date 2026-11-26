using System.Text.Json.Nodes;
using Veneer.Core;
using Veneer.Core.Dock;
using Veneer.Interop;

namespace Veneer.App;

/// <summary>
/// The pinned and running apps of one surface (a dock, or the <c>apps</c> modules of a bar) and what the user can do
/// with them. <paramref name="pinnedChanged"/> runs whenever <see cref="Pinned"/> changes, so the surface redraws.
/// </summary>
sealed class AppList(string surfaceId, IReadOnlyList<string> pinned, AppWindowTracker tracker, string? overridesPath, bool separateRunning, Action pinnedChanged, IReadOnlyList<string>? extras = null)
{
    string signature = "";

    /// <summary>The pinned list in use: the theme's, with the user's changes from the overrides file.</summary>
    public List<string> Pinned { get; private set; } = pinned.ToList();

    public List<DockItem> Items { get; private set; } = [];

    /// <summary>Set when the surface closes (a rebuild): a menu or drag that outlives it must not write a stale list.</summary>
    public bool Closed { get; set; }

    /// <summary>Rebuilds <see cref="Items"/> from the current windows. True when the icons changed, not only the window handles.</summary>
    public bool Update(bool force = false)
    {
        var list = DockModel.Build(Pinned, Running(), separateRunning, extras);
        // The window count (up to 3) is in it too: one indicator per window needs a redraw when it changes.
        string sig = string.Join('|', list.Select(i => $"{i.Key}:{Math.Min(i.Running?.Windows.Count ?? 0, 3)}"));
        Items = list;
        if (sig == signature && !force)
            return false;
        signature = sig;
#if DEBUG
        Log.Write($"{surfaceId}: {sig}");
#endif
        return true;
    }

    // Running apps grouped by AUMID (or exe), in the order their first window appears; windows most recent first.
    List<RunningApp> Running()
    {
        if (DockWindow.Demo)
            return [];
        var groups = new List<(string Key, string? Aumid, string? Exe, List<nint> Windows)>();
        foreach (var w in tracker.Enumerate())
        {
            string key = DockModel.KeyFor(w.Aumid, w.ExePath);
            var group = groups.FirstOrDefault(g => g.Key == key);
            if (group.Key is null)
                groups.Add(group = (key, w.Aumid, w.ExePath, []));
            group.Windows.Add(w.Handle);
        }

        return groups
            .Select(g => new RunningApp(g.Key, g.Aumid, g.Exe, g.Windows.OrderByDescending(tracker.LastActive).ToList()))
            .ToList();
    }

    /// <summary>Brings the app's last used window to the front, or starts a pinned app. True when it started one.</summary>
    public bool Activate(DockItem item)
    {
        if (item.Running is { Windows.Count: > 0 } app)
        {
            // The window used most recently, looked up now (foreground changes do not trigger a refresh).
            Log.Write($"{surfaceId}: activate {item.Key}");
            WindowTools.Activate(app.Windows.MaxBy(tracker.LastActive));
            return false;
        }

        if (item.Pinned is not { } entry || item.IsSeparator)
            return false;
        Log.Write($"{surfaceId}: launch {entry}");
        SystemActions.Launch(entry, DockModel.IsAumid(entry));
        return true;
    }

    public void CloseWindows(DockItem item)
    {
        foreach (nint w in item.Running?.Windows ?? [])
            WindowTools.Close(w);
    }

    public void Pin(DockItem item)
    {
        if (item.Running is { } app && DockModel.EntryFor(app) is { } entry)
            Save(DockModel.Pin(Pinned, entry));
    }

    public void Unpin(DockItem item)
    {
        if (item.Pinned is { } entry)
            Save(DockModel.Unpin(Pinned, entry));
    }

    public void Move(int from, int to) => Save(DockModel.Move(Pinned, from, to));

    /// <summary>The theme reloaded with another pinned list (usually our own write). False when nothing changed.</summary>
    public bool SetPinned(IReadOnlyList<string> list)
    {
        if (Closed || list.SequenceEqual(Pinned))
            return false;
        Pinned = list.ToList();
        return true;
    }

    /// <summary>
    /// Shows the new list at once and writes it to the user's overrides, so it survives theme switches. The theme
    /// then reloads from the overrides file by itself.
    /// </summary>
    void Save(List<string> list)
    {
        if (Closed)
            return;
        var previous = Pinned;
        Pinned = list;
        pinnedChanged();
        if (overridesPath is null)
        {
            Log.Write($"{surfaceId}: pinned apps not saved (no theme folder in use)");
            return;
        }

        var json = new JsonArray(list.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray());
        if (!OverridesFile.TrySetSurfaceValue(overridesPath, surfaceId, "defaultPinned", json, out string? error))
        {
            // Show what is really saved rather than a change that would vanish at the next reload.
            Log.Write($"{surfaceId}: pinned apps not saved, change undone: {error}");
            Pinned = previous;
            pinnedChanged();
        }
    }
}
