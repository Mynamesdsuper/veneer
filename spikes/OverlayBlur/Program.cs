// Phase 0 spike: borderless, topmost, non-activating WPF overlay with real blur and rounded corners.
// Throwaway code. Usage:
//   OverlayBlur --mode dwm|acrylic|blur [--rgn] [--tint AARRGGBB] [--seconds N] [--nobg]
//   dwm     = DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE = transient/acrylic), AllowsTransparency=False
//   acrylic = SetWindowCompositionAttribute(ACCENT_ENABLE_ACRYLICBLURBEHIND), AllowsTransparency=True
//   blur    = SetWindowCompositionAttribute(ACCENT_ENABLE_BLURBEHIND), AllowsTransparency=True
//   --rgn   = clip the window with SetWindowRgn (rounded rect) to get a radius larger than DWM's.
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

static class Program
{
    static string Mode = "dwm";
    static bool Rgn;
    static bool NcActivate;
    static bool Activate; // diagnostic: normal activatable window (no NOACTIVATE/TOOLWINDOW)
    static bool Single;   // diagnostic: WindowStyle.SingleBorderWindow under WindowChrome // keep DWM drawing the window as "active" so the acrylic backdrop does not fall back to solid
    static Color Tint = Color.FromArgb(0x99, 0xF5, 0xF5, 0xF5);

