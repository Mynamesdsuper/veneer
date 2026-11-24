using System.Text.Json.Nodes;

namespace Veneer.Core.Tests;

public class OverridesFileTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "veneer-tests", Guid.NewGuid().ToString("N"));

    string FilePath => Path.Combine(dir, "profile", "overrides.json");

    public void Dispose()
    {
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Creates_the_file_and_the_surface()
    {
        Assert.True(OverridesFile.TrySetSurfaceValue(FilePath, "dock", "defaultPinned", new JsonArray("a.exe"), out _));

        var root = JsonNode.Parse(File.ReadAllText(FilePath))!;
        Assert.Equal("dock", root["surfaces"]![0]!["id"]!.GetValue<string>());
        Assert.Equal("a.exe", root["surfaces"]![0]!["defaultPinned"]![0]!.GetValue<string>());
    }

    [Fact]
    public void Keeps_everything_else_and_updates_in_place()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, """{ "tokens": { "x": "#FF000000" }, "surfaces": [ { "id": "dock", "iconSize": 56 }, { "id": "bar" } ] }""");

        Assert.True(OverridesFile.TrySetSurfaceValue(FilePath, "dock", "defaultPinned", new JsonArray("b.exe"), out _));
        Assert.True(OverridesFile.TrySetSurfaceValue(FilePath, "dock", "iconSize", null, out _));

        var root = JsonNode.Parse(File.ReadAllText(FilePath))!;
        Assert.Equal("#FF000000", root["tokens"]!["x"]!.GetValue<string>());
        var dock = root["surfaces"]![0]!.AsObject();
        Assert.False(dock.ContainsKey("iconSize"));
        Assert.Equal("b.exe", dock["defaultPinned"]![0]!.GetValue<string>());
        Assert.Equal(2, root["surfaces"]!.AsArray().Count);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("[]")]
    [InlineData("{ \"surfaces\": 3 }")]
    public void Never_overwrites_a_file_it_cannot_read(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, content);

        Assert.False(OverridesFile.TrySetSurfaceValue(FilePath, "dock", "defaultPinned", new JsonArray(), out string? error));
        Assert.NotNull(error);
        Assert.Equal(content, File.ReadAllText(FilePath));
    }

    [Fact]
    public void A_file_with_comments_is_backed_up_before_the_rewrite()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        const string commented = "{ // my notes\n \"tokens\": { \"x\": \"#FF000000\" } }";
        File.WriteAllText(FilePath, commented);

        Assert.True(OverridesFile.TrySetSurfaceValue(FilePath, "dock", "iconSize", 56, out _));

        Assert.Equal(commented, File.ReadAllText(FilePath + ".bak"));
        Assert.Equal("#FF000000", JsonNode.Parse(File.ReadAllText(FilePath))!["tokens"]!["x"]!.GetValue<string>());
    }

    [Fact]
    public void No_backup_without_comments_and_no_temp_files_left()
    {
        Assert.True(OverridesFile.TrySetSurfaceValue(FilePath, "dock", "iconSize", 56, out _));
        Assert.True(OverridesFile.TrySetSurfaceValue(FilePath, "dock", "iconSize", 60, out _));

        Assert.Equal(["overrides.json"], Directory.GetFiles(Path.GetDirectoryName(FilePath)!).Select(Path.GetFileName));
    }

    [Fact]
    public void Duplicate_keys_are_refused_and_the_file_kept()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        const string content = "{ \"surfaces\": [], \"surfaces\": [] }";
        File.WriteAllText(FilePath, content);

        Assert.False(OverridesFile.TrySetSurfaceValue(FilePath, "dock", "iconSize", 56, out _));
        Assert.Equal(content, File.ReadAllText(FilePath));
    }

    [Fact]
    public void With_duplicate_surface_ids_the_first_one_is_updated()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ \"surfaces\": [ { \"id\": \"dock\" }, { \"id\": \"dock\", \"padding\": 1 } ] }");

        Assert.True(OverridesFile.TrySetSurfaceValue(FilePath, "dock", "iconSize", 56, out _));

        var surfaces = JsonNode.Parse(File.ReadAllText(FilePath))!["surfaces"]!.AsArray();
        Assert.Equal(56, surfaces[0]!["iconSize"]!.GetValue<int>());
        Assert.Null(surfaces[1]!["iconSize"]);
    }
}
