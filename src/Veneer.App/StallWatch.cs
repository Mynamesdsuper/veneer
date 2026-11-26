using System.Diagnostics;
using System.Windows.Threading;
using Veneer.Core;

namespace Veneer.App;

/// <summary>
/// Logs when the UI thread stops answering: a background thread asks it for a no-op once a second, and when no answer
/// comes within a second the log gets how long it took and what the UI thread was marked as doing (.NET cannot read
/// another thread's stack, so the marks are the hint). The slowest answer is logged at exit.
/// </summary>
static class StallWatch
{
    static readonly TimeSpan Every = TimeSpan.FromSeconds(1), Limit = TimeSpan.FromSeconds(1);
    static volatile string doing = "idle";
    static long longestMs;
    static string longestDoing = "";

    /// <summary>Marks what the UI thread does until the result is disposed, for the stall log.</summary>
    public static Mark Doing(string what)
    {
        string before = doing;
        doing = what;
        return new Mark(before);
    }

    public readonly struct Mark(string before) : IDisposable
    {
        public void Dispose() => doing = before;
    }

    public static void Start(Dispatcher ui)
    {
        var thread = new Thread(() => Watch(ui)) { IsBackground = true, Name = "Veneer stall watch", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    /// <summary>The slowest answer of the UI thread, for the exit log (sampled once a second: shorter pauses can slip by).</summary>
    public static string Summary => $"slowest UI answer {Interlocked.Read(ref longestMs)} ms{(longestDoing.Length > 0 ? $" ({longestDoing})" : "")}";

    static void Watch(Dispatcher ui)
    {
        var clock = new Stopwatch();
        while (!ui.HasShutdownStarted)
        {
            Thread.Sleep(Every);
            clock.Restart();
            var ping = ui.BeginInvoke(DispatcherPriority.Send, () => { });
            var status = ping.Wait(Limit);
            if (status == DispatcherOperationStatus.Completed)
            {
                Record(clock.ElapsedMilliseconds, doing);
                continue;
            }

            if (status == DispatcherOperationStatus.Aborted || ui.HasShutdownStarted)
                return; // exiting, not stalled

            string what = doing;
            Log.Write($"stall: the UI thread has not answered for {Limit.TotalSeconds:0} s (doing: {what})");
            if (ping.Wait() != DispatcherOperationStatus.Completed) // returns when it answers, or when the dispatcher shuts down
                return;
            Log.Write($"stall: the UI thread answered after {clock.ElapsedMilliseconds} ms");
            Record(clock.ElapsedMilliseconds, what);
        }
    }

    static void Record(long ms, string what)
    {
        if (ms <= Interlocked.Read(ref longestMs))
            return;
        longestDoing = what;
        Interlocked.Exchange(ref longestMs, ms);
    }
}
