using System.Windows;
using System.Windows.Controls;
using Veneer.Core.Dock;
using Veneer.Core.Themes;

namespace Veneer.App;

/// <summary>
/// The offer to replace the dock's pinned apps with the Windows taskbar's: each app with its icon and name, then
/// Import or Not now. Not modal, so the shell keeps running while it is open.
/// </summary>
sealed class PinImportWindow : VeneerWindow
{
    readonly Action import;

    public PinImportWindow(Theme theme, IReadOnlyList<string> entries, Action import)
    {
        this.import = import;
        Title = Strings.PinImportTitle;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var apps = new StackPanel();
        foreach (string entry in entries)
        {
            // The same key and icon cache as the dock's pinned cells.
            var item = new DockItem("pin:" + entry.ToLowerInvariant(), entry, null);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(new Image { Source = DockIcons.For(theme, item, 96), Width = 28, Height = 28, Margin = new Thickness(0, 0, 12, 0) });
            row.Children.Add(new TextBlock { Text = DockIcons.Name(item), VerticalAlignment = VerticalAlignment.Center });
            apps.Children.Add(row);
        }

        var importButton = new Button { Content = Strings.PinImportImport, IsDefault = true, MinWidth = 100, Margin = new Thickness(0, 0, 8, 0) };
        importButton.SetResourceReference(StyleProperty, "AccentButtonStyle");
        importButton.Click += (_, _) => Import();
        var notNow = new Button { Content = Strings.PinImportNotNow, IsCancel = true, MinWidth = 100 };
        notNow.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        buttons.Children.Add(importButton);
        buttons.Children.Add(notNow);

        var panel = new StackPanel { Margin = new Thickness(24), Width = 420 };
        panel.Children.Add(new TextBlock { Text = Strings.PinImportHeading, FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = Strings.PinImportNote, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 8, 0, 12) });
        panel.Children.Add(new ScrollViewer { Content = apps, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        panel.Children.Add(buttons);
        Body = panel;
    }

    /// <summary>Imports and closes (the Import button, and the self-test).</summary>
    public void Import()
    {
        import();
        Close();
    }
}
