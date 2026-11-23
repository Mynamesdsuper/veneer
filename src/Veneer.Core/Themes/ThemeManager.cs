namespace Veneer.Core.Themes;

/// <summary>
/// Holds the active theme and reloads it when its files or the overrides file change.
/// A failed load never replaces the current theme: the previous one (or <see cref="BuiltInTheme"/>) stays active.
/// </summary>
public sealed class ThemeManager : IDisposable
{
    readonly object gate = new();
    readonly TimeSpan debounce;
    readonly Timer timer;
    readonly List<FileSystemWatcher> watchers = [];
    string? themeDirectory;
    string? variant;
    string? overridesPath;

    public ThemeManager(TimeSpan? debounce = null)
    {
        this.debounce = debounce ?? TimeSpan.FromMilliseconds(300);
        timer = new Timer(_ => Reload());
    }

    public Theme Current { get; private set; } = BuiltInTheme.Theme;

    public ThemeLoadResult? LastResult { get; private set; }

    /// <summary>
    /// Raised after every load attempt, successful or not. Automatic reloads raise it on a thread-pool thread,
    /// so UI code must marshal to its own thread.
    /// </summary>
    public event Action<ThemeLoadResult>? Loaded;

    /// <summary>Loads a theme (or the same theme with another variant or overrides file) and starts watching it.</summary>
    public ThemeLoadResult Apply(string themeDirectory, string? variant = null, string? overridesPath = null)
    {
        lock (gate)
        {
            this.themeDirectory = themeDirectory;
            this.variant = variant;
            this.overridesPath = overridesPath;
            Watch();
        }

        return Reload()!; // never null: themeDirectory was just set
    }

    /// <summary>Loads the current theme again. Returns null when no theme has been applied yet.</summary>
    public ThemeLoadResult? Reload()
    {
        ThemeLoadResult result;
        lock (gate)
        {
            if (themeDirectory is null)
                return null;
            result = ThemeLoader.Load(themeDirectory, variant, overridesPath);
            LastResult = result;
            if (result.Theme is not null)
                Current = result.Theme;
        }

        Loaded?.Invoke(result);
        return result;
    }

    void Watch()
    {
        foreach (var w in watchers)
            w.Dispose();
        watchers.Clear();

        if (Directory.Exists(themeDirectory))
            watchers.Add(NewWatcher(themeDirectory, "*", subdirectories: true));
        // shortcut: the overrides folder must exist when Apply is called; the settings layer creates it before that.
        if (overridesPath is not null && Path.GetDirectoryName(Path.GetFullPath(overridesPath)) is { } dir && Directory.Exists(dir))
            watchers.Add(NewWatcher(dir, Path.GetFileName(overridesPath), subdirectories: false));
    }

    FileSystemWatcher NewWatcher(string path, string filter, bool subdirectories)
    {
        var w = new FileSystemWatcher(path, filter)
        {
            IncludeSubdirectories = subdirectories,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        // Editors save in several steps: wait until the files are quiet before reloading.
        FileSystemEventHandler changed = (_, _) => timer.Change(debounce, Timeout.InfiniteTimeSpan);
        w.Changed += changed;
        w.Created += changed;
        w.Deleted += changed;
        w.Renamed += (_, _) => timer.Change(debounce, Timeout.InfiniteTimeSpan);
        w.EnableRaisingEvents = true;
        return w;
    }

    public void Dispose()
    {
        lock (gate)
        {
            foreach (var w in watchers)
                w.Dispose();
            watchers.Clear();
        }

        timer.Dispose();
    }
}
