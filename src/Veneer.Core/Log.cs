namespace Veneer.Core;

/// <summary>Minimal append-only log in <see cref="AppPaths.Logs"/>. Never throws.</summary>
public static class Log
{
    static readonly object Gate = new();
    const long MaxBytes = 2 * 1024 * 1024;

    public static string FilePath => Path.Combine(AppPaths.Logs, "veneer.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Logs);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Logging must never take Veneer down.
        }
    }

    /// <summary>The last <paramref name="count"/> lines of the log (for the diagnostics report); empty if it cannot be read.</summary>
    public static IReadOnlyList<string> Tail(int count)
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];
            // Only the end of the file (it can reach 2 MB), and without the write lock: writers append meanwhile.
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long start = Math.Max(0, stream.Length - 64 * 1024);
            stream.Seek(start, SeekOrigin.Begin);
            string[] lines = new StreamReader(stream).ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            int skip = start > 0 ? 1 : 0; // the first line may be cut in half
            return lines.Skip(skip).TakeLast(count).Select(l => l.TrimEnd('\r')).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
