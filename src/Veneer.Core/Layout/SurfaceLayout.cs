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

    /// <summary>
    /// A floating bar inside its <paramref name="strip"/>: <paramref name="margin"/> pixels away from the screen edge
    /// and from both ends of the strip. The side facing the desktop keeps no gap (the strip is thickness + margin).
    /// </summary>
    public static PixelRect Float(PixelRect strip, Edge edge, int margin)
    {
        var r = new PixelRect(strip.Left + margin, strip.Top + margin, strip.Right - margin, strip.Bottom - margin);
        return edge switch
        {
            Edge.Top => r with { Bottom = strip.Bottom },
            Edge.Bottom => r with { Top = strip.Top },
            Edge.Left => r with { Right = strip.Right },
            _ => r with { Left = strip.Left },
        };
    }

    /// <summary>What is left of the monitor once the edge surfaces are placed (where a dock goes).</summary>
    public static PixelRect Remaining(PixelRect monitor, IEnumerable<EdgeItem> items)
    {
        var list = items.ToList();
        int Sum(Edge e) => list.Where(s => s.Edge == e).Sum(s => s.Thickness);
        return new PixelRect(monitor.Left + Sum(Edge.Left), monitor.Top + Sum(Edge.Top), monitor.Right - Sum(Edge.Right), monitor.Bottom - Sum(Edge.Bottom));
    }

    /// <summary>
    /// Whether a maximized window no longer matches the work area (its maximized size is from before the bars or the
    /// taskbar changed). A maximized window overhangs the work area by its frame width on every side; that overhang
    /// is read from the left edge.
    /// </summary>
    public static bool NeedsRefit(PixelRect window, PixelRect work)
    {
        var expected = MaximizedBounds(window, work);
        return Math.Abs(window.Top - expected.Top) > 2 || Math.Abs(window.Bottom - expected.Bottom) > 2
            || Math.Abs(window.Right - expected.Right) > 2;
    }

    /// <summary>Where a maximized window belongs on <paramref name="work"/>, keeping its frame overhang.</summary>
    public static PixelRect MaximizedBounds(PixelRect window, PixelRect work)
    {
        int inset = Math.Max(0, work.Left - window.Left);
        return new PixelRect(work.Left - inset, work.Top - inset, work.Right + inset, work.Bottom + inset);
    }

    /// <summary>Converts device-independent pixels to physical pixels for a monitor DPI (96 = 100%).</summary>
    public static int ToPhysical(double dips, uint dpi) => (int)Math.Round(dips * dpi / 96.0, MidpointRounding.AwayFromZero);
}
