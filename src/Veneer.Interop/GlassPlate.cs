using System.Numerics;
using System.Runtime.InteropServices;
using Veneer.Core.Themes;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace Veneer.Interop;

/// <summary>
/// A blurred glass plate with rounded corners of any radius, drawn by the composition engine under a per-pixel
/// transparent window (WPF: AllowsTransparency). DWM's own backdrops (system backdrop, composition-attribute blur)
/// always fill the whole window rectangle and ignore window regions and per-pixel alpha, so above the native 8 px
/// radius they leave square corners. Here a host-backdrop brush is clipped to a rounded rectangle, and the glass tint
/// and the edge are composition shapes on top of it: the window's own WPF content is hidden by this tree.
/// </summary>
public sealed class GlassPlate
{
    static nint queueController; // the UI thread's dispatcher queue, kept for the app's lifetime
    static Compositor? compositor;

    readonly DesktopWindowTarget target;
    readonly CompositionRoundedRectangleGeometry clip, outline;
    readonly float edge;

    /// <param name="fill">Glass tint over the blur.</param>
    /// <param name="border">Edge color; with <paramref name="highlight"/>, the edge fades from it (top) to this (bottom).</param>
    /// <param name="edgePx">Edge thickness in physical pixels (0 for none).</param>
    public GlassPlate(nint hwnd, float cornerRadiusPx, ThemeColor? fill, ThemeColor? border, ThemeColor? highlight, float edgePx)
    {
        EnsureDispatcherQueue();
        compositor ??= new Compositor();
        // A host-backdrop brush draws black until the window opts in (undocumented ACCENT_ENABLE_HOSTBACKDROP).
        SurfaceWindow.SetAccent(hwnd, 5, 0);

        compositor.As<ICompositorDesktopInterop>().CreateDesktopWindowTarget(hwnd, false, out nint raw);
        target = DesktopWindowTarget.FromAbi(raw);
        Marshal.Release(raw); // FromAbi holds its own reference

        var root = compositor.CreateContainerVisual();
        root.RelativeSizeAdjustment = Vector2.One;

        clip = compositor.CreateRoundedRectangleGeometry();
        clip.CornerRadius = new Vector2(cornerRadiusPx);
        var blur = compositor.CreateSpriteVisual();
        blur.RelativeSizeAdjustment = Vector2.One;
        blur.Brush = compositor.CreateHostBackdropBrush();
        blur.Clip = compositor.CreateGeometricClip(clip);
        root.Children.InsertAtTop(blur);

        // The edge is stroked on a rectangle inset by half its thickness, so it lies inside the plate like a WPF Border.
        edge = border is null && highlight is null ? 0 : edgePx;
        outline = compositor.CreateRoundedRectangleGeometry();
        outline.CornerRadius = new Vector2(Math.Max(0, cornerRadiusPx - edge / 2));
        outline.Offset = new Vector2(edge / 2);
        var shape = compositor.CreateSpriteShape(outline);
        if (fill is { } f)
            shape.FillBrush = compositor.CreateColorBrush(ToColor(f));
        if (edge > 0)
        {
            shape.StrokeThickness = edge;
            shape.StrokeBrush = highlight is { } top
                ? Gradient(top, border ?? top with { A = 0 })
                : compositor.CreateColorBrush(ToColor(border!.Value));
        }

        var shapes = compositor.CreateShapeVisual();
        shapes.RelativeSizeAdjustment = Vector2.One;
        shapes.Shapes.Add(shape);
        root.Children.InsertAtTop(shapes);
        target.Root = root;
    }

    /// <summary>Call after every resize, with the window size in physical pixels.</summary>
    public void Resize(int width, int height)
    {
        clip.Size = new Vector2(width, height);
        outline.Size = new Vector2(Math.Max(0, width - edge), Math.Max(0, height - edge));
    }

    static CompositionLinearGradientBrush Gradient(ThemeColor top, ThemeColor bottom)
    {
        var brush = compositor!.CreateLinearGradientBrush();
        brush.StartPoint = new Vector2(0, 0);
        brush.EndPoint = new Vector2(0, 1);
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0, ToColor(top)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(1, ToColor(bottom)));
        return brush;
    }

    static Windows.UI.Color ToColor(ThemeColor c) => Windows.UI.Color.FromArgb(c.A, c.R, c.G, c.B);

    static void EnsureDispatcherQueue()
    {
        if (queueController != 0 || Windows.System.DispatcherQueue.GetForCurrentThread() is not null)
            return;
        var options = new DispatcherQueueOptions { Size = Marshal.SizeOf<DispatcherQueueOptions>(), ThreadType = 2 /* DQTYPE_THREAD_CURRENT */, ApartmentType = 2 /* DQTAT_COM_STA */ };
        Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out queueController));
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DispatcherQueueOptions
    {
        public int Size;
        public int ThreadType;
        public int ApartmentType;
    }

    [DllImport("CoreMessaging.dll")]
    static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint controller);

    [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICompositorDesktopInterop
    {
        void CreateDesktopWindowTarget(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool isTopmost, out nint target);

        void EnsureOnThread(uint threadId);
    }
}
