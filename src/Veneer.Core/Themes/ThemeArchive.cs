using System.IO.Compression;

namespace Veneer.Core.Themes;

public sealed record ArchiveLimits(long MaxTotalBytes = 100L * 1024 * 1024, long MaxFileBytes = 30L * 1024 * 1024, int MaxFiles = 2000)
{
    public static ArchiveLimits Default { get; } = new();
}

public sealed record ThemeInstallResult(string? InstalledDirectory, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => InstalledDirectory is not null;
}

/// <summary>Safe extraction of theme <c>.zip</c> files: zip-slip, links, file types and size limits are all checked.</summary>
public static class ThemeArchive
{
    public static readonly IReadOnlySet<string> AllowedExtensions =
        new HashSet<string> { ".json", ".png", ".ico", ".jpg", ".jpeg", ".ttf", ".otf", ".txt", ".md" };

    static readonly HashSet<string> AllowedBareNames = new(StringComparer.OrdinalIgnoreCase) { "LICENSE", "CREDITS" };

    /// <summary>
    /// Extracts <paramref name="zip"/> into <paramref name="destination"/>. Every entry is checked before anything
    /// is written. Returns the errors; on error the destination may hold partial output and must be deleted by the caller.
    /// </summary>
    public static List<Diagnostic> Extract(Stream zip, string destination, ArchiveLimits? limits = null)
    {
        limits ??= ArchiveLimits.Default;
        var errors = new List<Diagnostic>();
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            errors.Add(Diagnostic.Error("archive", "is not a valid .zip file"));
            return errors;
        }

        using (archive)
        {
            var files = archive.Entries.Where(e => !e.FullName.EndsWith('/') && !IsMacJunk(e.FullName)).ToList();
            if (files.Count > limits.MaxFiles)
            {
                errors.Add(Diagnostic.Error("archive", $"has {files.Count} files, the limit is {limits.MaxFiles}"));
                return errors;
            }

            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination)) + Path.DirectorySeparatorChar;
            var targets = new List<(ZipArchiveEntry Entry, string Path)>();
            long declared = 0;
            foreach (var entry in files)
            {
                if (Check(entry, root, limits) is { } problem)
                    errors.Add(Diagnostic.Error(entry.FullName, problem));
                else
                    targets.Add((entry, Path.GetFullPath(Path.Combine(root, entry.FullName))));
                declared += entry.Length;
            }

            if (declared > limits.MaxTotalBytes)
                errors.Add(Diagnostic.Error("archive", $"unpacks to {declared / (1024 * 1024)} MB, the limit is {limits.MaxTotalBytes / (1024 * 1024)} MB"));
            if (errors.Count > 0)
                return errors;

            // Sizes in the zip header can lie, so count the bytes actually written too.
            long total = 0;
            var buffer = new byte[81920];
            foreach (var (entry, path) in targets)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var source = entry.Open();
                using var target = File.Create(path);
                long written = 0;
                int read;
                while ((read = source.Read(buffer)) > 0)
                {
                    written += read;
                    total += read;
                    if (written > limits.MaxFileBytes || total > limits.MaxTotalBytes)
                    {
                        errors.Add(Diagnostic.Error(entry.FullName, "is larger than the zip header says: archive rejected"));
                        return errors;
                    }

                    target.Write(buffer, 0, read);
                }
            }
        }

        return errors;
    }

    static string? Check(ZipArchiveEntry entry, string root, ArchiveLimits limits)
    {
        const int UnixFileTypeMask = 0xF000, UnixSymlink = 0xA000;
        if (((entry.ExternalAttributes >> 16) & UnixFileTypeMask) == UnixSymlink)
            return "links are not allowed in themes";

        string name = entry.FullName.Replace('\\', '/');
        if (name.StartsWith('/') || name.Contains(':'))
            return "absolute paths are not allowed";
        string[] parts = name.Split('/');
        if (parts.Any(p => p is "" or "." or ".." || p != p.TrimEnd('.', ' ')))
            return "the path is not valid ('..' and empty parts are not allowed)";
        if (!Path.GetFullPath(Path.Combine(root, name)).StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return "the path leaves the theme folder";

        string file = parts[^1];
        string ext = Path.GetExtension(file).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext) && !(ext.Length == 0 && AllowedBareNames.Contains(file)))
            return $"file type not allowed (allowed: {string.Join(", ", AllowedExtensions)}, LICENSE, CREDITS)";
        if (entry.Length > limits.MaxFileBytes)
            return $"is {entry.Length / (1024 * 1024)} MB, the limit per file is {limits.MaxFileBytes / (1024 * 1024)} MB";
        return null;
    }

    static bool IsMacJunk(string name) =>
        name.StartsWith("__MACOSX/", StringComparison.Ordinal) || name.EndsWith(".DS_Store", StringComparison.Ordinal);

    /// <summary>
    /// Installs a theme <c>.zip</c> into <paramref name="themesDirectory"/>/&lt;theme id&gt;. The archive is extracted to a
    /// staging folder and fully validated first; an existing theme with the same id is replaced only on success.
    /// </summary>
    public static ThemeInstallResult Install(string zipPath, string themesDirectory, ArchiveLimits? limits = null)
    {
        var diagnostics = new List<Diagnostic>();
        string staging = Path.Combine(themesDirectory, ".staging-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(themesDirectory);
            using (var stream = File.OpenRead(zipPath))
                diagnostics.AddRange(Extract(stream, staging, limits));
            if (diagnostics.Count > 0)
                return new ThemeInstallResult(null, diagnostics);

            if (FindThemeRoot(staging) is not { } root)
            {
                diagnostics.Add(Diagnostic.Error("archive", $"no {ThemeLoader.ThemeFileName} at the top of the archive (or in its only folder)"));
                return new ThemeInstallResult(null, diagnostics);
            }

            var load = ThemeLoader.Load(root);
            diagnostics.AddRange(load.Diagnostics);
            if (load.Theme is not { } theme)
                return new ThemeInstallResult(null, diagnostics);

            string target = Path.Combine(themesDirectory, theme.Id); // the id is validated to be a safe folder name
            string backup = target + ".old-" + Guid.NewGuid().ToString("N");
            bool hadPrevious = Directory.Exists(target);
            if (hadPrevious)
                Directory.Move(target, backup);
            try
            {
                Directory.Move(root, target);
            }
            catch (IOException)
            {
                if (hadPrevious)
                    Directory.Move(backup, target); // put the old version back
                throw;
            }

            if (hadPrevious)
                Directory.Delete(backup, recursive: true);
            return new ThemeInstallResult(target, diagnostics);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(Diagnostic.Error("archive", $"could not be installed: {e.Message}"));
            return new ThemeInstallResult(null, diagnostics);
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    static string? FindThemeRoot(string folder)
    {
        if (File.Exists(Path.Combine(folder, ThemeLoader.ThemeFileName)))
            return folder;
        if (!Directory.Exists(folder))
            return null;
        string[] dirs = Directory.GetDirectories(folder);
        return dirs.Length == 1 && Directory.GetFiles(folder).Length == 0 && File.Exists(Path.Combine(dirs[0], ThemeLoader.ThemeFileName))
            ? dirs[0]
            : null;
    }
}