    [STAThread]
    static void Main(string[] args)
    {
        int seconds = 20;
        bool background = true;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--mode": Mode = args[++i]; break;
                case "--rgn": Rgn = true; break;
                case "--ncactivate": NcActivate = true; break;
                case "--activate": Activate = true; break;
                case "--single": Single = true; break;
                case "--nobg": background = false; break;
                case "--seconds": seconds = int.Parse(args[++i]); break;
                case "--tint":
                    uint t = Convert.ToUInt32(args[++i], 16);
                    Tint = Color.FromArgb((byte)(t >> 24), (byte)(t >> 16), (byte)(t >> 8), (byte)t);
                    break;
            }
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += (_, _) =>
        {
            if (background) MakeBackground().Show();
            var area = SystemParameters.WorkArea;
            // Bar: full width, thin, top edge, no rounding (menu-bar like).
            MakeSurface("bar", 0, 0, SystemParameters.PrimaryScreenWidth, 28, 0).Show();
            // Dock: centered near the bottom, radius 18 like the theme draft.
            MakeSurface("dock", (area.Width - 900) / 2, area.Bottom - 90, 900, 80, 18).Show();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            timer.Tick += (_, _) => app.Shutdown();
            timer.Start();
        };
        app.Run();
    }

    // Busy, high-contrast background so blur (or its absence) is obvious in screenshots.
    static Window MakeBackground()
    {
        var canvas = new Canvas { Background = Brushes.White };
        var colors = new[] { Brushes.Red, Brushes.Blue, Brushes.Green, Brushes.Black, Brushes.Orange };
        for (int y = 0; y < 1100; y += 40)
            for (int x = 0; x < 1920; x += 260)
            {
                var tb = new TextBlock { Text = "VENEER BLUR", FontSize = 28, FontWeight = FontWeights.Bold, Foreground = colors[(x / 260 + y / 40) % colors.Length] };
                Canvas.SetLeft(tb, x + (y / 40 % 2) * 60);
                Canvas.SetTop(tb, y);
                canvas.Children.Add(tb);
            }
        return new Window { WindowStyle = WindowStyle.None, WindowState = WindowState.Maximized, Content = canvas, Title = "spike-bg", ShowInTaskbar = false };
    }

    static Window MakeSurface(string name, double left, double top, double width, double height, double radius)
    {
        bool dwm = Mode == "dwm";
        var w = new Window
        {
            Title = "spike-" + name,
            WindowStyle = Single && dwm ? WindowStyle.SingleBorderWindow : WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = !dwm,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowActivated = Activate,
            ShowInTaskbar = false,
            Left = left, Top = top, Width = width, Height = height,
            Content = new Border
            {
                Background = new SolidColorBrush(Tint),
                CornerRadius = new CornerRadius(radius),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(radius > 0 ? 1 : 0),
                Child = new TextBlock { Text = $"{name} / {Mode}{(Rgn ? " + rgn" : "")}", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), FontSize = 13 },
            },
        };
        if (dwm)
            WindowChrome.SetWindowChrome(w, new WindowChrome { GlassFrameThickness = new Thickness(-1), CaptionHeight = 0, ResizeBorderThickness = new Thickness(0), UseAeroCaptionButtons = false });

        w.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (Single && dwm) // keep WS_CAPTION (needed by the backdrop) but drop the system menu and its caption buttons
                SetWindowLongPtr(hwnd, GWL_STYLE, GetWindowLongPtr(hwnd, GWL_STYLE) & ~(WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX));
            if (!Activate)
                SetWindowLongPtr(hwnd, GWL_EXSTYLE, GetWindowLongPtr(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            if (dwm)
            {
                HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
                var m = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                Log(name, "DwmExtendFrameIntoClientArea", DwmExtendFrameIntoClientArea(hwnd, ref m));
                int backdrop = 3; // DWMSBT_TRANSIENTWINDOW (acrylic)
                Log(name, "SYSTEMBACKDROP_TYPE", DwmSetWindowAttribute(hwnd, 38, ref backdrop, 4));
                int corner = radius > 0 ? 2 : 1; // DWMWCP_ROUND : DWMWCP_DONOTROUND
                Log(name, "WINDOW_CORNER_PREFERENCE", DwmSetWindowAttribute(hwnd, 33, ref corner, 4));
                int border = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
                Log(name, "BORDER_COLOR none", DwmSetWindowAttribute(hwnd, 34, ref border, 4));
                Log(name, "CAPTION_COLOR none", DwmSetWindowAttribute(hwnd, 35, ref border, 4));
                if (NcActivate)
                {
                    HwndSource.FromHwnd(hwnd).AddHook((IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
                    {
                        const int WM_NCACTIVATE = 0x86;
                        if (msg != WM_NCACTIVATE || wp != IntPtr.Zero) return IntPtr.Zero;
                        handled = true;
                        return DefWindowProc(h, WM_NCACTIVATE, 1, lp);
                    });
                    SendMessage(hwnd, 0x86, 1, IntPtr.Zero);
                }
            }
            else
            {
                var accent = new ACCENT_POLICY
                {
                    AccentState = Mode == "acrylic" ? 4 : 3, // ACRYLICBLURBEHIND : BLURBEHIND
                    GradientColor = 0x01FFFFFF, // ABGR, near-transparent: tint comes from WPF
                };
                int size = Marshal.SizeOf<ACCENT_POLICY>();
                IntPtr p = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(accent, p, false);
                var data = new WINCOMPATTRDATA { Attribute = 19, Data = p, SizeOfData = size }; // WCA_ACCENT_POLICY
                Log(name, "SetWindowCompositionAttribute", SetWindowCompositionAttribute(hwnd, ref data) ? 0 : Marshal.GetLastWin32Error());
                Marshal.FreeHGlobal(p);
            }
            if (Rgn && radius > 0)
            {
                // Physical pixels; spike assumes 96 DPI.
                int d = (int)(radius * 2);
                SetWindowRgn(hwnd, CreateRoundRectRgn(0, 0, (int)width + 1, (int)height + 1, d, d), true);
            }
        };
        return w;
    }

    static readonly string LogPath = Path.Combine(Path.GetTempPath(), "veneer-spike-overlay.log");
    static void Log(string surface, string what, int hr) =>
        File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss} pid={Environment.ProcessId} {Mode} {surface} {what} -> 0x{hr:X8}{Environment.NewLine}");

    const int GWL_EXSTYLE = -20, GWL_STYLE = -16;
    const nint WS_SYSMENU = 0x80000, WS_MINIMIZEBOX = 0x20000, WS_MAXIMIZEBOX = 0x10000;
    const nint WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    [StructLayout(LayoutKind.Sequential)] struct MARGINS { public int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct ACCENT_POLICY { public int AccentState, AccentFlags; public uint GradientColor; public int AnimationId; }
    [StructLayout(LayoutKind.Sequential)] struct WINCOMPATTRDATA { public int Attribute; public IntPtr Data; public int SizeOfData; }

    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS m);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref WINCOMPATTRDATA data);
    [DllImport("user32.dll")] static extern nint GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern nint SetWindowLongPtr(IntPtr hwnd, int index, nint value);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd, IntPtr rgn, bool redraw);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr hwnd, int msg, nint wp, IntPtr lp);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, nint wp, IntPtr lp);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
}
