using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Floe.App.Services;
using MediaColor = System.Windows.Media.Color;
using DrawColor = System.Drawing.Color;

namespace Floe.Tools.VisualizerProbe;

/// <summary>
/// Drives the real <c>VisualizerWindow</c> out of process: the same WPF window, the same plugin host and
/// the same settings code the dock uses, over a painted backdrop. It is the integration test the dock
/// itself would run, without touching the user's taskbar.
/// </summary>
internal static class Program
{
    private const int X = 100;
    private const int Y = 700;
    private const int W = 1600;
    private const int H = 140;

    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "floe-visualizer-probe.log");
    private static readonly string ShotPath = Path.Combine(Path.GetTempPath(), "floe-visualizer-probe.png");

    [STAThread]
    private static int Main(string[] args)
    {
        var pluginId = args.Length > 0 ? args[0] : "floe.tidepool";
        Log($"plugin={pluginId}");

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        // A backdrop, so the screenshot shows what the strip does over colour instead of the desktop.
        var backdrop = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = false,
            Left = X,
            Top = Y,
            Width = W,
            Height = H,
            Background = new LinearGradientBrush(
                MediaColor.FromRgb(10, 26, 44), MediaColor.FromRgb(28, 12, 42), new System.Windows.Point(0, 0), new System.Windows.Point(1, 1)),
            Content = new System.Windows.Controls.Border
            {
                Background = new RadialGradientBrush(MediaColor.FromArgb(90, 70, 130, 255), Colors.Transparent)
                {
                    RadiusX = 0.6,
                    RadiusY = 0.9,
                },
            },
        };
        backdrop.Show();
        Thread.Sleep(400);

        var glass = typeof(GlassSettings);
        var visualizerStyle = glass.Assembly.GetType("Floe.App.Services.VisualizerStyle")!;
        SetStatic(glass, "VisualizerLook", Enum.Parse(visualizerStyle, "Plugin"));
        SetStatic(glass, "PluginId", pluginId);

        var type = glass.Assembly.GetType("Floe.App.Windows.VisualizerWindow")!;
        var window = (Window)Activator.CreateInstance(type, nonPublic: true)!;
        var place = type.GetMethod("Place")!;
        var setRunning = type.GetMethod("SetRunning")!;

        place.Invoke(window, new object[]
        {
            new Rect(X, Y, W, H), 1.0, IntPtr.Zero, new Rect(X + 420, Y + 8, 760, 96),
        });
        window.Show();
        var clock = Stopwatch.StartNew();
        setRunning.Invoke(window, new object[] { true });
        Log($"setRunning took {clock.ElapsedMilliseconds} ms");

        var probeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        probeTimer.Tick += (_, _) =>
        {
            probeTimer.Stop();
            var field = type.GetField("_web", BindingFlags.NonPublic | BindingFlags.Instance);
            var host = field?.GetValue(window);
            if (host is null)
            {
                Log("host=null");
                return;
            }

            var hostType = host.GetType();
            var handle = (IntPtr)hostType.GetProperty("Handle")!.GetValue(host)!;
            var rect = new RECT();
            GetWindowRect(handle, ref rect);
            Log($"host window rect=({rect.Left},{rect.Top})-({rect.Right},{rect.Bottom}) visible={IsWindowVisible(handle)} style=0x{GetWindowLong(handle, -20):X8} exstyle=0x{GetWindowLong(handle, -16):X8}");
            Log($"visualizer visible={window.IsVisible} size={window.ActualWidth}x{window.ActualHeight} at ({window.Left},{window.Top}) topmost={window.Topmost}");
            Log($"host handle={hostType.GetProperty("Handle")!.GetValue(host)} " +
                $"running={hostType.GetProperty("IsRunning")!.GetValue(host)} " +
                $"failure={hostType.GetProperty("Failure")!.GetValue(host) ?? "none"} " +
                $"fps={hostType.GetProperty("LastFps")!.GetValue(host)} " +
                $"tier={hostType.GetProperty("LastTier")!.GetValue(host)}");
        };
        probeTimer.Start();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                Capture();
            }
            catch (Exception ex)
            {
                Log("capture failed: " + ex.Message);
            }

            try
            {
                setRunning.Invoke(window, new object[] { false });
                Thread.Sleep(600);
                window.Close();
            }
            catch (Exception ex)
            {
                Log("close failed: " + ex.Message);
            }

            app.Shutdown();
        };
        timer.Start();
        app.Run();

        backdrop.Close();
        Log("done");
        return 0;
    }

    private static void SetStatic(Type type, string property, object value)
    {
        var prop = type.GetProperty(property, BindingFlags.Public | BindingFlags.Static)!;
        prop.GetSetMethod(nonPublic: true)!.Invoke(null, new[] { value });
    }

    private static void Capture()
    {
        using var bitmap = new Bitmap(W, H);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(X, Y, 0, 0, new System.Drawing.Size(W, H));
        }

        bitmap.Save(ShotPath, ImageFormat.Png);
        var c = bitmap.GetPixel(W / 2, H / 2);
        Log($"sample=#{c.R:X2}{c.G:X2}{c.B:X2} shot={ShotPath}");
    }

    private static void Log(string line) => File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, ref RECT rect);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
}
