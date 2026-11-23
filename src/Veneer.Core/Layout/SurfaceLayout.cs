using Veneer.Core.Themes;

namespace Veneer.Core.Layout;

/// <summary>A rectangle in physical screen pixels; Right and Bottom are exclusive.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}

/// <summary>An edge surface to place: its edge and its thickness in physical pixels.</summary>
public readonly record struct EdgeItem(string Id, Edge Edge, int Thickness);

public static class SurfaceLayout
{
    /// <summary>
    /// Places edge surfaces on a monitor. Surfaces on the same edge stack outwards-in, in theme order (the first one
    /// touches the screen edge). Top and bottom surfaces span the full width; left and right ones fit between them.
    /// </summary>
    public static Dictionary<string, PixelRect> Compute(PixelRect monitor, IEnumerable<EdgeItem> items)
    {
        var list = items.ToList();
        int top = monitor.Top, bottom = monitor.Bottom;
        var result = new Dictionary<string, PixelRect>();

        foreach (var s in list.Where(s => s.Edge is Edge.Top or Edge.Bottom))
        {
            if (s.Edge == Edge.Top)
            {
                result[s.Id] = monitor with { Top = top, Bottom = top + s.Thickness };
                top += s.Thickness;
            }
            else
            {
                result[s.Id] = monitor with { Top = bottom - s.Thickness, Bottom = bottom };
                bottom -= s.Thickness;
            }
        }

        int left = monitor.Left, right = monitor.Right;
        foreach (var s in list.Where(s => s.Edge is Edge.Left or Edge.Right))
        {
            if (s.Edge == Edge.Left)
            {
                result[s.Id] = new PixelRect(left, top, left + s.Thickness, bottom);
                left += s.Thickness;
            }
            else
            {
                result[s.Id] = new PixelRect(right - s.Thickness, top, right, bottom);
                right -= s.Thickness;
            }
        }

        return result;
    }

    /// <summary>Converts device-independent pixels to physical pixels for a monitor DPI (96 = 100%).</summary>
    public static int ToPhysical(double dips, uint dpi) => (int)Math.Round(dips * dpi / 96.0, MidpointRounding.AwayFromZero);
}
