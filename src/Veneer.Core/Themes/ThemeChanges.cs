using System.Text.Json;

namespace Veneer.Core.Themes;

/// <summary>What changed between two loads of a theme.</summary>
public static class ThemeChanges
{
    static readonly JsonSerializerOptions Options = new() { IncludeFields = false };

    /// <summary>
    /// True when the only difference is the pinned list of some docks (the user pinned, unpinned or reordered an
    /// app, which writes the overrides file and reloads the theme). Then the app updates the docks in place instead
    /// of rebuilding every surface, which would release and reserve the screen strips and resize maximized windows.
    /// </summary>
    public static bool OnlyDockPinnedChanged(Theme before, Theme after)
    {
        if (before.Surfaces.Count != after.Surfaces.Count)
            return false;
        if (Json(before with { Surfaces = [], Origins = NoOrigins }) != Json(after with { Surfaces = [], Origins = NoOrigins }))
            return false;
        for (int i = 0; i < before.Surfaces.Count; i++)
        {
            if (before.Surfaces[i].GetType() != after.Surfaces[i].GetType() || Json(Unpinned(before.Surfaces[i])) != Json(Unpinned(after.Surfaces[i])))
                return false;
        }

        return true;
    }

    static readonly IReadOnlyDictionary<string, ThemeLayer> NoOrigins = new Dictionary<string, ThemeLayer>();

    static Surface Unpinned(Surface s) => s is DockSurface d ? d with { DefaultPinned = [] } : s;

    // Serialized with the runtime type, so DockSurface and BarSurface fields are all compared.
    static string Json(object value) => JsonSerializer.Serialize(value, value.GetType(), Options);
}
