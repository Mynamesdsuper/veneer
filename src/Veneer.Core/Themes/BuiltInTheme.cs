namespace Veneer.Core.Themes;

/// <summary>The theme used when no theme has ever loaded. Plain data, loaded through the normal loader.</summary>
public static class BuiltInTheme
{
    public const string Json = """
        {
          "schemaVersion": 1,
          "id": "veneer.fallback",
          "name": "Veneer fallback",
          "surfaces": [
            {
              "id": "bar",
              "type": "bar",
              "edge": "top",
              "thickness": 28,
              "reserveSpace": true,
              "style": { "background": "#F0202020", "foreground": "#FFFFFFFF" },
              "start": [ { "module": "logo-menu" }, { "module": "active-app" } ],
              "end": [ { "module": "volume" }, { "module": "network" }, { "module": "clock", "format": "HH:mm" } ]
            }
          ]
        }
        """;

    public static Theme Theme { get; } =
        ThemeLoader.LoadFromJson(Json, AppContext.BaseDirectory).Theme
        ?? throw new InvalidOperationException("The built-in theme is invalid.");
}
