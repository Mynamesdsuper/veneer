using Veneer.Core.Themes;

namespace Veneer.Core.Tests.Themes;

public sealed class IconMapTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("veneer-iconmap-").FullName;

    public IconMapTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "apps"));
        Directory.CreateDirectory(Path.Combine(_root, "links"));
        File.WriteAllText(Path.Combine(_root, "apps", "discord.svg"), "<svg/>");
        File.WriteAllText(Path.Combine(_root, "links", "other.svg"), "<svg/>");
        File.WriteAllText(Path.Combine(_root, "apps", "notes.txt"), "");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Good_entries_resolve_inside_the_source_folder()
    {
        var r = IconMap.Parse("""{ "discord": "apps/discord.svg", }""", _root);
        Assert.Empty(r.Problems);
        var e = Assert.Single(r.Entries);
        Assert.Equal("discord", e.Name);
        Assert.Equal(Path.Combine(_root, "apps", "discord.svg"), e.SvgPath);
    }

    [Theory]
    [InlineData("""{ "a/b": "apps/discord.svg" }""", "not a valid file name")]
    [InlineData("""{ "x": "../outside.svg" }""", "'..'")]
    [InlineData("""{ "x": "C:/icons/a.svg" }""", "absolute")]
    [InlineData("""{ "x": "links/other.svg" }""", "links")]
    [InlineData("""{ "x": "apps/notes.txt" }""", "not allowed")]
    [InlineData("""{ "x": "apps/missing.svg" }""", "not found")]
    [InlineData("""{ "x": 3 }""", "must be an SVG path")]
    [InlineData("""{ "x": "apps/discord.svg", "X": "apps/discord.svg" }""", "listed twice")]
    [InlineData("""[ "apps/discord.svg" ]""", "JSON object")]
    [InlineData("""{ "x": """, "not valid JSON")]
    public void Bad_entries_become_problems(string json, string problem)
    {
        var r = IconMap.Parse(json, _root);
        Assert.Contains(r.Problems, p => p.Contains(problem));
    }

    [Fact]
    public void Repository_is_found_from_a_folder_that_does_not_exist_yet()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        Assert.Equal(_root, IconMap.EnclosingRepository(Path.Combine(_root, "themes", "new", "icons")));
        Assert.Null(IconMap.EnclosingRepository(Path.GetPathRoot(_root)!));
    }
}
