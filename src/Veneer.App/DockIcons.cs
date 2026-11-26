using System.Windows.Media;
using System.Windows.Media.Imaging;
using Veneer.Core;
using Veneer.Core.Dock;
using Veneer.Core.Layout;
using Veneer.Core.Themes;
using Veneer.Interop;

namespace Veneer.App;

/// <summary>Dock icons: theme images first (iconOverrides, then iconFolder), then the shell's icon. Cached per run.</summary>
static class DockIcons
{
    static readonly Dictionary<string, ImageSource?> Cache = [];
    static readonly Dictionary<string, string> Names = [];

    public static void Clear()
    {
        Cache.Clear();
        Names.Clear();
    }

    // The Recycle Bin's shell name: the same in every Windows language, unlike its display name.
    const string RecycleBinFolder = "::{645FF040-5081-101B-9F08-00AA002F954E}";
    static readonly Lazy<string?> DownloadsFolder = new(KnownFolders.Downloads);

    /// <summary>Where the dock's Downloads item points; null when Windows has no Downloads folder.</summary>
    public static string? Downloads => DownloadsFolder.Value;

    /// <summary>The icon of <paramref name="item"/>; <paramref name="trashFull"/> picks the full Recycle Bin.</summary>
    public static ImageSource? For(Theme theme, DockItem item, uint dpi, bool trashFull = false)
    {
        bool full = trashFull && item.Extra == DockModel.Trash;
        string key = $"{theme.Id}|{item.Key}|{full}";
        if (Cache.TryGetValue(key, out var cached))
            return cached;
        var image = FromTheme(theme, item, full)
            ?? (item.Extra == DockModel.Trash ? TrashIcon(full, SurfaceLayout.ToPhysical(256, Math.Max(dpi, 96u))) : null)
            ?? (Aumid(item) is { } aumid && ShellItems.PackagedIcon(aumid) is { } packaged ? Load(packaged) : null)
            ?? FromShell(item, SurfaceLayout.ToPhysical(256, Math.Max(dpi, 96u)));
        Cache[key] = image;
        return image;
    }

    static string? Exe(DockItem item) =>
        item.Running?.ExePath is { } path ? System.IO.Path.GetFileName(path)
        : item.Pinned is { } p && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? System.IO.Path.GetFileName(p)
        : null;

    static string? Aumid(DockItem item) =>
        item.Running?.Aumid ?? (item.Pinned is { } p ? (DockModel.IsAumid(p) ? p : DockModel.AumidForUri(p)) : null);

    static ImageSource? FromTheme(Theme theme, DockItem item, bool trashFull)
    {
        if (item.Extra is { } extra)
        {
            // A theme with only "dock:trash" keeps one look for both states rather than a Windows icon when full.
            string? own = (trashFull ? theme.IconOverrides.GetValueOrDefault("dock:trash-full") : null) ?? theme.IconOverrides.GetValueOrDefault("dock:" + extra);
            return own is null ? null : Load(own);
        }

        string? exe = Exe(item)?.ToLowerInvariant();
        string? aumid = Aumid(item);
        string? path = null;
        if (aumid is not null)
            path = theme.IconOverrides.FirstOrDefault(kv => string.Equals(kv.Key, "aumid:" + aumid, StringComparison.OrdinalIgnoreCase)).Value;
        if (path is null && exe is not null)
            path = theme.IconOverrides.FirstOrDefault(kv => string.Equals(kv.Key, "exe:" + exe, StringComparison.OrdinalIgnoreCase)).Value;
        if (path is null && exe is not null && theme.IconFolder is { } folder)
        {
            string candidate = System.IO.Path.Combine(folder, System.IO.Path.GetFileNameWithoutExtension(exe) + ".png");
            // The folder was checked when the theme loaded; still refuse links that appeared since.
            if (System.IO.File.Exists(candidate) && !AssetPaths.IsLink(candidate))
                path = candidate;
        }

        return path is null ? null : Load(path);
    }

    static ImageSource? Load(string path) => ThemeVisuals.LoadImage(path);

    static ImageSource? TrashIcon(bool full, int size)
    {
        nint icon = RecycleBin.Icon(full, size);
        if (icon == 0)
            return null;
        try
        {
            var image = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        finally
        {
            RecycleBin.FreeIcon(icon);
        }
    }

    static string? ParsingName(DockItem item) =>
        item.Extra == DockModel.Downloads ? Downloads
        : item.Extra == DockModel.Trash ? RecycleBinFolder
        : Aumid(item) is { } aumid ? ShellItems.ParsingName(aumid, isAumid: true)
        : item.Running?.ExePath ?? (item.Pinned is { } p ? ShellItems.ParsingName(p, isAumid: false) : null);

    static ImageSource? FromShell(DockItem item, int size)
    {
        // Some apps set an AppUserModelID that is not in the Apps folder (Steam): fall back to the exe.
        var icon = (ParsingName(item) is { } name ? ShellItems.Icon(name, size) : null)
            ?? (item.Running?.ExePath is { } exe ? ShellItems.Icon(exe, size) : null);
        if (icon is null)
            return null;
        var (pixels, width, height) = icon.Value;
        var format = IsPremultiplied(pixels) ? PixelFormats.Pbgra32 : PixelFormats.Bgra32;
        var bitmap = BitmapSource.Create(width, height, 96, 96, format, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    // The shell returns some icons premultiplied and some with straight alpha. In premultiplied data no color
    // channel can exceed its alpha; one that does means straight alpha. Guessing wrong gives light fringes.
    static bool IsPremultiplied(byte[] bgra)
    {
        for (int i = 0; i < bgra.Length; i += 4)
        {
            byte a = bgra[i + 3];
            if (bgra[i] > a || bgra[i + 1] > a || bgra[i + 2] > a)
                return false;
        }

        return true;
    }

    static string? Description(string exe)
    {
        string? d = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).FileDescription?.Trim();
        return string.IsNullOrEmpty(d) ? null : d.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? d[..^4] : d;
    }

    public static string Name(DockItem item)
    {
        if (Names.TryGetValue(item.Key, out string? name))
            return name;
        string? parsing = ParsingName(item);
        // For an exe the shell's name is the file name ("explorer.exe"); its description is the app name.
        name = (Aumid(item) is null && parsing is not null && System.IO.File.Exists(parsing) ? Description(parsing) : null)
            ?? (parsing is not null ? ShellItems.DisplayName(parsing) : null)
            ?? (item.Running?.ExePath is { } exe ? System.IO.Path.GetFileNameWithoutExtension(exe) : item.Pinned) ?? "";
        Names[item.Key] = name;
        return name;
    }
}
