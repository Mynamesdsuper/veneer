using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Veneer.Core;
using Veneer.Core.Themes;

namespace Veneer.App;

/// <summary>Turns theme values into WPF brushes, fonts and text settings.</summary>
static class ThemeVisuals
{
    static readonly Dictionary<string, FontFamily?> FontCache = [];

    public static Brush? Brush(ThemeColor? color)
    {
        if (color is not { } c)
            return null;
        var brush = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }

    /// <summary>Sets the inheritable text properties on <paramref name="element"/>; null values keep the inherited ones.</summary>
    public static void ApplyText(DependencyObject element, Theme theme, TextStyle text, ThemeColor? foreground)
    {
        if (Brush(foreground) is { } brush)
            TextElement.SetForeground(element, brush);
        if (text.Font is { } font && Font(theme, font) is { } family)
            TextElement.SetFontFamily(element, family);
        if (text.FontSize is { } size)
            TextElement.SetFontSize(element, size);
        if (text.FontWeight is { } weight)
            TextElement.SetFontWeight(element, FontWeight.FromOpenTypeWeight(weight));
    }

    /// <summary>A key of the theme's <c>fonts</c> map (a font file in the theme) or an installed family name.</summary>
    static FontFamily? Font(Theme theme, string font)
    {
        if (!theme.Fonts.TryGetValue(font, out string? file))
            return new FontFamily(font);

        lock (FontCache)
        {
            if (FontCache.TryGetValue(file, out var cached))
                return cached;
            FontFamily? family = null;
            try
            {
                // WPF loads a font file through its folder plus "#<family name>"; the name is read from the file.
                var face = new GlyphTypeface(new Uri(file));
                string name = face.FamilyNames.TryGetValue(System.Globalization.CultureInfo.GetCultureInfo("en-US"), out var en)
                    ? en
                    : face.FamilyNames.Values.First();
                family = new FontFamily(new Uri(Path.GetDirectoryName(file) + Path.DirectorySeparatorChar), "./#" + name);
            }
            catch (Exception e) when (e is IOException or UriFormatException or NotSupportedException or FileFormatException)
            {
                Log.Write($"font \"{font}\" ({file}) could not be loaded: {e.Message}");
            }

            FontCache[file] = family;
            return family;
        }
    }
}
