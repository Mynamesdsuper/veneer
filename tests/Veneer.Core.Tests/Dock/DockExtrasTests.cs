using Veneer.Core.Dock;
using Veneer.Core.Themes;

namespace Veneer.Core.Tests.Dock;

public class DockExtrasTests
{
    [Fact]
    public void Extras_come_last_after_their_own_separator()
    {
        var running = new RunningApp("exe:c:\\x\\a.exe", null, @"C:\x\a.exe", [1]);

        var items = DockModel.Build(["explorer.exe"], [running], separateRunning: true, [DockModel.Downloads, DockModel.Trash]);

        Assert.Equal(["pin:explorer.exe", "sep:running", running.Key, "sep:extras", "extra:downloads", "extra:trash"], items.Select(i => i.Key));
        Assert.Equal(DockModel.Trash, items[^1].Extra);
        Assert.False(items[^1].IsRunning);
    }

    [Fact]
    public void Extras_alone_have_no_separator_and_none_follows_another_separator()
    {
        Assert.Equal(["extra:trash"], DockModel.Build([], [], extras: [DockModel.Trash]).Select(i => i.Key));
        Assert.Single(DockModel.Build(["a.exe", DockModel.Separator], [], extras: [DockModel.Trash]), i => i.IsSeparator);
        Assert.DoesNotContain(DockModel.Build(["a.exe"], []), i => i.Key == "sep:extras");
    }

    [Theory]
    [InlineData(IndicatorMode.App, 5, 1)]
    [InlineData(IndicatorMode.Window, 0, 1)]
    [InlineData(IndicatorMode.Window, 2, 2)]
    [InlineData(IndicatorMode.Window, 7, 3)]
    public void Indicator_count_follows_the_mode(IndicatorMode mode, int windows, int expected) =>
        Assert.Equal(expected, new RunningIndicator(IndicatorShape.Dot, null, 4, mode).Count(windows));

    [Fact]
    public void Downloads_lists_newest_first_and_skips_hidden_and_unfinished_files()
    {
        string folder = Directory.CreateTempSubdirectory("veneer-downloads-").FullName;
        try
        {
            var now = DateTime.UtcNow;
            string File(string name, int minutesAgo, FileAttributes extra = 0)
            {
                string path = Path.Combine(folder, name);
                System.IO.File.WriteAllText(path, "x");
                System.IO.File.SetCreationTimeUtc(path, now.AddMinutes(-minutesAgo));
                System.IO.File.SetLastWriteTimeUtc(path, now.AddDays(-30)); // the server's date, older than the download
                System.IO.File.SetAttributes(path, System.IO.File.GetAttributes(path) | extra);
                return path;
            }

            string old = File("old.zip", 60), mid = File("mid.pdf", 30), recent = File("new.png", 1);
            File("going.crdownload", 0);
            File("desktop.ini", 0, FileAttributes.Hidden | FileAttributes.System);
            string sub = Directory.CreateDirectory(Path.Combine(folder, "unzipped")).FullName;
            Directory.SetCreationTimeUtc(sub, now.AddMinutes(-10));
            Directory.SetLastWriteTimeUtc(sub, now.AddMinutes(-10));

            Assert.Equal([recent, sub, mid, old], DownloadsStack.Latest(folder, 10));
            Assert.Equal([recent, sub], DownloadsStack.Latest(folder, 2));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
