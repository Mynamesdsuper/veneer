using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Veneer.Core;
using Veneer.Core.Themes;

namespace Veneer.App;

/// <summary>
/// Module views by name. A new module adds one entry here (and its option checks in ThemeBinder.ModuleOptions);
/// the bar itself does not change.
/// </summary>
static class Modules
{
    static readonly Dictionary<string, Func<ModuleSpec, bool, FrameworkElement>> Factories = new()
    {
        ["clock"] = (spec, _) => new ClockModule(spec),
        ["spacer"] = (spec, horizontal) => Spacer(spec, horizontal),
    };

    static readonly HashSet<string> Missing = [];

    public static FrameworkElement? Create(ModuleSpec spec, bool horizontal)
    {
        if (Factories.TryGetValue(spec.Module, out var create))
            return create(spec, horizontal);
        if (Missing.Add(spec.Module))
            Log.Write($"module \"{spec.Module}\" is not implemented yet, skipped");
        return null;
    }

    static FrameworkElement Spacer(ModuleSpec spec, bool horizontal)
    {
        double size = spec.Options["size"]?.GetValue<double>() ?? 8;
        return horizontal ? new Border { Width = size } : new Border { Height = size };
    }
}

/// <summary>Current date/time in the theme's <c>format</c>. Wakes once per minute (per second if the format shows seconds).</summary>
sealed class ClockModule : TextBlock
{
    readonly string format;
    readonly DispatcherTimer timer = new();

    public ClockModule(ModuleSpec spec)
    {
        format = spec.Options["format"]?.GetValue<string>() ?? "HH:mm";
        timer.Tick += (_, _) => Update();
        Loaded += (_, _) =>
        {
            SystemEvents.TimeChanged += OnSystemChange;
            SystemEvents.UserPreferenceChanged += OnSystemChange; // locale and regional format changes
            Update();
        };
        Unloaded += (_, _) =>
        {
            // SystemEvents is static: unsubscribe or the module leaks after a theme reload.
            SystemEvents.TimeChanged -= OnSystemChange;
            SystemEvents.UserPreferenceChanged -= OnSystemChange;
            timer.Stop();
        };
    }

    bool ShowsSeconds => format.Contains('s') || format is "T" or "G" or "F" or "o" or "O" or "r" or "R" or "s" or "u" or "U";

    void OnSystemChange(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Update);

    void Update()
    {
        var now = DateTime.Now;
        Text = now.ToString(format, CultureInfo.CurrentCulture);
        var next = ShowsSeconds
            ? now.AddTicks(TimeSpan.TicksPerSecond - now.Ticks % TimeSpan.TicksPerSecond)
            : now.AddTicks(TimeSpan.TicksPerMinute - now.Ticks % TimeSpan.TicksPerMinute);
        timer.Interval = next - now + TimeSpan.FromMilliseconds(20);
        timer.Start();
    }
}
