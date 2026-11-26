namespace Veneer.Core.Dock;

/// <summary>What the dock's Downloads item lists: the newest files and folders of the Downloads folder.</summary>
public static class DownloadsStack
{
    // Files a browser is still writing; they get their real name when the download ends.
    static readonly HashSet<string> Partial = new(StringComparer.OrdinalIgnoreCase) { ".crdownload", ".part", ".partial", ".download", ".tmp" };

    /// <summary>
    /// Up to <paramref name="count"/> entries of <paramref name="folder"/>, newest first. "Newest" is the later of the
    /// creation and the last write time: a download keeps the server's date as its write time in some browsers.
    /// Hidden and system entries, links and unfinished downloads are left out. Empty when the folder cannot be read.
    /// Reads the disk: never call it on the UI thread.
    /// </summary>
    public static List<string> Latest(string folder, int count)
    {
        try
        {
            return new DirectoryInfo(folder)
                .EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint })
                .Where(e => !Partial.Contains(e.Extension))
                .OrderByDescending(e => e.CreationTimeUtc > e.LastWriteTimeUtc ? e.CreationTimeUtc : e.LastWriteTimeUtc)
                .Take(count)
                .Select(e => e.FullName)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            Log.Write($"downloads: cannot read the folder: {e.Message}");
            return [];
        }
    }
}
