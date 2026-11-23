namespace Veneer.Core.Themes;

/// <summary>Resolves theme-relative paths and refuses anything that could leave the theme folder.</summary>
public static class AssetPaths
{
    public static readonly IReadOnlySet<string> ImageExtensions = new HashSet<string> { ".png", ".ico", ".jpg", ".jpeg" };
    public static readonly IReadOnlySet<string> WallpaperExtensions = new HashSet<string> { ".png", ".jpg", ".jpeg" };
    public static readonly IReadOnlySet<string> FontExtensions = new HashSet<string> { ".ttf", ".otf" };

    /// <summary>
    /// Resolves <paramref name="relative"/> inside <paramref name="root"/>. Rejects empty, absolute and drive paths,
    /// <c>..</c> and <c>.</c> segments, alternate data streams, disallowed extensions (when
    /// <paramref name="allowedExtensions"/> is given) and any existing symlink or junction along the way.
    /// The target does not have to exist.
    /// </summary>
    public static bool TryResolve(string root, string relative, IReadOnlySet<string>? allowedExtensions, out string fullPath, out string error)
    {
        fullPath = "";
        error = "";
        if (string.IsNullOrWhiteSpace(relative))
            return Fail("the path is empty", out error);
        if (Path.IsPathRooted(relative) || relative.Contains(':'))
            return Fail("absolute paths are not allowed, use a path relative to the theme folder", out error);

        string[] parts = relative.Split('/', '\\');
        if (parts.Contains(".."))
            return Fail("'..' is not allowed in theme paths", out error);
        if (parts.Any(p => p.Length == 0 || p == "." || p != p.TrimEnd('.', ' ')))
            return Fail("the path is not valid", out error);
        if (allowedExtensions is not null && !allowedExtensions.Contains(Path.GetExtension(relative).ToLowerInvariant()))
            return Fail($"file type not allowed here (allowed: {string.Join(", ", allowedExtensions)})", out error);

        string rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(rootFull, relative));
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            return Fail("the path leaves the theme folder", out error);

        string current = rootFull;
        foreach (string part in parts)
        {
            current = Path.Combine(current, part);
            if (IsLink(current))
                return Fail("links (symlinks, junctions) are not allowed in themes", out error);
        }

        fullPath = full;
        return true;
    }

    /// <summary>True when <paramref name="path"/> exists and is a symlink, junction or other reparse point.</summary>
    public static bool IsLink(string path)
    {
        try
        {
            return (File.Exists(path) || Directory.Exists(path))
                && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (IOException)
        {
            return true; // cannot inspect it: treat as unsafe
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}
