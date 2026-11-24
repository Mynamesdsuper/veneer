using System.IO.Compression;
using Veneer.Core.Themes;

namespace Veneer.Core.Tests.Themes;

public class ThemeArchiveTests : IDisposable
{
    readonly string work = Path.Combine(Path.GetTempPath(), "veneer-tests", Guid.NewGuid().ToString("N"));

    public ThemeArchiveTests() => Directory.CreateDirectory(work);

    public void Dispose() => Directory.Delete(work, recursive: true);

    static MemoryStream Zip(params (string Name, string Content)[] entries) => Zip(entries.Select(e => (e.Name, e.Content, 0)).ToArray());

    static MemoryStream Zip((string Name, string Content, int UnixMode)[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content, mode) in entries)
            {
                var e = zip.CreateEntry(name);
                if (mode != 0)
                    e.ExternalAttributes = mode << 16;
                using var w = new StreamWriter(e.Open());
                w.Write(content);
            }
        }

        ms.Position = 0;
        return ms;
    }

    string ZipFile(params (string Name, string Content)[] entries)
    {
        string path = Path.Combine(work, Guid.NewGuid().ToString("N") + ".zip");
        using var ms = Zip(entries);
        File.WriteAllBytes(path, ms.ToArray());
        return path;
    }

    static (string, string)[] SampleEntries(string prefix = "") =>
        [(prefix + "theme.json", TempTheme.Sample), .. TempTheme.SampleAssets.Select(a => (prefix + a, "x")), (prefix + "LICENSE", "MIT")];

    [Theory]
    [InlineData("../evil.png")]
    [InlineData("assets/../../evil.png")]
    [InlineData("..\\evil.png")]
    [InlineData("/evil.png")]
    [InlineData("C:/evil.png")]
    [InlineData("assets/run.exe")]
    [InlineData("assets/script.ps1")]
    [InlineData("assets/evil.png.")]
    public void Unsafe_entries_are_rejected_and_nothing_is_written(string name)
    {
        string dest = Path.Combine(work, "out");
        var errors = ThemeArchive.Extract(Zip(("theme.json", "{}"), (name, "x")), dest);

        Assert.Contains(errors, e => e.Path == name);
        Assert.False(Directory.Exists(dest));
        Assert.False(File.Exists(Path.Combine(work, "evil.png")));
    }

    [Fact]
    public void Symlink_entries_are_rejected()
    {
        var errors = ThemeArchive.Extract(Zip([("assets/link.png", "/etc/passwd", 0xA1FF)]), Path.Combine(work, "out"));
        Assert.Contains("links", Assert.Single(errors).Message);
    }

    [Fact]
    public void Too_many_files_are_rejected()
    {
        var entries = Enumerable.Range(0, 11).Select(i => ($"assets/{i}.png", "x")).ToArray();
        var errors = ThemeArchive.Extract(Zip(entries), Path.Combine(work, "out"), new ArchiveLimits(MaxFiles: 10));
        Assert.Contains("limit is 10", Assert.Single(errors).Message);
    }

    [Fact]
    public void Too_large_file_is_rejected()
    {
        var errors = ThemeArchive.Extract(Zip(("assets/big.png", new string('x', 2000))), Path.Combine(work, "out"), new ArchiveLimits(MaxFileBytes: 1000));
        Assert.Equal("assets/big.png", Assert.Single(errors).Path);
    }

    [Fact]
    public void Too_large_total_is_rejected()
    {
        var errors = ThemeArchive.Extract(Zip(("a.png", new string('x', 600)), ("b.png", new string('x', 600))), Path.Combine(work, "out"), new ArchiveLimits(MaxTotalBytes: 1000));
        Assert.Equal("archive", Assert.Single(errors).Path);
    }

    [Fact]
    public void Not_a_zip_is_an_error()
    {
        var errors = ThemeArchive.Extract(new MemoryStream("not a zip"u8.ToArray()), Path.Combine(work, "out"));
        Assert.Contains("not a valid .zip", Assert.Single(errors).Message);
    }

    [Fact]
    public void Mac_metadata_is_skipped()
    {
        string dest = Path.Combine(work, "out");
        var errors = ThemeArchive.Extract(Zip(("theme.json", "{}"), ("__MACOSX/._theme.json", "junk"), (".DS_Store", "junk")), dest);

        Assert.Empty(errors);
        Assert.Equal(["theme.json"], Directory.GetFiles(dest).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("sample/")]
    public void Install_puts_the_theme_in_a_folder_named_by_id(string prefix)
    {
        string themes = Path.Combine(work, "themes");
        var r = ThemeArchive.Install(ZipFile(SampleEntries(prefix)), themes);

        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.Equal(Path.Combine(themes, "com.example.sample"), r.InstalledDirectory);
        Assert.True(ThemeLoader.Load(r.InstalledDirectory!).Success);
        Assert.Equal(["com.example.sample"], Directory.GetDirectories(themes).Select(Path.GetFileName)); // no staging left
    }

    [Fact]
    public void Install_replaces_an_older_version()
    {
        string themes = Path.Combine(work, "themes");
        ThemeArchive.Install(ZipFile(SampleEntries()), themes);
        var r = ThemeArchive.Install(ZipFile([.. SampleEntries(), ("assets/new.png", "x")]), themes);

        Assert.True(r.Success);
        Assert.True(File.Exists(Path.Combine(r.InstalledDirectory!, "assets", "new.png")));
        Assert.Single(Directory.GetDirectories(themes));
    }

    [Fact]
    public void Invalid_theme_is_not_installed_and_the_old_one_survives()
    {
        string themes = Path.Combine(work, "themes");
        ThemeArchive.Install(ZipFile(SampleEntries()), themes);
        string broken = TempTheme.Sample.Replace("\"iconSize\": 48", "\"iconSize\": 900");
        var r = ThemeArchive.Install(ZipFile([("theme.json", broken), .. SampleEntries().Skip(1)]), themes);

        Assert.False(r.Success);
        Assert.Contains(r.Diagnostics, d => d.Path == "surfaces[id=dock].iconSize");
        Assert.Equal(["com.example.sample"], Directory.GetDirectories(themes).Select(Path.GetFileName));
        Assert.True(ThemeLoader.Load(Path.Combine(themes, "com.example.sample")).Success);
    }

    // Rewrites the compression method of every entry (local and central headers) to 14 (LZMA), which .NET cannot read.
    static MemoryStream WithUnsupportedMethod(MemoryStream zip)
    {
        byte[] b = zip.ToArray();
        for (int i = 0; i + 4 <= b.Length; i++)
        {
            uint sig = BitConverter.ToUInt32(b, i);
            if (sig == 0x04034b50)
                BitConverter.GetBytes((ushort)14).CopyTo(b, i + 8);
            else if (sig == 0x02014b50)
                BitConverter.GetBytes((ushort)14).CopyTo(b, i + 10);
        }

        return new MemoryStream(b);
    }

    [Fact]
    public void An_unsupported_or_damaged_entry_is_an_error_not_an_exception()
    {
        var errors = ThemeArchive.Extract(WithUnsupportedMethod(Zip(("theme.json", new string('x', 500)))), Path.Combine(work, "out"));
        Assert.Contains("damaged or uses an unsupported format", Assert.Single(errors).Message);
    }

    [Fact]
    public void A_damaged_zip_is_reported_by_install_too()
    {
        string path = Path.Combine(work, "bad.zip");
        File.WriteAllBytes(path, WithUnsupportedMethod(Zip(SampleEntries())).ToArray());

        var r = ThemeArchive.Install(path, Path.Combine(work, "themes"));

        Assert.False(r.Success);
        Assert.Empty(Directory.GetDirectories(Path.Combine(work, "themes"))); // staging cleaned up
    }

    [Fact]
    public void A_zip_larger_than_the_total_limit_is_rejected_before_reading()
    {
        var errors = ThemeArchive.Extract(Zip(("a.png", new string('x', 100))), Path.Combine(work, "out"), new ArchiveLimits(MaxTotalBytes: 50));
        Assert.Contains("larger than", Assert.Single(errors).Message);
    }

    [Fact]
    public void Archive_without_theme_json_is_rejected()
    {
        var r = ThemeArchive.Install(ZipFile(("readme.md", "hi")), Path.Combine(work, "themes"));
        Assert.Contains("no theme.json", Assert.Single(r.Diagnostics).Message);
    }
}
