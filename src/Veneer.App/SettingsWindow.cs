using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Veneer.Core;
using Veneer.Core.Settings;
using Veneer.Core.Themes;
using Veneer.Interop;

namespace Veneer.App;

/// <summary>
/// Veneer's settings window, built in code like the rest of the app, in the Windows 11 (Fluent) style. Every change
/// goes through the same files as the menus (the profile's <c>overrides.json</c> and <c>windows.json</c>, the profile
/// store), so hot reload applies it and the window rebuilds its page on <see cref="App.Changed"/>.
/// </summary>
sealed class SettingsWindow : VeneerWindow
{
    readonly App app;
    // Selection like the Windows 11 navigation pane: a subtle fill and a small accent bar, not a solid accent block.
    static readonly Style ListItemStyle = (Style)XamlReader.Parse("""
        <Style TargetType="ListBoxItem" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
          <Setter Property="Padding" Value="12,0,12,0"/>
          <Setter Property="MinHeight" Value="36"/>
          <Setter Property="Margin" Value="0,1,0,1"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="ListBoxItem">
                <Border x:Name="Back" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" CornerRadius="4" Background="Transparent">
                  <Grid>
                    <Border x:Name="Bar" Width="3" Height="16" CornerRadius="1.5" HorizontalAlignment="Left" Visibility="Collapsed"
                            Background="{DynamicResource AccentFillColorDefaultBrush}"/>
                    <ContentPresenter Margin="{TemplateBinding Padding}" VerticalAlignment="Center"/>
                  </Grid>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="Back" Property="Background" Value="{DynamicResource SubtleFillColorSecondaryBrush}"/>
                  </Trigger>
                  <Trigger Property="IsSelected" Value="True">
                    <Setter TargetName="Back" Property="Background" Value="{DynamicResource SubtleFillColorSecondaryBrush}"/>
                    <Setter TargetName="Bar" Property="Visibility" Value="Visible"/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """);

