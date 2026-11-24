namespace Veneer.Core.Dock;

/// <summary>One cell of the dock row: where it starts along the dock, how long it is, and its scale.</summary>
public readonly record struct MagnifiedCell(double Start, double Length, double Scale);

/// <summary>
/// Hover magnification, the way the macOS dock does it: cells near the cursor grow with a cosine falloff, the row
/// is laid out again with the new sizes, and the point under the cursor stays under the cursor (so the row grows
/// on both sides of it).
/// </summary>
public static class Magnifier
{
    /// <param name="cells">Base length of each cell along the dock, and whether it grows (icons do, separators do not).</param>
    /// <param name="spacing">Gap between cells.</param>
    /// <param name="cursor">Cursor position along the row, from the start of the unmagnified row; null when outside.</param>
    /// <param name="maxScale">Scale of the cell right under the cursor (1 = no magnification).</param>
    /// <param name="radius">How far the effect reaches, in cells.</param>
    public static MagnifiedCell[] Layout(IReadOnlyList<(double Size, bool Scales)> cells, double spacing, double? cursor, double maxScale, int radius)
    {
        var result = new MagnifiedCell[cells.Count];
        var baseStarts = new double[cells.Count];
        double pos = 0;
        for (int i = 0; i < cells.Count; i++)
        {
            baseStarts[i] = pos;
            result[i] = new MagnifiedCell(pos, cells[i].Size, 1);
            pos += cells[i].Size + spacing;
        }

        if (cursor is not { } x || maxScale <= 1 || radius <= 0 || cells.Count == 0)
            return result;

        double unit = (cells.FirstOrDefault(c => c.Scales).Size is > 0 and var s ? s : 1) + spacing;
        double reach = radius * unit;
        pos = 0;
        for (int i = 0; i < cells.Count; i++)
        {
            double center = baseStarts[i] + cells[i].Size / 2;
            double d = Math.Min(1, Math.Abs(x - center) / reach);
            double scale = cells[i].Scales ? 1 + (maxScale - 1) * Math.Cos(d * Math.PI / 2) : 1;
            result[i] = new MagnifiedCell(pos, cells[i].Size * scale, scale);
            pos += result[i].Length + spacing;
        }

        // Keep the point under the cursor fixed: find the cell it is in, and shift the whole row.
        int k = 0;
        for (int i = 0; i < cells.Count; i++)
        {
            if (x >= baseStarts[i])
                k = i;
        }

        double fraction = Math.Clamp((x - baseStarts[k]) / cells[k].Size, 0, 1);
        double shift = x - (result[k].Start + fraction * result[k].Length);
        for (int i = 0; i < result.Length; i++)
            result[i] = result[i] with { Start = result[i].Start + shift };
        return result;
    }
}
