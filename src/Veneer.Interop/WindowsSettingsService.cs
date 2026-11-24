using System.Runtime.InteropServices;
using Microsoft.Win32;
using Veneer.Core;
using Veneer.Core.Settings;
using Veneer.Core.Themes;

namespace Veneer.Interop;

/// <summary>
/// Applies a theme's wallpaper, accent color and light/dark mode, and gives the originals back. Before a setting is
/// changed for the first time its current values go into the snapshot file (see <see cref="WindowsSnapshot"/>).
/// Changes stay when Veneer exits; only <see cref="Restore"/> undoes them. Recipes: docs/decisions.md (Phase 0, 5).
/// </summary>
public sealed class WindowsSettingsService(string? snapshotPath = null)
{
    const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    const string Dwm = @"Software\Microsoft\Windows\DWM";
    const string ExplorerAccent = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";

    static readonly (string Key, string Name)[] AppearanceValues = [(Personalize, "AppsUseLightTheme"), (Personalize, "SystemUsesLightTheme")];

    static readonly (string Key, string Name)[] AccentValues =
    [
        (Dwm, "AccentColor"), (Dwm, "ColorizationColor"), (Dwm, "ColorizationAfterglow"), (Dwm, "ColorPrevalence"),
        (ExplorerAccent, "AccentPalette"), (ExplorerAccent, "AccentColorMenu"), (ExplorerAccent, "StartColorMenu"),
        (@"Control Panel\Desktop", "AutoColorization"),
    ];

    readonly string path = snapshotPath ?? Path.Combine(AppPaths.State, WindowsSnapshot.FileName);
    readonly object gate = new(); // Apply runs on a background thread: one change at a time, snapshot included

    /// <summary>Whether apps are in dark mode right now.</summary>
    public static bool WindowsIsDark =>
        Registry.CurrentUser.OpenSubKey(Personalize)?.GetValue("AppsUseLightTheme") is int light && light == 0;

    /// <summary>True when there are saved originals to restore.</summary>
    public bool HasSnapshot => File.Exists(path);

    /// <summary>
    /// Applies the plan. Slow (the change notification waits for every app to answer, up to seconds), so callers run
    /// it off the UI thread.
    /// </summary>
    public void Apply(WindowsPlan plan)
    {
        lock (gate)
            ApplyLocked(plan);
    }

