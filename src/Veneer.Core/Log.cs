using System.Collections.Concurrent;

namespace Veneer.Core;

/// <summary>
/// Minimal append-only log in <see cref="AppPaths.Logs"/>. Never throws. <see cref="Write"/> only queues the line: a
/// background thread writes it, so the UI thread never waits for the disk. Call <see cref="Flush"/> before exiting.
/// </summary>
public static class Log
{
    static readonly object Gate = new();
    static readonly ConcurrentQueue<string> Pending = new();
    static readonly AutoResetEvent Queued = new(false);
    static int started;
    const long MaxBytes = 2 * 1024 * 1024;

    public static string FilePath => Path.Combine(AppPaths.Logs, "veneer.log");

    public static void Write(string message)
    {
        Pending.Enqueue($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {message}{Environment.NewLine}");
        if (Interlocked.Exchange(ref started, 1) == 0)
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
            new Thread(WriteLoop) { IsBackground = true, Name = "Veneer log" }.Start();
        }

        Queued.Set();
    }

    /// <summary>Writes every queued line now, on the calling thread. For exit and crash paths.</summary>
    public static void Flush()
    {
        try
        {
            lock (Gate)
            {
                if (Pending.IsEmpty)
                    return;
                var lines = new System.Text.StringBuilder();
                while (Pending.TryDequeue(out string? line))
                    lines.Append(line);
                Directory.CreateDirectory(AppPaths.Logs);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, lines.ToString());
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Logging must never take Veneer down; the lines are lost.
        }
    }

    static void WriteLoop()
    {
        while (true)
        {
            Queued.WaitOne();
            Flush();
        }
    }

    /// <summary>The last <paramref name="count"/> lines of the log (for the diagnostics report); empty if it cannot be read.</summary>
    public static IReadOnlyList<string> Tail(int count)
    {
        Flush();
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
