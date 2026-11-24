using System.Text;

namespace Veneer.Core;

public static class AtomicFile
{
    /// <summary>
    /// Writes a file through a temporary copy that is flushed to disk and then swapped in, so a crash or power loss
    /// leaves either the old file or the new one, never an empty or partial one.
    /// </summary>
    public static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(Encoding.UTF8.GetBytes(content));
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
