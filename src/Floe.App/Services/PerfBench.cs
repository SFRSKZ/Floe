using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Floe.App.Windows;

namespace Floe.App.Services;

/// <summary>
/// Frame-cost benchmarks on real windows, parked left of every screen so nothing covers the running dock.
/// <para>--bench-viz=&lt;seconds&gt; plays the visualizer strip (pick it with --look=Cubes and --gpu-viz=0|1) at a
/// made-up level; --bench-dock=&lt;seconds&gt; shows a dock and sweeps a cursor across it the whole time
/// (--theme=, --surface=, --rim= and FLOE_NOFX pick what it wears). Both measure after a 2 s warm-up and append
/// one line to --bench-out=&lt;file&gt;, labelled with --bench-label=: CPU per second for the whole process, the UI
/// thread and the busiest other thread (WPF's render thread), DWM's CPU when Windows lets us read it, and for the
/// dock its frames, their UI work and the late ones (a gap over 25 ms).</para>
/// The taskbar, the app bar, autostart and the settings files are never touched.
/// </summary>
internal static class PerfBench
{
    private const double WarmUp = 2;

    public static bool TryStart(string[] args)
    {
        string? viz = Value(args, "--bench-viz=");
        string? dock = Value(args, "--bench-dock=");
        string? layered = Value(args, "--bench-window=");
        if (Value(args, "--render-icon-menu=") is { } menuShot)
        {
            GlassSettings.Load();
            MenuShot(Path.GetFullPath(menuShot));
            return true;
        }

        if (viz is null && dock is null && layered is null) return false;
        string output = Path.GetFullPath(Value(args, "--bench-out=") ?? Path.Combine(Path.GetTempPath(), "floe-bench.txt"));
        string label = Value(args, "--bench-label=") ?? string.Join(' ', args);
        double seconds = double.TryParse(viz ?? dock ?? Value(args, "--bench-seconds=") ?? "10", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 10;

        // the bench's dock never opens the visualizer: music playing right now would skew the numbers
        if (dock is not null) Environment.SetEnvironmentVariable("FLOE_NOFX", (Environment.GetEnvironmentVariable("FLOE_NOFX") ?? "") + ",viz");
        GlassSettings.Load();
        try
        {
            if (viz is not null) RunViz(seconds, output, label);
            else if (dock is not null) RunDock(seconds, output, label, args.Any(a => a.Equals("--bench-idle", StringComparison.OrdinalIgnoreCase)));
            else RunWindow(layered!, args.Any(a => a.Equals("--dwm", StringComparison.OrdinalIgnoreCase)), args.Any(a => a.Equals("--full", StringComparison.OrdinalIgnoreCase)), seconds, output, label);
        }
        catch (Exception ex)
        {
            Write(output, $"{label}\tFAILED {ex.GetType().Name}: {ex.Message}");
            Application.Current.Shutdown(1);
        }

        return true;
    }

    private static void RunViz(double seconds, string output, string label)
    {
        var close = StartStrip();
        Measure(seconds, () => $"look={GlassSettings.VisualizerLook} gpu={GlassSettings.GpuVisualizer} cube={GlassSettings.CubeSize}", output, label, close);
    }

    // The visualizer strip, parked off screen and held at a made-up level; returns what closes it.
    private static Action StartStrip()
    {
        double scale = GetDpiForSystem() / 96.0;
        var window = new VisualizerWindow();
        var bounds = new Rect(SystemParameters.VirtualScreenLeft - 2400, SystemParameters.VirtualScreenTop + 300, 1920, 76);
        window.Place(bounds, scale, IntPtr.Zero, new Rect(bounds.Left + 500, bounds.Top, 920, 68));
        window.SetRunning(true);
        var clock = Stopwatch.StartNew();
        // hold the level both looks see: CubePreview's beat over a slow swell
        var level = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(8) };
        level.Tick += (_, _) =>
        {
            double t = clock.Elapsed.TotalSeconds;
            double value = Math.Clamp(.25 + (.35 * Math.Exp(-((t % .5) * 9))) + (.3 * Math.Sin(t * 1.3)), 0, 1);
            window.Cubes.DebugLevel = value;
            if (window.CubeScene is { } scene) scene.DebugLevel = (float)value;
        };
        level.Start();
        return () =>
        {
            level.Stop();
            window.SetRunning(false);
            window.Close();
        };
    }