    void ApplyLocked(WindowsPlan plan)
    {
        if (!TryLoad(out var snapshot))
        {
            Log.Write("windows settings: the snapshot file is damaged; nothing is changed until it is restored or removed");
            return;
        }

        if (plan.Dark is { } dark && WindowsIsDark != dark)
        {
            snapshot = Save(snapshot with { Appearance = snapshot.Appearance ?? new AppearanceSnapshot(Read(AppearanceValues)) });
            using (var key = Registry.CurrentUser.CreateSubKey(Personalize))
            {
                key.SetValue("AppsUseLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
                key.SetValue("SystemUsesLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
            }

            Broadcast();
            Log.Write($"windows settings: {(dark ? "dark" : "light")} mode");
        }

        if (plan.Accent is { } color && Accent.Current is { } current && current.Accent != Accent.ToColorRef(color))
        {
            snapshot = Save(snapshot with { Accent = snapshot.Accent ?? new AccentSnapshot(current.Start, current.Accent, Read(AccentValues)) });
            if (Accent.Set(color))
                Log.Write($"windows settings: accent {color}");
        }

        if (plan.Wallpaper is { } file && !Wallpaper.IsEverywhere(file))
        {
            snapshot = Save(snapshot with { Wallpaper = snapshot.Wallpaper ?? Wallpaper.Snapshot() });
            Wallpaper.Set(file);
            Log.Write($"windows settings: wallpaper {Path.GetFileName(file)}");
        }
    }

    /// <summary>Writes the saved originals back and deletes the snapshot. False when there is a snapshot that cannot be read.</summary>
    public bool Restore()
    {
        lock (gate)
            return RestoreLocked();
    }

    bool RestoreLocked()
    {
        if (!HasSnapshot)
            return true;
        if (!TryLoad(out var snapshot))
        {
            Log.Write("windows settings: snapshot damaged, cannot restore");
            return false;
        }

        if (snapshot.Appearance is { } appearance)
        {
            Write(appearance.Values);
            Broadcast();
        }

        if (snapshot.Accent is { } accent)
        {
            Accent.SetPreference(accent.PreferenceStart, accent.PreferenceAccent);
            Write(accent.Values); // after the API call, which rewrites some of these values
            Broadcast();
        }

        if (snapshot.Wallpaper is { } wallpaper)
            Wallpaper.Restore(wallpaper);

        File.Delete(path);
        Log.Write("windows settings: originals restored");
        return true;
    }

    bool TryLoad(out WindowsSnapshot snapshot)
    {
        snapshot = WindowsSnapshot.Empty;
        if (!File.Exists(path))
            return true;
        var parsed = WindowsSnapshot.Parse(File.ReadAllText(path));
        if (parsed is null)
            return false;
        snapshot = parsed;
        return true;
    }

    // Saved before the change it covers, atomically: a crash right after leaves a complete snapshot.
    WindowsSnapshot Save(WindowsSnapshot snapshot)
    {
        AtomicFile.Write(path, snapshot.Serialize());
        return snapshot;
    }

    static List<SavedValue> Read((string Key, string Name)[] values)
    {
        var list = new List<SavedValue>();
        foreach (var (keyPath, name) in values)
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            object? v = key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (v is null)
            {
                list.Add(new SavedValue(keyPath, name, null, null, null, null));
                continue;
            }

            var kind = key!.GetValueKind(name);
            list.Add(v switch
            {
                byte[] b => new SavedValue(keyPath, name, kind.ToString(), null, Convert.ToBase64String(b), null),
                string s => new SavedValue(keyPath, name, kind.ToString(), null, null, s),
                _ => new SavedValue(keyPath, name, kind.ToString(), Convert.ToInt64(v), null, null),
            });
        }

        return list;
    }

    static void Write(List<SavedValue> values)
    {
        foreach (var v in values)
        {
            using var key = Registry.CurrentUser.CreateSubKey(v.Key);
            if (!v.Existed)
            {
                key.DeleteValue(v.Name, throwOnMissingValue: false); // it did not exist before Veneer
                continue;
            }

            var kind = Enum.Parse<RegistryValueKind>(v.Kind!);
            object data = kind switch
            {
                RegistryValueKind.Binary => Convert.FromBase64String(v.Base64 ?? ""),
                RegistryValueKind.String or RegistryValueKind.ExpandString => v.Text ?? "",
                RegistryValueKind.QWord => v.Number ?? 0,
                _ => unchecked((int)(v.Number ?? 0)),
            };
            key.SetValue(v.Name, data, kind);
        }
    }

    // Tells Explorer and apps that the color settings changed. SMTO_ABORTIFHUNG: a hung app cannot block us.
    static void Broadcast() => SendMessageTimeout(0xFFFF, 0x001A /* WM_SETTINGCHANGE */, 0, "ImmersiveColorSet", 0x0002, 5000, out _);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern nint SendMessageTimeout(nint hwnd, uint msg, nint wParam, string lParam, uint flags, uint timeout, out nint result);

    /// <summary>
    /// The accent color through uxtheme's <c>SetUserColorPreference</c>, which has no name, only ordinal 122
    /// (docs/decisions.md, Phase 0: the only way that regenerates the palette apps see).
    /// </summary>
    static class Accent
    {
        [StructLayout(LayoutKind.Sequential)]
        struct ColorPreference
        {
            public uint Start;
            public uint Accent;
        }

        [DllImport("uxtheme.dll", EntryPoint = "GetUserColorPreference")]
        static extern int GetUserColorPreference(out ColorPreference preference, bool forceReload);

        [DllImport("uxtheme.dll", EntryPoint = "#122")]
        static extern int SetUserColorPreference(ref ColorPreference preference, bool forceCommit);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        static extern nint GetProcAddress(nint module, string name);

        [DllImport("kernel32.dll", EntryPoint = "GetProcAddress")]
        static extern nint GetProcAddressByOrdinal(nint module, nint ordinal);

        /// <summary>
        /// Ordinal 122 is only trusted while its neighbour, the named GetUserColorPreference, is still ordinal 120:
        /// if Windows renumbers uxtheme's exports, Veneer reports "not supported" instead of calling something else.
        /// </summary>
        public static bool Supported { get; } = CheckOrdinals();

        static bool CheckOrdinals()
        {
            if (!NativeLibrary.TryLoad("uxtheme.dll", out nint module))
                return false;
            nint named = GetProcAddress(module, "GetUserColorPreference");
            bool ok = named != 0 && named == GetProcAddressByOrdinal(module, 120) && GetProcAddressByOrdinal(module, 122) != 0;
            if (!ok)
                Log.Write("windows settings: accent color not supported on this Windows build (uxtheme exports changed)");
            return ok;
        }

        public static (uint Start, uint Accent)? Current =>
            Supported && GetUserColorPreference(out var p, false) >= 0 ? (p.Start, p.Accent) : null;

        /// <summary>COLORREF with an opaque alpha byte, the form Windows stores (0xFFBBGGRR).</summary>
        public static uint ToColorRef(ThemeColor c) => 0xFF000000 | (uint)(c.R | c.G << 8 | c.B << 16);

        public static bool Set(ThemeColor color)
        {
            if (Current is not { } current)
                return false;
            SetPreference(current.Start, ToColorRef(color));
            // The API leaves ColorizationColor (title bars with "show accent color") untouched: set it too.
            using (var dwm = Registry.CurrentUser.CreateSubKey(Dwm))
            {
                int argb = unchecked((int)(0xC4000000 | (uint)(color.R << 16 | color.G << 8 | color.B)));
                dwm.SetValue("ColorizationColor", argb, RegistryValueKind.DWord);
                dwm.SetValue("ColorizationAfterglow", argb, RegistryValueKind.DWord);
            }

            Broadcast();
            return true;
        }

        public static void SetPreference(uint start, uint accent)
        {
            if (!Supported)
                return;
            var p = new ColorPreference { Start = start, Accent = accent };
            SetUserColorPreference(ref p, true);
        }
    }

    // IDesktopWallpaper declared by hand: CsWin32 only offers raw pointers for it. Methods in vtable order, up to
    // the last one used (GetPosition).
    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId);

        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetMonitorDevicePathAt(uint index);

