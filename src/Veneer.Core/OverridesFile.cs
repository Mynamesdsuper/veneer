using System.Text.Json;
using System.Text.Json.Nodes;

namespace Veneer.Core;

/// <summary>
/// Writes single values into the user's overrides file (same shape as a theme, see docs/theme-format.md).
/// Everything else in the file is kept. A file that cannot be parsed is never overwritten.
/// </summary>
public static class OverridesFile
{
    static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Sets <c>surfaces[id=<paramref name="surfaceId"/>].<paramref name="key"/></c>; null removes the key.</summary>
    public static bool TrySetSurfaceValue(string path, string surfaceId, string key, JsonNode? value, out string? error)
    {
        error = null;
        try
        {
            JsonObject root = [];
            if (File.Exists(path))
            {
                if (JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions) is not JsonObject parsed)
                {
                    error = "the overrides file is not a JSON object; it was left untouched";
                    return false;
                }

                root = parsed;
            }

            if (root["surfaces"] is not JsonArray surfaces)
            {
                if (root["surfaces"] is not null)
                {
                    error = "\"surfaces\" in the overrides file is not an array; it was left untouched";
                    return false;
                }

                root["surfaces"] = surfaces = [];
            }

            var surface = surfaces.OfType<JsonObject>().FirstOrDefault(s => s["id"] is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>() == surfaceId);
            if (surface is null)
            {
                surface = new JsonObject { ["id"] = surfaceId };
                surfaces.Add(surface);
            }

            if (value is null)
                surface.Remove(key);
            else
                surface[key] = value.DeepClone();

            // Write a temporary file first, then swap: a crash mid-write never leaves a half file.
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, root.ToJsonString(WriteOptions));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            error = e.Message;
            return false;
        }
    }
}
