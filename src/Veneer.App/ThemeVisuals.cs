using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Veneer.Core;
using Veneer.Core.Themes;

namespace Veneer.App;

/// <summary>Turns theme values into WPF brushes, fonts and text settings.</summary>
static class ThemeVisuals
{
    static readonly Dictionary<string, FontFamily?> FontCache = [];

    /// <summary>
    /// A border brush: solid, or a vertical gradient from <paramref name="highlight"/> (top) to <paramref name="border"/>
    /// (bottom) when the theme gives a highlight, like light on the edge of glass.
    /// </summary>
    public static Brush? BorderBrush(ThemeColor? border, ThemeColor? highlight)
    {
        if (highlight is not { } top)
            return Brush(border);
        var bottom = border ?? new ThemeColor(0, top.R, top.G, top.B);
        var brush = new LinearGradientBrush(Color.FromArgb(top.A, top.R, top.G, top.B), Color.FromArgb(bottom.A, bottom.R, bottom.G, bottom.B), 90);
        brush.Freeze();
        return brush;
    }

    /// <summary>The theme the surfaces are built from; menus and labels take their style from it.</summary>
    public static Theme? Current { get; set; }

    static string Hex(ThemeColor? c, string fallback) => c is { } v ? v.ToString() : fallback;

    /// <summary>
    /// Gives a menu the theme's look (theme <c>menu</c>). Without one, the Windows 11 look stays. Templates are
    /// built from XAML text with the theme's values filled in: the shortest way to express a ControlTemplate.
    /// </summary>
    public static void StyleMenu(System.Windows.Controls.ContextMenu menu)
    {
        if (Current is not { Menu: { } style } theme)
            return;
        try
        {
            ApplyMenuStyle(menu, theme, style);
        }
        catch (Exception e) when (e is System.Windows.Markup.XamlParseException or InvalidOperationException or FormatException)
        {
            // A theme value that WPF cannot use: keep the Windows look rather than lose the shell.
            Log.Write($"menu style not applied: {e.Message}");
        }
    }

