using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Floe.Core.Interop;
using Floe.Plugins;

namespace Floe.Tools.PluginProbe;

/// <summary>
/// End-to-end probe for the real host: paints a wallpaper-like backdrop, puts the Tidepool plugin on
/// top of it through <see cref="WebVisualizerHost"/>, feeds it synthetic audio, and screenshots the pair.
/// </summary>
internal static class Program
{
    private const int W = 1600;
    private const int H = 140;

    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "floe-plugin-probe.log");
    private static readonly string ShotPath = Path.Combine(Path.GetTempPath(), "floe-plugin-probe.png");

    private static IntPtr _backdrop;

    [STAThread]
    private static int Main(string[] args)
    {
        TrySetDpiAware();
        const int x = 120;
        const int y = 700;
        Log($"dpi={GetDpiForSystem()}");

        var roots = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "plugins"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "plugins"),
        };

        var catalog = VisualizerPluginCatalog.Discover(roots);
        Log($"plugins={catalog.Plugins.Count} rejected={catalog.Rejected.Count}");
        foreach (var rejection in catalog.Rejected)
        {
            Log("REJECTED " + rejection.Folder + " :: " + string.Join("; ", rejection.Problems));
        }

        var wanted = args.Length > 0 ? args[0] : "floe.tidepool";
        var plugin = catalog.Find(wanted) ?? catalog.Plugins.FirstOrDefault();
        if (plugin is null)
        {
            Log("no plugin found");
            return 2;
        }

        Log($"plugin={plugin.Id} entry={plugin.EntryPath}");
        CreateBackdrop(x, y);
        Thread.Sleep(400);

        WebVisualizerHost? host = null;
        try
        {
            host = new WebVisualizerHost(plugin);
            Log($"host hwnd={host.Handle}");

            host.Place(new PluginRect(x, y, W, H), 1.0, new PluginRect(x + 250, y + 8, 760, 96));
            host.SetAppearance("#70E2F5", "#AF8CFF", "#FF7AB8", "#081826", PluginOptionsDocument.Merge(plugin.Manifest, null), 60);
            host.FrameSource = Synthetic;
            host.SetRunning(true);

            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(6))
            {
                Pump();
                Thread.Sleep(10);
            }

            Log($"fps={host.LastFps} tier={host.LastTier} failure={host.Failure ?? "none"}");
            Capture(x, y);
            Log("shot=" + ShotPath);
        }
        catch (Exception ex)
        {
            Log("FAILURE: " + ex);
        }
        finally
        {
            host?.Dispose();
            if (_backdrop != IntPtr.Zero) DestroyWindow(_backdrop);
        }

        return 0;
    }

    private static SpectrumFrame Synthetic(double seconds)
    {
        var frame = new SpectrumFrame(32);
        double beat = 60.0 / 122.0;
        double kick = Math.Exp(-(seconds % beat) / 0.16);
        for (int i = 0; i < frame.Bands.Length; i++)
        {
            double f = 30 * Math.Pow(16000.0 / 30.0, (i + 0.5) / 32.0);
            double bass = Math.Exp(-f / 140.0);
            double mid = Math.Exp(-Math.Pow((Math.Log2(f) - Math.Log2(600)) / 1.5, 2));
            double high = 1.0 / (1.0 + Math.Pow(2400.0 / f, 2.2));
            frame.Bands[i] = (float)Math.Clamp(kick * bass * 1.2 + 0.45 * mid + 0.18 * high + 0.08 * Math.Sin(seconds * 2 + i), 0, 1);
        }

        frame.Bass = (float)Math.Clamp(kick * 1.1, 0, 1);
        frame.Mid = 0.55f;
        frame.Treble = 0.35f;
        frame.Level = (float)Math.Clamp(0.35 + kick * 0.5, 0, 1);
        frame.Kick = (float)Math.Max(0, kick * 0.9 - 0.05);

        for (int n = 0; n < frame.Wave.Length; n++)
        {
            double t = seconds + n / 6000.0;
            double v = 0.6 * Math.Sin(2 * Math.PI * 110 * t) + 0.25 * Math.Sin(2 * Math.PI * 220 * t + 1.1)
                       + 0.12 * Math.Sin(2 * Math.PI * 330 * t + 2.2) + 0.5 * kick * Math.Sin(2 * Math.PI * 62 * t);
            frame.Wave[n] = (float)Math.Clamp(v, -1, 1);
        }

        return frame;
    }

    private static void Capture(int x, int y)
    {
        using var bitmap = new Bitmap(W, H);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(x, y, 0, 0, new Size(W, H));
        }

        bitmap.Save(ShotPath, ImageFormat.Png);
        var c = bitmap.GetPixel(800, 60);
        Log($"sample=#{c.R:X2}{c.G:X2}{c.B:X2}");
    }

    private static void Log(string line) => File.AppendAllText(LogPath, line + Environment.NewLine);

    private static void CreateBackdrop(int x, int y)
    {
        var instance = GetModuleHandleW(null);
        var wc = new WndClass
        {
            Size = (uint)Marshal.SizeOf<WndClass>(),
            WndProc = BackProc,
            Instance = instance,
            ClassName = "PluginProofBackdrop",
        };
        RegisterClassEx(ref wc);
        _backdrop = CreateWindowExW(0x00000008, wc.ClassName, "backdrop", 0x80000000, x, y, W, H,
            IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        ShowWindow(_backdrop, 5);
        Log("backdrop created");
    }

    private static IntPtr BackProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == 0x000F)
        {
            var ps = new PaintStruct();
            var dc = BeginPaint(hwnd, ref ps);
            using (var g = Graphics.FromHdc(dc))
            {
                using var brush = new LinearGradientBrush(new Rectangle(0, 0, W, H),
                    Color.FromArgb(10, 26, 44), Color.FromArgb(24, 12, 40), 160);
                g.FillRectangle(brush, 0, 0, W, H);
                using var blob1 = new SolidBrush(Color.FromArgb(120, 70, 130, 255));
                using var blob2 = new SolidBrush(Color.FromArgb(90, 255, 90, 170));
                g.FillEllipse(blob1, 40, -40, 460, 220);
                g.FillEllipse(blob2, 1180, 20, 420, 200);
            }

            EndPaint(hwnd, ref ps);
            return IntPtr.Zero;
        }

        if (message == 0x0014) return new IntPtr(1);
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private static void Pump()
    {
        while (PeekMessageW(out var message, IntPtr.Zero, 0, 0, 0x0001))
        {
            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }
    }

    private static void TrySetDpiAware()
    {
        try
        {
            SetProcessDpiAwarenessContext(-4);
        }
        catch
        {
            // older systems keep the default
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClass
    {
        public uint Size;
        public uint Style;
        public WndProcDelegate WndProc;
        public int ClsExtra;
        public int WndExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr IconSmall;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        public IntPtr Hdc;
        public int Erase;
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Restore;
        public int IncUpdate;
        public IntPtr Reserved;
    }

    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClass wndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(int value);

    [DllImport("user32.dll")]
    private static extern int GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PeekMessageW(out Msg message, IntPtr hwnd, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessageW(ref Msg message);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hwnd, ref PaintStruct paint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hwnd, ref PaintStruct paint);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);
}