        uint GetMonitorDevicePathCount();

        void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out Windows.Win32.Foundation.RECT rect);

        void SetBackgroundColor(uint color);

        uint GetBackgroundColor();

        void SetPosition(int position);

        int GetPosition();
    }

    /// <summary>The desktop wallpaper of every monitor, through <c>IDesktopWallpaper</c>.</summary>
    static class Wallpaper
    {
        const int Fill = 4; // DWPOS_FILL
        static readonly Guid DesktopWallpaperClass = new("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD");

        static T Use<T>(Func<IDesktopWallpaper, T> action)
        {
            var w = (IDesktopWallpaper)Activator.CreateInstance(Type.GetTypeFromCLSID(DesktopWallpaperClass)!)!;
            try
            {
                return action(w);
            }
            finally
            {
                Marshal.ReleaseComObject(w);
            }
        }

        static List<string> Monitors(IDesktopWallpaper w) =>
            Enumerable.Range(0, (int)w.GetMonitorDevicePathCount()).Select(i => w.GetMonitorDevicePathAt((uint)i)).ToList();

        public static bool IsEverywhere(string file) =>
            Use(w => Monitors(w).All(m => string.Equals(w.GetWallpaper(m), file, StringComparison.OrdinalIgnoreCase)));

        // shortcut: a slideshow or Spotlight is saved as its current picture, so a restore brings back a still image;
        // read the slideshow settings too if users ask.
        public static WallpaperSnapshot Snapshot() =>
            Use(w => new WallpaperSnapshot(Monitors(w).Select(m => new MonitorWallpaper(m, w.GetWallpaper(m))).ToList(), w.GetPosition()));

        public static void Set(string file) => Use(w =>
        {
            w.SetPosition(Fill);
            w.SetWallpaper(null, file); // null monitor: all monitors
            return 0;
        });

        public static void Restore(WallpaperSnapshot snapshot) => Use(w =>
        {
            w.SetPosition(snapshot.Position);
            var paths = snapshot.Monitors.Select(m => m.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (paths.Count == 1)
            {
                w.SetWallpaper(null, paths[0]); // one picture everywhere: also updates the registry value Windows shows
                return 0;
            }

            var now = Monitors(w);
            foreach (var m in snapshot.Monitors.Where(m => now.Contains(m.MonitorId)))
                w.SetWallpaper(m.MonitorId, m.Path);
            return 0;
        });
    }

    /// <summary>For the app: whether the accent color can be set on this Windows build.</summary>
    public static bool AccentSupported => Accent.Supported;
}
