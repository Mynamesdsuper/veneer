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
}
