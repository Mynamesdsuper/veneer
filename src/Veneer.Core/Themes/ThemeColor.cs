using System.Globalization;

namespace Veneer.Core.Themes;

/// <summary>An sRGB color with alpha, written in themes as <c>#RRGGBB</c> or <c>#AARRGGBB</c>.</summary>
public readonly record struct ThemeColor(byte A, byte R, byte G, byte B)
{
    public static bool TryParse(string? text, out ThemeColor color)
    {
        color = default;
        if (text is null || text.Length is not (7 or 9) || text[0] != '#')
            return false;
        if (!uint.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint v))
            return false;
        if (text.Length == 7)
            v |= 0xFF000000;
        color = new ThemeColor((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public override string ToString() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";
}