    static void ApplyMenuStyle(System.Windows.Controls.ContextMenu menu, Theme theme, PopupStyle style)
    {
        const string ns = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'";
        string bg = Hex(style.Background, "#F2F6F6F6"), border = Hex(style.Border, "#00000000");
        string borderBrush = style.BorderHighlight is { } top
            ? $"<Border.BorderBrush><LinearGradientBrush StartPoint='0,0' EndPoint='0,1'><GradientStop Color='{top}' Offset='0'/><GradientStop Color='{border}' Offset='1'/></LinearGradientBrush></Border.BorderBrush>"
            : "";
        string highlight = Hex(style.Highlight, "#FF0A84FF"), highlightFg = Hex(style.HighlightForeground, "#FFFFFFFF");
        string separator = Hex(style.Separator, "#26000000");
        double r = style.CornerRadius, p = style.Padding, itemRadius = Math.Max(0, r - p);
        int bt = style.Border is null && style.BorderHighlight is null ? 0 : 1;
        // A WPF shadow needs room around the menu; with blur the room would be blurred too, so blurred menus have none.
        string margin = style.Shadow && !style.Blur ? "10" : "0";
        string effect = style.Shadow && !style.Blur ? "<Border.Effect><DropShadowEffect BlurRadius='16' ShadowDepth='3' Opacity='0.25'/></Border.Effect>" : "";

        menu.Template = Parse<System.Windows.Controls.ControlTemplate>($@"
<ControlTemplate {ns} TargetType='ContextMenu'>
  <Border Margin='{margin}' Background='{bg}' BorderBrush='{border}' BorderThickness='{bt}' CornerRadius='{r}' Padding='{p}'>
    {borderBrush}
    {effect}
    <ItemsPresenter/>
  </Border>
</ControlTemplate>");
        menu.HasDropShadow = false;
        var itemStyle = Parse<Style>($@"
<Style {ns} TargetType='MenuItem'>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='MenuItem'>
        <Border x:Name='bd' Background='Transparent' CornerRadius='{itemRadius}' Padding='10,3,24,3'>
          <ContentPresenter ContentSource='Header' RecognizesAccessKey='True'/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsHighlighted' Value='True'>
            <Setter TargetName='bd' Property='Background' Value='{highlight}'/>
            <Setter Property='Foreground' Value='{highlightFg}'/>
          </Trigger>
          <Trigger Property='IsEnabled' Value='False'>
            <Setter Property='Opacity' Value='0.45'/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>");
        // Per item: ItemContainerStyle would also be applied to the separators, which WPF refuses.
        foreach (var item in menu.Items.OfType<System.Windows.Controls.MenuItem>())
            item.Style = itemStyle;
        menu.Resources[System.Windows.Controls.MenuItem.SeparatorStyleKey] = Parse<Style>($@"
<Style {ns} TargetType='Separator'>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='Separator'>
        <Rectangle Height='1' Margin='10,4' Fill='{separator}'/>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>");
        TextElement.SetFontFamily(menu, new FontFamily("Segoe UI Variable Text, Segoe UI"));
        TextElement.SetFontSize(menu, 13);
        ApplyText(menu, theme, style.Text, style.Foreground ?? new ThemeColor(0xFF, 0x1D, 0x1D, 0x1F));

        if (style.Blur)
        {
            menu.Opened += (_, _) =>
            {
                if (System.Windows.PresentationSource.FromVisual(menu) is System.Windows.Interop.HwndSource source)
                    Veneer.Interop.SurfaceWindow.ApplyPopupBlur(source.Handle, 0x01FFFFFF, round: r > 0);
            };
        }
    }

    static T Parse<T>(string xaml) => (T)System.Windows.Markup.XamlReader.Parse(xaml);

    public static Brush? Brush(ThemeColor? color)
    {
        if (color is not { } c)
            return null;
        var brush = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Loads a theme or system image without keeping the file open. A damaged or unreadable file gives null (and a
    /// log line), never an exception: a bad theme image must not take the shell down.
    /// </summary>
    public static ImageSource? LoadImage(string path)
    {
        try
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; // decodes now, file closed after
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or FileFormatException or UriFormatException or ArgumentException or UnauthorizedAccessException)
        {
            Log.Write($"image {path}: {e.Message}");
            return null;
        }
    }

    /// <summary>Sets the inheritable text properties on <paramref name="element"/>; null values keep the inherited ones.</summary>
    public static void ApplyText(DependencyObject element, Theme theme, TextStyle text, ThemeColor? foreground)
    {
        if (Brush(foreground) is { } brush)
            TextElement.SetForeground(element, brush);
        if (text.Font is { } font && Font(theme, font) is { } family)
            TextElement.SetFontFamily(element, family);
        if (text.FontSize is { } size)
            TextElement.SetFontSize(element, size);
        if (text.FontWeight is { } weight)
            TextElement.SetFontWeight(element, FontWeight.FromOpenTypeWeight(weight));
    }

    /// <summary>A key of the theme's <c>fonts</c> map (a font file in the theme) or an installed family name.</summary>
    static FontFamily? Font(Theme theme, string font)
    {
        if (!theme.Fonts.TryGetValue(font, out string? file))
            return new FontFamily(font);

        lock (FontCache)
        {
            if (FontCache.TryGetValue(file, out var cached))
                return cached;
            FontFamily? family = null;
            try
            {
                // WPF loads a font file through its folder plus "#<family name>"; the name is read from the file.
                var face = new GlyphTypeface(new Uri(file));
                string name = face.FamilyNames.TryGetValue(System.Globalization.CultureInfo.GetCultureInfo("en-US"), out var en)
                    ? en
                    : face.FamilyNames.Values.First();
                family = new FontFamily(new Uri(Path.GetDirectoryName(file) + Path.DirectorySeparatorChar), "./#" + name);
            }
            catch (Exception e) when (e is IOException or UriFormatException or NotSupportedException or FileFormatException)
            {
                Log.Write($"font \"{font}\" ({file}) could not be loaded: {e.Message}");
            }

            FontCache[file] = family;
            return family;
        }
    }
}