    private static void RunDock(double seconds, string output, string label, bool idle)
    {
        DockWindow.BenchLeft = SystemParameters.VirtualScreenLeft - 4000;
        if (Value(Environment.GetCommandLineArgs(), "--bench-shot=") is { } shot)
        {
            Shot(Path.GetFullPath(shot));
            return;
        }

        var dock = new DockWindow { AutoHide = false, BlurBackdrop = true, ReserveScreenSpace = false };
        dock.Show();
        var clock = Stopwatch.StartNew();
        // a cursor drifting to and fro over the icons, as a hand does, every 8 ms like a mouse
        var cursor = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(8) };
        cursor.Tick += (_, _) =>
        {
            double t = clock.Elapsed.TotalSeconds;
            double width = dock.BenchWidth;
            dock.BenchHover((width / 2) + (width * .32 * Math.Sin(t * 2 * Math.PI / 4)));
        };
        if (!idle) cursor.Start();
        // --with-viz: the visualizer strip plays alongside, as when music is on while the cursor is on the dock
        Action closeStrip = Environment.GetCommandLineArgs().Any(a => a.Equals("--with-viz", StringComparison.OrdinalIgnoreCase)) ? StartStrip() : () => { };
        Measure(seconds, () => $"{(idle ? "idle " : "")}theme={GlassSettings.Theme} surface={GlassSettings.Surface} rim={GlassSettings.Rim} nofx={Environment.GetEnvironmentVariable("FLOE_NOFX")}"
            + $" size={dock.BenchWidth:F0}x{dock.Height:F0}dip frames={DockWindow.BenchFrames} work={DockWindow.BenchWork / Math.Max(1, DockWindow.BenchFrames):F2}ms late={DockWindow.BenchLate} maxGap={DockWindow.BenchMaxGap:F0}ms",
            output, label, () =>
        {
            cursor.Stop();
            closeStrip();
            dock.Close();
        }, () =>
        {
            DockWindow.BenchFrames = 0;
            DockWindow.BenchWork = 0;
            DockWindow.BenchLate = 0;
            DockWindow.BenchMaxGap = 0;
        });
    }

    // --bench-shot=<png>: the dock at the top of the primary screen for a moment, hovered mid-way, grabbed off the
    // screen as DWM composed it (run with FLOE_NOFX=exclude, or the dock keeps itself out of screen grabs)
    private static void Shot(string file)
    {
        DockWindow.BenchLeft = 40;
        DockWindow.BenchTop = 0;
        var dock = new DockWindow { AutoHide = false, BlurBackdrop = true, ReserveScreenSpace = false };
        dock.Show();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        int step = 0;
        timer.Tick += (_, _) =>
        {
            if (step++ == 0)
            {
                dock.BenchHover(dock.BenchWidth * .4);
                timer.Interval = TimeSpan.FromSeconds(1);
                return;
            }

            timer.Stop();
            double scale = GetDpiForSystem() / 96.0;
            int x = (int)(dock.Left * scale), y = (int)(dock.Top * scale), w = (int)(dock.ActualWidth * scale), h = (int)(dock.ActualHeight * scale);
            if (Floe.App.Effects.Derezz.ScreenGrab.Capture(new Int32Rect(x, y, w, h), 0) is { } grabbed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(grabbed));
                using var stream = File.Create(file);
                encoder.Save(stream);
            }

            dock.Close();
            Application.Current.Shutdown();
        };
        timer.Start();
    }

    // --render-icon-menu=<png>: an icon's right-click menu at the top left of the primary screen for a moment, grabbed off
    // the screen with whatever is behind it (run with FLOE_NOFX=exclude, or the glass keeps itself out of screen grabs)
    private static void MenuShot(string file)
    {
        var menu = new IconMenuWindow { KeepOpen = true };
        var actions = new List<(string, Action)> { ("Aç", () => { }), ("Sabitle", () => { }), ("Konum", () => { }), ("Kapat", () => { }) };
        menu.ShowFor("Google Chrome", null, actions, 200, 260, SystemParameters.WorkArea);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            double scale = GetDpiForSystem() / 96.0;
            int x = (int)(menu.Left * scale), y = (int)(menu.Top * scale), w = (int)(menu.ActualWidth * scale), h = (int)(menu.ActualHeight * scale);
            if (Floe.App.Effects.Derezz.ScreenGrab.Capture(new Int32Rect(x, y, w, h), 0) is { } grabbed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(grabbed));
                using var stream = File.Create(file);
                encoder.Save(stream);
            }

            menu.Close();
            Application.Current.Shutdown();
        };
        timer.Start();
    }

    // The floor under any dock: a window the dock's size whose only content is a bar moving every frame.
    // Layered (AllowsTransparency, what the dock is) or, with --dwm, a plain window DWM blends by its alpha.
    private static void RunWindow(string size, bool dwm, bool full, double seconds, string output, string label)
    {
        var parts = size.Split('x', ',');
        double width = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
        double height = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        // --full: the bar covers the window and only its opacity moves, so every frame redraws every pixel
        var bar = new System.Windows.Shapes.Rectangle { Width = full ? width : 60, Height = full ? height : height / 2, Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x80, 0x40, 0x80, 0xFF)) };
        var canvas = new System.Windows.Controls.Canvas { Background = System.Windows.Media.Brushes.Transparent };
        canvas.Children.Add(bar);
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = !dwm,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Left = SystemParameters.VirtualScreenLeft - width - 400,
            Top = SystemParameters.VirtualScreenTop,
            Width = width,
            Height = height,
            Content = canvas,
        };
        if (dwm) window.SourceInitialized += (_, _) => DwmTransparency.Apply(window);
        window.Show();
        var clock = Stopwatch.StartNew();
        int frames = 0;
        System.Windows.Media.CompositionTarget.Rendering += (_, _) =>
        {
            frames++;
            double k = .5 + (.5 * Math.Sin(clock.Elapsed.TotalSeconds * 2));
            if (full) bar.Opacity = .3 + (.6 * k);
            else System.Windows.Controls.Canvas.SetLeft(bar, (width - 60) * k);
        };
        Measure(seconds, () => $"window {width}x{height} {(dwm ? "dwm-alpha" : "layered")}{(full ? " full-redraw" : "")} ticks={frames}", output, label, window.Close, () => frames = 0);
    }

    // Warms up, snapshots the CPU counters, runs, and reports per second; then closes up and quits.
    private static void Measure(double seconds, Func<string> details, string output, string label, Action close, Action? reset = null)
    {
        int ui = (int)GetCurrentThreadId();
        Dictionary<int, TimeSpan> threads = new();
        TimeSpan process = default, dwm = default;
        bool dwmReadable = false;
        long start = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(WarmUp) };
        timer.Tick += (_, _) =>
        {
            if (start == 0)
            {
                reset?.Invoke();
                threads = Threads();
                process = Process.GetCurrentProcess().TotalProcessorTime;
                dwmReadable = TryDwm(out dwm);
                start = Stopwatch.GetTimestamp();
                timer.Interval = TimeSpan.FromSeconds(seconds);
                return;
            }

            timer.Stop();
            double elapsed = (Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency;
            double PerSecond(TimeSpan span) => span.TotalMilliseconds / elapsed;
            var now = Threads();
            double uiMs = 0, busiest = 0, rest = 0;
            foreach (var (id, total) in now)
            {
                double ms = PerSecond(total - (threads.TryGetValue(id, out var before) ? before : TimeSpan.Zero));
                if (id == ui) uiMs = ms;
                else if (ms > busiest) { rest += busiest; busiest = ms; }
                else rest += ms;
            }

            string dwmText = dwmReadable && TryDwm(out var dwmNow) ? $"{PerSecond(dwmNow - dwm):F0}" : "n/a";
            string extra = details();
            Write(output, $"{label}\tcpu={PerSecond(Process.GetCurrentProcess().TotalProcessorTime - process):F0}ms/s ui={uiMs:F0} render={busiest:F0} other={rest:F0} dwm={dwmText}\t{extra}");
            close();
            Application.Current.Shutdown();
        };
        timer.Start();
    }

    private static Dictionary<int, TimeSpan> Threads()
    {
        var map = new Dictionary<int, TimeSpan>();
        using var self = Process.GetCurrentProcess();
        foreach (ProcessThread thread in self.Threads)
        {
            try { map[thread.Id] = thread.TotalProcessorTime; }
            catch { /* the thread ended */ }
        }

        return map;
    }

    private static bool TryDwm(out TimeSpan time)
    {
        time = default;
        try
        {
            var dwm = Process.GetProcessesByName("dwm");
            if (dwm.Length == 0) return false;
            foreach (var p in dwm) time += p.TotalProcessorTime;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void Write(string file, string line)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.AppendAllText(file, line + Environment.NewLine);
    }

    private static string? Value(string[] args, string option) =>
        args.FirstOrDefault(a => a.StartsWith(option, StringComparison.OrdinalIgnoreCase))?[option.Length..];

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
}