    static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    readonly ListBox nav = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent, Margin = new Thickness(8, 4, 8, 16), ItemContainerStyle = ListItemStyle };
    readonly ScrollViewer content = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(28, 4, 32, 32) };
    readonly (string Id, string Title, string Icon, Func<UIElement> Build)[] pages;
    (string Text, bool Error)? status; // a line shown once at the top of the page: an error, "Copied."
    string? selectedProfile;
    ThemeInstallResult? installResult;
    bool installing;
    readonly List<DispatcherTimer> pendingSaves = []; // slider saves waiting for the keys to go quiet

    public SettingsWindow(App app)
    {
        this.app = app;
        Title = Strings.SettingsTitle;
        Width = 920;
        Height = 660;
        MinWidth = 640;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        pages =
        [
            ("general", Strings.PageGeneral, "\uE713", General),
            ("profiles", Strings.PageProfiles, "\uE77B", ProfilesPage),
            ("theme", Strings.PageTheme, "\uE790", ThemePage),
            ("customize", Strings.PageCustomize, "\uE771", Customize),
            ("windows", Strings.PageWindows, "\uE7F4", WindowsPage),
            ("diagnostics", Strings.PageDiagnostics, "\uE9D9", DiagnosticsPage),
        ];
        foreach (var page in pages)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal };
            item.Children.Add(new TextBlock { Text = page.Icon, FontFamily = IconFont, FontSize = 16, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center });
            item.Children.Add(new TextBlock { Text = page.Title, VerticalAlignment = VerticalAlignment.Center });
            nav.Items.Add(new ListBoxItem { Content = item, Tag = page.Id });
            AutomationProperties.SetName((ListBoxItem)nav.Items[^1]!, page.Title);
        }
        nav.SelectionChanged += (_, _) =>
        {
            status = null;
            Build();
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(nav);
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
        Body = grid;

        app.Changed += Build;
        app.PinsImported += OnPinsImported;
        Closed += (_, _) =>
        {
            app.Changed -= Build;
            app.PinsImported -= OnPinsImported;
            StopPendingSaves();
        };
    }

    /// <summary>Shows the window on <paramref name="page"/> (an id such as <c>customize</c>), or on the page it was on.</summary>
    public void ShowPage(string? page)
    {
        int index = page is null ? -1 : Array.FindIndex(pages, p => p.Id == page);
        nav.SelectedIndex = index >= 0 ? index : Math.Max(0, nav.SelectedIndex);
        Build();
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    void Build()
    {
        StopPendingSaves(); // a save queued by the old page must not write a stale value after a reset or reload
        if (nav.SelectedIndex < 0)
            return;
        var page = pages[nav.SelectedIndex];
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = page.Title, FontSize = 28, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
        if (status is { } s)
        {
            var line = Note(s.Text);
            line.Opacity = 1;
            if (s.Error)
                line.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
            panel.Children.Add(line);
        }

        panel.Children.Add(page.Build());
        // Full width up to 820, kept on the left like Windows Settings (a MaxWidth alone would center the page).
        var column = new Grid();
        column.ColumnDefinitions.Add(new ColumnDefinition { MaxWidth = 820 });
        column.Children.Add(panel);
        content.Content = column;
    }

    // ----- pages -----

    UIElement General()
    {
        var autostart = new CheckBox { IsChecked = Autostart.IsEnabled, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(autostart, Strings.StartWithWindows);
        autostart.Click += (_, _) => Act(() => Autostart.Set(autostart.IsChecked == true));
        bool snapshot = app.HasWindowsSnapshot;
        return Stack(
            Card(Labeled(Strings.StartWithWindows), autostart, null),
            Section(Strings.TaskbarSection),
            Card(Labeled(Strings.TaskbarNote), null, Button(Strings.TaskbarShow, app.RestoreTaskbar)),
            Section(Strings.WindowsSettingsSection),
            Card(Labeled(snapshot ? Strings.WindowsSettingsNote : Strings.WindowsSettingsNothing), null,
                Button(Strings.MenuRestoreWindowsSettings, () => app.RestoreWindowsSettings().ContinueWith(_ => Dispatcher.BeginInvoke(Build)), snapshot)),
            Section(Strings.ShortcutsSection),
            Shortcuts(Strings.ShortcutQuit, Strings.ShortcutTaskbar, Strings.ShortcutProfile, Strings.ShortcutEmergency));
    }

    // "Keys: what they do" lines as a two-column card: the keys line up on the left.
    static Border Shortcuts(params string[] lines)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < lines.Length; i++)
        {
            int colon = lines[i].IndexOf(": ", StringComparison.Ordinal);
            grid.RowDefinitions.Add(new RowDefinition());
            var keys = new TextBlock { Text = colon < 0 ? "" : lines[i][..colon], FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 24, 4) };
            var what = new TextBlock { Text = colon < 0 ? lines[i] : lines[i][(colon + 2)..], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
            Grid.SetRow(keys, i);
            Grid.SetRow(what, i);
            Grid.SetColumn(what, 1);
            grid.Children.Add(keys);
            grid.Children.Add(what);
        }

        return CardBorder(grid);
    }

    UIElement ProfilesPage()
    {
        string? active = app.ActiveProfile;
        if (active is null)
            return Stack(Note(Strings.ProfilesOff));

        var list = List(300);
        var names = app.Profiles.Names();
        if (selectedProfile is null || !names.Contains(selectedProfile))
            selectedProfile = active;
        foreach (string name in names)
            list.Items.Add(new ListBoxItem { Content = name == active ? Strings.ProfileActive(name) : name, Tag = name, IsSelected = name == selectedProfile });

        var use = Button(Strings.ProfileUse, () => app.SwitchProfile(selectedProfile!));
        use.SetResourceReference(StyleProperty, "AccentButtonStyle");
        var delete = Button(Strings.ProfileDelete, () =>
        {
            if (Modules.Confirm(Strings.ProfileDeleteConfirm(selectedProfile!)))
                Act(() => Report(app.Profiles.Delete(selectedProfile!)));
        });
        void Enable()
        {
            use.IsEnabled = selectedProfile != active;
            delete.IsEnabled = selectedProfile != active;
        }

        list.SelectionChanged += (_, _) =>
        {
            selectedProfile = (list.SelectedItem as ListBoxItem)?.Tag as string ?? active;
            Enable();
        };
        Enable();

        var buttons = new WrapPanel();
        buttons.Children.Add(use);
        buttons.Children.Add(Button(Strings.ProfileNew, () =>
        {
            if (Ask(Strings.ProfileNewTitle, "") is { } name)
                Act(() => Report(app.Profiles.Create(name), select: name));
        }));
        buttons.Children.Add(Button(Strings.ProfileDuplicate, () =>
        {
            if (Ask(Strings.ProfileDuplicateTitle, Strings.ProfileCopyName(selectedProfile!)) is { } name)
                Act(() => Report(app.Profiles.Duplicate(selectedProfile!, name), select: name));
        }));
        buttons.Children.Add(Button(Strings.ProfileRename, () =>
        {
            if (Ask(Strings.ProfileRenameTitle, selectedProfile!) is { } name)
                RenameProfile(selectedProfile!, name);
        }));
        buttons.Children.Add(delete);
        return Stack(Note(Strings.ProfilesNote), list, buttons);
    }

    /// <summary>Renames a profile; the active one is loaded again from its new folder (also used by the self-test).</summary>
    public void RenameProfile(string old, string name) => Act(() =>
    {
        bool wasActive = old == app.ActiveProfile;
        string? error = app.Profiles.Rename(old, name);
        if (error is null && wasActive)
            app.ReloadSession(); // its files moved with the folder
        Report(error, select: name);
    });

    UIElement ThemePage()
    {
        string? profile = app.ActiveProfile;
        var list = List(260);
        list.IsEnabled = profile is not null;
        var themes = app.InstalledThemes();
        foreach (var theme in themes)
        {
            bool current = string.Equals(theme.Folder, app.ThemeFolder, StringComparison.OrdinalIgnoreCase);
            // Two themes may call themselves the same (an installed copy of a bundled one): the folder tells them apart.
            string text = themes.Count(t => t.DisplayName == theme.DisplayName) > 1 ? $"{theme.DisplayName} ({theme.Name})" : theme.DisplayName;
            list.Items.Add(new ListBoxItem { Content = text, Tag = theme, IsSelected = current, ToolTip = theme.Folder });
        }

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is ListBoxItem { Tag: ThemeEntry theme } && !string.Equals(theme.Folder, app.ThemeFolder, StringComparison.OrdinalIgnoreCase))
                app.SetTheme(theme.Name);
        };

        var page = Stack(Note(profile is null ? Strings.ProfilesOff : Strings.ThemeNote(profile)), list, Button(Strings.ThemeInstall, InstallTheme, !installing));
        if (installing)
            page.Children.Add(Note(Strings.ThemeInstalling));
        else if (installResult is { } result)
        {
            page.Children.Add(Note(result.Success ? Strings.ThemeInstalled(themes.FirstOrDefault(t => string.Equals(t.Folder, result.InstalledDirectory, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? Path.GetFileName(result.InstalledDirectory)!) : Strings.ThemeNotInstalled));
            foreach (var d in result.Diagnostics)
                page.Children.Add(Problem(d));
        }

        page.Children.Add(Section(Strings.ThemeProblems));
        var load = app.LastLoad;
        if (load is null || load.Diagnostics.Count == 0)
        {
            page.Children.Add(Note(Strings.ThemeNoProblems));
            return page;
        }

        if (!load.Success)
            page.Children.Add(Note(Strings.ThemeNotLoaded));
        page.Children.Add(Note(Strings.ThemeProblemsNote));
        foreach (var d in load.Diagnostics)
            page.Children.Add(Problem(d));
        return page;
    }

    void InstallTheme()
    {
        var dialog = new OpenFileDialog { Filter = Strings.ThemeZipFilter };
        if (dialog.ShowDialog(this) != true)
            return;
        InstallFrom(dialog.FileName);
    }

    /// <summary>Installs a theme .zip off the UI thread and shows the outcome on the Theme page (also used by the self-test).</summary>
    public void InstallFrom(string zip)
    {
        installing = true;
        installResult = null;
        Build();
        Task.Run(() =>
        {
            try
            {
                return ThemeArchive.Install(zip, AppPaths.Themes);
            }
            catch (Exception e)
            {
                return new ThemeInstallResult(null, [Diagnostic.Error("archive", e.Message)]);
            }
        }).ContinueWith(t => Dispatcher.BeginInvoke(() =>
        {
            installing = false;
            installResult = t.Result;
            Log.Write($"theme install from .zip: {(t.Result.Success ? t.Result.InstalledDirectory : "failed")}");
            Build();
        }));
    }

    UIElement Customize()
    {
        if (app.ThemeFolder is null)
            return Stack(Note(Strings.CustomizeBuiltIn));
        var theme = app.CurrentTheme;
        var page = Stack(Note(Strings.CustomizeNote(app.ActiveProfile ?? Path.GetFileName(Path.GetDirectoryName(app.OverridesPath))!)));
        foreach (var surface in theme.Surfaces)
        {
            page.Children.Add(Section(surface is DockSurface ? Strings.SurfaceDock(surface.Id) : Strings.SurfaceBar(surface.Id)));
            foreach (var setting in SurfaceSettings.For(theme, surface))
                page.Children.Add(SettingRow(setting));
        }

        if (theme.Surfaces.OfType<DockSurface>().Any())
        {
            page.Children.Add(Section(Strings.PinImportSection));
            page.Children.Add(Note(Strings.PinImportSettingsNote));
            page.Children.Add(Button(Strings.PinImportButton, () =>
            {
                status = app.OfferPinImport() ? null : (Strings.PinImportNothing, false);
                Build();
            }));
        }

        var orphans = app.LastLoad?.OrphanOverrides ?? [];
        if (orphans.Count == 0)
            return page;
        page.Children.Add(Section(Strings.OrphansSection));
        page.Children.Add(Note(Strings.OrphansNote));
        foreach (string path in orphans)
        {
            string id = path["surfaces[id=".Length..^1];
            page.Children.Add(Card(Labeled(id), null,
                Button(Strings.OrphanRemove, () => Saved(OverridesFile.TryRemoveSurface(app.OverridesPath, id, out string? error), error))));
        }

        return page;
    }

    FrameworkElement SettingRow(SurfaceSetting setting)
    {
        FrameworkElement control = setting.Kind switch
        {
            SettingKind.Toggle => Toggle(setting),
            SettingKind.Choice => Choice(setting),
            _ => Number(setting),
        };
        string origin = setting.Origin switch
        {
            ThemeLayer.User => Strings.OriginYours,
            null => Strings.OriginDefault,
            _ => Strings.OriginTheme,
        };
        // Reset only where there is something to reset: a column of disabled buttons said nothing.
        var reset = setting.IsYours ? Button(Strings.Reset, () => Set(setting, null)) : null;
        reset?.ToolTip = Strings.ResetTip;
        return Card(Labeled(Label(setting.Key), origin), control, reset);
    }

    FrameworkElement Toggle(SurfaceSetting setting)
    {
        var box = new CheckBox { IsChecked = (bool)setting.Value, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(box, Label(setting.Key));
        box.Click += (_, _) => Set(setting, box.IsChecked == true);
        return box;
    }

    FrameworkElement Choice(SurfaceSetting setting)
    {
        var combo = new ComboBox { MinWidth = 160 };
        AutomationProperties.SetName(combo, Label(setting.Key));
        foreach (string choice in setting.Choices!)
            combo.Items.Add(new ComboBoxItem { Content = EdgeLabel(choice), Tag = choice, IsSelected = choice == (string)setting.Value });
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: string value })
                Set(setting, value);
        };
        return combo;
    }

    FrameworkElement Number(SurfaceSetting setting)
    {
        double initial = Convert.ToDouble(setting.Value);
        var slider = new Slider
        {
            Minimum = setting.Min,
            Maximum = setting.Max,
            Value = initial,
            SmallChange = setting.Step,
            LargeChange = setting.Step * 10,
            TickFrequency = setting.Step,
            IsSnapToTickEnabled = true,
            Width = 170,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(slider, Label(setting.Key));
        string Text(double v) => v.ToString(setting.Step < 1 ? "0.0" : "0", System.Globalization.CultureInfo.CurrentCulture);
        var value = new TextBlock { Text = Text(initial), Width = 44, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

        // Saved when the drag ends, or shortly after the last key press or click on the track: every save reloads
        // the theme and rebuilds the bars, which must not happen at every pixel of a drag.
        bool dragging = false;
        var quiet = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        pendingSaves.Add(quiet);
        quiet.Tick += (_, _) =>
        {
            quiet.Stop();
            Set(setting, slider.Value);
        };
        slider.ValueChanged += (_, _) =>
        {
            value.Text = Text(slider.Value);
            if (!dragging)
            {
                quiet.Stop();
                quiet.Start();
            }
        };
        slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => dragging = true));
        slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) =>
        {
            dragging = false;
            Set(setting, slider.Value);
        }));
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(slider);
        panel.Children.Add(value);
        return panel;
    }

    /// <summary>Writes one value into the profile's overrides (null: back to the theme's value). Also used by the self-test.</summary>
    public void Set(SurfaceSetting setting, object? value)
    {
        StopPendingSaves();
        if (value is not null && Equals(setting.ToJson(value).ToJsonString(), setting.ToJson(setting.Value).ToJsonString()))
            return; // unchanged: do not turn the theme's value into "yours"
        JsonNode? json = value is null ? null : setting.ToJson(value);
        string? error = null;
        bool ok = value is null || (setting.WriteWith ?? []).All(w => OverridesFile.TrySetSurfaceValue(app.OverridesPath, w.SurfaceId, w.Key, w.ToJson(w.Value), out error));
        ok = ok && OverridesFile.TrySetSurfaceValue(app.OverridesPath, setting.SurfaceId, setting.Key, json, out error);
        Saved(ok, error);
    }

    void StopPendingSaves()
    {
        foreach (var timer in pendingSaves)
            timer.Stop();
        pendingSaves.Clear();
    }

    // On success the theme reloads and App.Changed rebuilds the page; only a failure needs showing here.
    void Saved(bool ok, string? error)
    {
        if (ok)
            return;
        status = (Strings.NotSaved(error ?? ""), true);
        Build();
    }

    UIElement WindowsPage()
    {
        var prefs = app.Preferences();
        var windows = app.CurrentTheme.Windows;
        var page = Stack(Note(Strings.WindowsNote));

        var sources = new[] { (SettingSource.Theme, Strings.ChoiceFromTheme), (SettingSource.System, Strings.ChoiceLeave) };
        page.Children.Add(WindowsRow(Strings.WindowsWallpaper, sources, prefs.Wallpaper, windows.Wallpaper is { } w ? Path.GetFileName(w) : null,
            v => prefs with { Wallpaper = v }));
        page.Children.Add(WindowsRow(Strings.WindowsAccent, sources, prefs.Accent, windows.AccentColor?.ToString(), v => prefs with { Accent = v }));
        string? appearance = windows.Appearance switch
        {
            Appearance.Light => Strings.AppearanceLight,
            Appearance.Dark => Strings.AppearanceDark,
            Appearance.System => Strings.AppearanceSystem,
            _ => null,
        };
        page.Children.Add(WindowsRow(Strings.WindowsAppearance,
            [(AppearanceChoice.Theme, Strings.ChoiceFromTheme), (AppearanceChoice.Light, Strings.ChoiceLight), (AppearanceChoice.Dark, Strings.ChoiceDark), (AppearanceChoice.System, Strings.ChoiceFollow)],
            prefs.Appearance, appearance, v => prefs with { Appearance = v }));
        page.Children.Add(Note(Strings.WindowsRestoreNote));
        return page;
    }

    FrameworkElement WindowsRow<T>(string label, (T Value, string Text)[] choices, T current, string? themeValue, Func<T, WindowsPreferences> change) where T : struct, Enum
    {
        var combo = new ComboBox { MinWidth = 220 };
        foreach (var (value, text) in choices)
            combo.Items.Add(new ComboBoxItem { Content = text, Tag = value, IsSelected = value.Equals(current) });
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: T value } && !value.Equals(current))
            {
                Act(() =>
                {
                    AtomicFile.Write(app.PreferencesPath, change(value).Serialize());
                    app.PreferencesChanged();
                });
            }
        };
        AutomationProperties.SetName(combo, label);
        return Card(Labeled(label, themeValue is null ? Strings.ThemeSetsNothing : Strings.ThemeSets(themeValue)), combo, null);
    }

    UIElement DiagnosticsPage()
    {
        string report = DiagnosticsText();
        var box = new TextBox
        {
            Text = report,
            IsReadOnly = true,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12,
            Height = 380,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 8, 0, 8),
        };
        var buttons = new WrapPanel();
        buttons.Children.Add(Button(Strings.Copy, () =>
        {
            try
            {
                Clipboard.SetText(report);
                status = (Strings.Copied, false);
            }
            catch (System.Runtime.InteropServices.ExternalException e)
            {
                status = (e.Message, true); // another app holds the clipboard
            }

            Build();
        }));
        buttons.Children.Add(Button(Strings.Refresh, () =>
        {
            status = null;
            Build();
        }));
        return Stack(Note(Strings.DiagnosticsNote), box, buttons);
    }

    /// <summary>The window's area on screen in physical pixels (for the self-test's screenshots).</summary>
    public Veneer.Core.Layout.PixelRect ScreenBounds()
    {
        // The drawn area (title bar included), also when maximized, where Left and Top keep the restored position.
        var content = (FrameworkElement)Content;
        var topLeft = content.PointToScreen(new Point(0, 0));
        var bottomRight = content.PointToScreen(new Point(content.ActualWidth, content.ActualHeight));
        return new((int)topLeft.X, (int)topLeft.Y, (int)bottomRight.X, (int)bottomRight.Y);
    }

    /// <summary>The diagnostics report, with personal paths and names hidden.</summary>
    public string DiagnosticsText()
    {
        string version = typeof(SettingsWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        var monitors = Monitors.All()
            .Select(m => $"{m.Info.Bounds.Width}x{m.Info.Bounds.Height} at {m.Info.Dpi} dpi ({m.Info.Dpi * 100 / 96}%){(m.Primary ? ", primary" : "")}")
            .ToList();
        var info = new ReportInfo(version, WindowsVersion(), monitors, app.ActiveProfile, app.CurrentTheme.Id, app.ThemeFolder,
            app.LastLoad?.Diagnostics ?? [], Log.Tail(40));
        return DiagnosticsReport.Build(info, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName, Environment.MachineName);
    }

    static string WindowsVersion()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        string Value(string name) => key?.GetValue(name)?.ToString() ?? "?";
        string windows = int.TryParse(Value("CurrentBuildNumber"), out int build) && build >= 22000 ? "Windows 11" : "Windows";
        return $"{windows} {Value("EditionID")} {Value("DisplayVersion")}, build {Value("CurrentBuildNumber")}.{Value("UBR")}";
    }

    void OnPinsImported(int count, string? error)
    {
        status = error is null ? (Strings.PinImportDone(count), false) : (Strings.NotSaved(error), true);
        Build();
    }

    // ----- small pieces -----

    // Runs a change; a locked or unwritable file becomes a message on the page instead of an exception.
    void Act(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            status = (Strings.NotSaved(e.Message), true);
            Build();
        }
    }

    void Report(string? error, string? select = null)
    {
        status = error is null ? null : (error, true);
        if (error is null && select is not null)
            selectedProfile = select;
        Build();
    }

    string? Ask(string title, string initial)
    {
        var box = new TextBox { Text = initial, MinWidth = 300, Margin = new Thickness(0, 6, 0, 16) };
        var ok = new Button { Content = Strings.Ok, IsDefault = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = Strings.Cancel, IsCancel = true, MinWidth = 90 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = Strings.ProfileNamePrompt });
        panel.Children.Add(box);
        panel.Children.Add(buttons);
        var dialog = new VeneerWindow
        {
            Title = title,
            Owner = this,
            Body = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        ok.Click += (_, _) => dialog.DialogResult = true;
        dialog.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        return dialog.ShowDialog() == true && box.Text.Trim() is { Length: > 0 } name ? name : null;
    }

    static string Label(string key) => key switch
    {
        "edge" => Strings.SettingEdge,
        "thickness" => Strings.SettingThickness,
        "reserveSpace" => Strings.SettingReserveSpace,
        "iconSize" => Strings.SettingIconSize,
        "magnification.enabled" => Strings.SettingMagnification,
        "magnification.maxScale" => Strings.SettingMagnificationScale,
        "autoHide" => Strings.SettingAutoHide,
        _ => key,
    };

    static string EdgeLabel(string edge) => edge switch
    {
        "top" => Strings.EdgeTop,
        "bottom" => Strings.EdgeBottom,
        "left" => Strings.EdgeLeft,
        "right" => Strings.EdgeRight,
        _ => edge,
    };

    static StackPanel Stack(params UIElement[] children)
    {
        var panel = new StackPanel();
        foreach (var child in children)
            panel.Children.Add(child);
        return panel;
    }

    static TextBlock Section(string text) =>
        new() { Text = text, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 24, 0, 8) };

    static TextBlock Note(string text) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 2, 0, 6) };

    static TextBlock Problem(Diagnostic d)
    {
        var line = new TextBlock
        {
            Text = $"{(d.Severity == Severity.Error ? Strings.SeverityError : Strings.SeverityWarning)}: {d.Path}: {d.Message}",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 2),
        };
        if (d.Severity == Severity.Error)
            line.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        return line;
    }

    static Button Button(string text, Action click, bool enabled = true)
    {
        var button = new Button { Content = text, IsEnabled = enabled, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 6, 8, 6), HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>A settings row on a card: name (with its description), control, and an action button.</summary>
    static Border Card(FrameworkElement name, FrameworkElement? control, FrameworkElement? action)
    {
        var grid = new Grid { MinHeight = 44 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        // Controls line up from row to row; a row without one gives the space to its text.
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = control is null ? new GridLength(0) : new GridLength(260) });
        // Room for Reset on every row with a control, so a row that shows it does not push its control left.
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = control is null ? 0 : 110 });
        FrameworkElement?[] cells = [name, control, action];
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] is not { } cell)
                continue;
            cell.Margin = new Thickness(i == 0 ? 0 : 16, 0, 0, 0);
            cell.VerticalAlignment = VerticalAlignment.Center;
            if (i > 0)
                cell.HorizontalAlignment = i == 1 ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        return CardBorder(grid);
    }

    static Border CardBorder(UIElement child)
    {
        var card = new Border { Child = child, Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(0, 0, 0, 4), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        return card;
    }

    /// <summary>A row's name, with an optional smaller line under it (where the value comes from, what it does).</summary>
    static StackPanel Labeled(string title, string? description = null)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        if (description is not null)
        {
            panel.Children.Add(new TextBlock { Text = description, FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
        }

        return panel;
    }

    /// <summary>A list on a card, for profiles and themes: as tall as its items, up to <paramref name="maxHeight"/>.</summary>
    static ListBox List(double maxHeight)
    {
        var list = new ListBox { MaxHeight = maxHeight, Margin = new Thickness(0, 4, 0, 8), Padding = new Thickness(4), BorderThickness = new Thickness(1), ItemContainerStyle = ListItemStyle };
        list.SetResourceReference(BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        list.SetResourceReference(BorderBrushProperty, "CardStrokeColorDefaultBrush");
        return list;
    }
}
