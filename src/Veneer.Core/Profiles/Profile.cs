using System.Text.Json;

namespace Veneer.Core.Profiles;

/// <summary>
/// What a profile chooses besides its overrides and Windows preferences (which have their own files): the theme, by
/// folder name. Stored as <c>profile.json</c> in the profile folder.
/// </summary>
public sealed record Profile(string Theme = Profile.DefaultTheme)
{
    public const string FileName = "profile.json";

    /// <summary>The bundled default theme.</summary>
    public const string DefaultTheme = "aqua";

    public string Serialize() => JsonSerializer.Serialize(new { theme = Theme }, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Reads the file leniently: a missing file or a bad value gives the default theme, with a warning.</summary>
    public static Profile Parse(string? json, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Profile();
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                warnings.Add($"{FileName}: must be an object; using the defaults");
                return new Profile();
            }

            if (!doc.RootElement.TryGetProperty("theme", out var theme))
                return new Profile();
            string? name = theme.ValueKind == JsonValueKind.String ? theme.GetString() : null;
            if (FolderName.Problem(name) is { } problem)
            {
                warnings.Add($"{FileName}: \"theme\" must be a theme folder name ({problem}); using \"{DefaultTheme}\"");
                return new Profile();
            }

            return new Profile(name!);
        }
        catch (JsonException e)
        {
            warnings.Add($"{FileName}: invalid JSON ({e.Message}); using the defaults");
            return new Profile();
        }
    }
}
