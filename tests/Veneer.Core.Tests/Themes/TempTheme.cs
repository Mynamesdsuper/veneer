namespace Veneer.Core.Tests.Themes;

/// <summary>A throwaway theme folder on disk for tests.</summary>
sealed class TempTheme : IDisposable
{
    public const string Sample = """
        {
          // comments and trailing commas are allowed
          "schemaVersion": 1,
          "id": "com.example.sample",
          "name": "Sample",
          "author": "tests",
          "version": "1.0.0",
          "license": "MIT",
          "windows": { "wallpaper": "assets/wallpaper.jpg", "appearance": "light", "accentColor": "#FF0A84FF" },
          "fonts": { "ui": "assets/Inter-Regular.ttf" },
          "tokens": { "glass": "#99F5F5F5", "text": "#FF1D1D1F" },
          "surfaces": [
            {
              "id": "menubar", "type": "bar", "edge": "top", "monitor": "primary", "reserveSpace": true, "thickness": 24,
              "style": { "background": "$glass", "blur": true, "foreground": "$text" },
              "start": [ { "module": "logo-menu", "icon": "assets/logo.png" }, { "module": "active-app", "bold": true } ],
              "center": [],
              "end": [ { "module": "volume" }, { "module": "clock", "format": "ddd d MMM HH:mm" } ],
            },
            {
              "id": "dock", "type": "dock", "edge": "bottom", "iconSize": 48,
              "magnification": { "enabled": true, "maxScale": 1.6, "radius": 3 },
              "cornerRadius": 18, "padding": 6,
              "style": { "background": "#66FFFFFF", "blur": true, "border": "#33FFFFFF" },
              "runningIndicator": { "shape": "dot", "color": "$text", "size": 4 },
              "defaultPinned": [ "explorer.exe", "ms-settings:" ]
            }
          ],
          "iconOverrides": { "exe:code.exe": "assets/icons/vscode.png" },
          "iconFolder": "assets/icons",
          "variants": {
            "dark": { "tokens": { "glass": "#99202022", "text": "#FFFFFFFF" }, "windows": { "wallpaper": "assets/wallpaper-dark.jpg" } }
          }
        }
        """;

    public static readonly string[] SampleAssets =
        ["assets/wallpaper.jpg", "assets/wallpaper-dark.jpg", "assets/Inter-Regular.ttf", "assets/logo.png", "assets/icons/vscode.png"];

    public TempTheme(string? json = Sample, params string[] assets)
    {
        Directory.CreateDirectory(Root);
        if (json is not null)
            File.WriteAllText(System.IO.Path.Combine(Root, "theme.json"), json);
        foreach (string a in json == Sample && assets.Length == 0 ? SampleAssets : assets)
            Write(a, "x");
    }

    public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "veneer-tests", Guid.NewGuid().ToString("N"));

    public string Path(string relative) => System.IO.Path.Combine(Root, relative);

    public string Write(string relative, string content)
    {
        string full = Path(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
