using System.Diagnostics;
using Veneer.Core.Themes;

namespace Veneer.Core.Tests.Themes;

public class AssetPathsTests
{
    [Fact]
    public void Relative_path_resolves_inside_the_root()
    {
        using var t = new TempTheme(json: null);
        Assert.True(AssetPaths.TryResolve(t.Root, "assets\\icons/a.png", AssetPaths.ImageExtensions, out string full, out _));
        Assert.Equal(t.Path(Path.Combine("assets", "icons", "a.png")), full);
    }

    [Fact]
    public void Junction_inside_the_theme_is_rejected()
    {
        // Junctions need no admin rights, unlike symlinks, so this runs everywhere (including CI).
        using var t = new TempTheme(json: null);
        using var outside = new TempTheme(json: null, "secret.png");
        var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{t.Path("assets")}\" \"{outside.Root}\"") { CreateNoWindow = true, UseShellExecute = false })!;
        mklink.WaitForExit();
        Assert.Equal(0, mklink.ExitCode);

        Assert.False(AssetPaths.TryResolve(t.Root, "assets/secret.png", AssetPaths.ImageExtensions, out _, out string error));
        Assert.Contains("links", error);
        Directory.Delete(t.Path("assets")); // removes the junction only, not its target
    }

    [Theory]
    [InlineData("#FF0A84FF", 0xFF, 0x0A, 0x84, 0xFF)]
    [InlineData("#0a84ff", 0xFF, 0x0A, 0x84, 0xFF)]
    [InlineData("#99F5F5F5", 0x99, 0xF5, 0xF5, 0xF5)]
    public void Colors_parse(string text, byte a, byte r, byte g, byte b)
    {
        Assert.True(ThemeColor.TryParse(text, out var c));
        Assert.Equal(new ThemeColor(a, r, g, b), c);
    }

    [Theory]
    [InlineData("FF0A84FF")]
    [InlineData("#FFF")]
    [InlineData("#GG0000")]
    [InlineData("#+12345")]
    [InlineData(null)]
    public void Bad_colors_do_not_parse(string? text) => Assert.False(ThemeColor.TryParse(text, out _));
}
