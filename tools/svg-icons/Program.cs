using SkiaSharp;
using Svg.Skia;
using Veneer.Core.Themes;

// Converts the SVG icons named in an exe → svg map into 256 px PNGs for a theme's iconFolder.
// Usage: svg-icons <map.json> <svg folder> <output folder>
const int Size = 256;

if (args.Length != 3)
{
    Console.Error.WriteLine("usage: svg-icons <map.json> <svg folder> <output folder>");
    return 2;
}

string mapPath = args[0], source = args[1], output = Path.GetFullPath(args[2]);
if (IconMap.EnclosingRepository(output) is { } repo)
{
    Console.Error.WriteLine($"refused: {output} is inside the git repository {repo}. Converted icons may not be redistributable; write them into a local theme folder.");
    return 2;
}

if (AssetPaths.IsLink(output))
{
    Console.Error.WriteLine($"refused: {output} is a link");
    return 2;
}

if (!File.Exists(mapPath) || !Directory.Exists(source))
{
    Console.Error.WriteLine(File.Exists(mapPath) ? $"svg folder not found: {source}" : $"map not found: {mapPath}");
    return 2;
}

var map = IconMap.Parse(File.ReadAllText(mapPath), source);
var problems = new List<string>(map.Problems);
Directory.CreateDirectory(output);
int written = 0;
foreach (var entry in map.Entries)
{
    string target = Path.Combine(output, entry.Name + ".png");
    if (AssetPaths.IsLink(target))
    {
        problems.Add($"'{entry.Name}': {target} is a link, not overwritten");
        continue;
    }

    try
    {
        if (Render(entry.SvgPath) is { } png)
        {
            File.WriteAllBytes(target, png);
            written++;
        }
        else
            problems.Add($"'{entry.Name}': {entry.SvgPath} has no drawable content");
    }
    catch (Exception e) when (e is not OutOfMemoryException)
    {
        problems.Add($"'{entry.Name}': {entry.SvgPath} could not be converted: {e.Message}");
    }
}

Console.WriteLine($"{written} icon(s) written to {output}");
foreach (string p in problems)
    Console.Error.WriteLine("warning: " + p);
return problems.Count == 0 ? 0 : 1;

// Draws the SVG centered on a transparent square, scaled to fit, and encodes it as PNG.
static byte[]? Render(string svgPath)
{
    using var svg = new SKSvg();
    if (svg.Load(svgPath) is not { } picture)
        return null;
    SKRect bounds = picture.CullRect;
    if (bounds.Width <= 0 || bounds.Height <= 0)
        return null;

    float scale = Size / Math.Max(bounds.Width, bounds.Height);
    using var surface = SKSurface.Create(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul));
    SKCanvas canvas = surface.Canvas;
    canvas.Clear(SKColors.Transparent);
    canvas.Translate((Size - bounds.Width * scale) / 2, (Size - bounds.Height * scale) / 2);
    canvas.Scale(scale);
    canvas.Translate(-bounds.Left, -bounds.Top);
    canvas.DrawPicture(picture);
    using SKImage image = surface.Snapshot();
    using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}
