using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floe.App.Effects.Visualizer;
using Floe.App.Services;
using Floe.App.Windows;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Color4 = Vortice.Mathematics.Color4;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;
using Color = System.Windows.Media.Color;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// --render-gpu-cubes=&lt;dir&gt; writes 90 frames (30 fps) of <see cref="CubeGpuScene"/> and of the WPF
/// <see cref="CubeVisualizer"/> on the same 900x82 DIP strip at 2x, fed the same made-up level, into gpu\, wpf\ and
/// compare\ (WPF on top, GPU below). --bench-cubes=&lt;file&gt; times both looks and Tron's loose cubes offscreen
/// and writes the numbers there. --hologram-tint=&lt;name&gt; and --cube-size=&lt;8..24&gt; preview without saving.
/// </summary>
public static class CubeGpuPreview
{
    private const int Frames = 90;
    private const double Dip = 2;

    public static bool TryRender(string[] args)
    {
        if (TryBench(args)) return true;
        if (TryRenderTron(args)) return true;
        const string option = "--render-gpu-cubes=";
        var argument = args.FirstOrDefault(a => a.StartsWith(option, StringComparison.OrdinalIgnoreCase));
        if (argument is null) return false;

        string folder = Path.GetFullPath(argument[option.Length..]);
        try
        {
            Prepare(args);
            const int width = 900, height = 82;
            string gpu = Path.Combine(folder, "gpu"), wpf = Path.Combine(folder, "wpf"), compare = Path.Combine(folder, "compare");
            Directory.CreateDirectory(wpf);
            Directory.CreateDirectory(compare);

            using (var scene = new CubeGpuScene())
            {
                GpuOffscreen.RenderFrames(scene, (int)(width * Dip), (int)(height * Dip), (float)Dip, Frames,
                    (frame, index) => scene.DebugLevel = (float)Level(index / 30.0), gpu);
            }

            var cubes = Stage(width, height, out var stage);
            for (int f = 0; f < Frames; f++)
            {
                cubes.DebugLevel = Level(f / 30.0);
                cubes.Step(1 / 30.0);
                var wpfFrame = Snapshot(stage, cubes, width, height);
                Save(wpfFrame, Path.Combine(wpf, $"f{f:000}.png"));
                var gpuFrame = new BitmapImage(new Uri(Path.Combine(gpu, $"f{f:000}.png")));
                Save(Stack(wpfFrame, gpuFrame), Path.Combine(compare, $"f{f:000}.png"));
            }
        }
        catch (Exception ex)
        {
            try { Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "render-error.txt"), ex.ToString()); } catch { }
            Environment.ExitCode = 1;
        }

        return true;
    }

    // A kick every half second over a slower swell: CubePreview's schedule.
    private static double Level(double t)
    {
        double beat = Math.Exp(-((t % .5) * 9));
        return Math.Clamp(.25 + (.35 * beat) + (.3 * Math.Sin(t * 1.3)), 0, 1);
    }

    private static void Prepare(string[] args)
    {
        const string tintOption = "--hologram-tint=";
        var tint = args.FirstOrDefault(a => a.StartsWith(tintOption, StringComparison.OrdinalIgnoreCase))?[tintOption.Length..];
        if (Enum.TryParse<HologramColor>(tint, true, out var color)) GlassSettings.PreviewTint(color);
        const string sizeOption = "--cube-size=";
        var size = args.FirstOrDefault(a => a.StartsWith(sizeOption, StringComparison.OrdinalIgnoreCase))?[sizeOption.Length..];
        if (double.TryParse(size, out var cell)) GlassSettings.PreviewCubeSize(cell);
    }

    private static CubeVisualizer Stage(int width, int height, out Canvas stage)
    {
        stage = new Canvas { Width = width, Height = height, Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0E, 0x14)) };
        var cubes = new CubeVisualizer { Width = width, Height = height, Capsule = new Rect(0, 0, width, height) };
        stage.Children.Add(cubes);
        stage.Measure(new Size(width, height));
        stage.Arrange(new Rect(0, 0, width, height));
        return cubes;
    }

    private static BitmapSource Snapshot(Canvas stage, CubeVisualizer cubes, int width, int height)
    {
        cubes.InvalidateVisual();
        stage.UpdateLayout();
        var target = new RenderTargetBitmap((int)(width * Dip), (int)(height * Dip), 96 * Dip, 96 * Dip, PixelFormats.Pbgra32);
        target.Render(stage);
        target.Freeze();
        return target;
    }

    private static BitmapSource Stack(BitmapSource top, BitmapSource bottom)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(top, new Rect(0, 0, top.PixelWidth, top.PixelHeight));
            dc.DrawRectangle(Brushes.White, null, new Rect(0, top.PixelHeight, top.PixelWidth, 2));
            dc.DrawImage(bottom, new Rect(0, top.PixelHeight + 2, bottom.PixelWidth, bottom.PixelHeight));
        }

        var target = new RenderTargetBitmap(top.PixelWidth, top.PixelHeight + 2 + bottom.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        return target;
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    // ---- --bench-cubes ------------------------------------------------------------------------------

    private static bool TryBench(string[] args)
    {
        const string option = "--bench-cubes=";
        var argument = args.FirstOrDefault(a => a.StartsWith(option, StringComparison.OrdinalIgnoreCase));
        if (argument is null) return false;
        string file = Path.GetFullPath(argument[option.Length..]);
        var report = new StringBuilder();
        try
        {
            Prepare(args);
            // the real strip: the screen's width by the dock's bar plus its bottom margin
            const int width = 1920, height = 76, frames = 300;
            report.AppendLine($"strip {width}x{height} DIP at {Dip}x, cube {GlassSettings.CubeSize} DIP, {frames} frames, level {Level(0):F2}..");

            // WPF: Step + OnRender (what the UI thread does), then a software raster of it (an upper bound for the
            // render thread, which tessellates the same geometry and fills it on the GPU)
            var cubes = Stage(width, height, out var stage);
            double ui = 0, uiMax = 0, raster = 0;
            var target = new RenderTargetBitmap((int)(width * Dip), (int)(height * Dip), 96 * Dip, 96 * Dip, PixelFormats.Pbgra32);
            for (int f = 0; f < frames; f++)
            {
                cubes.DebugLevel = Level(f / 30.0);
                long start = Stopwatch.GetTimestamp();
                cubes.Step(1 / 30.0);
                cubes.InvalidateVisual();
                stage.UpdateLayout();
                double ms = Ms(start);
                ui += ms;
                uiMax = Math.Max(uiMax, ms);
                start = Stopwatch.GetTimestamp();
                target.Clear();
                target.Render(stage);
                raster += Ms(start);
            }

            report.AppendLine($"wpf cubes   ui {ui / frames:F3} ms/frame (max {uiMax:F2})   software raster {raster / frames:F2} ms/frame");

            // GPU: the scene's CPU work (step + vertex build + upload) and the frame finished on the GPU
            var device = GpuDevice.Create();
            try
            {
                var context = device.ImmediateContext;
                int w = (int)(width * Dip), h = (int)(height * Dip);
                using var texture = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)w, (uint)h, 1, 1,
                    BindFlags.RenderTarget, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
                using var view = device.CreateRenderTargetView(texture, null);
                using var staging = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, 1, 1, 1, 1,
                    BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read, 1, 0, ResourceOptionFlags.None));
                using var scene = new CubeGpuScene();
                scene.Initialize(device);
                scene.Resize(w, h);
                var frame = new GpuFrame { Width = w, Height = h, PixelsPerDip = (float)Dip, Fade = 1, Dt = 1 / 30f };
                double cpu = 0, total = 0, totalMax = 0;
                for (int f = 0; f < frames + 10; f++)
                {
                    scene.DebugLevel = (float)Level(f / 30.0);
                    long start = Stopwatch.GetTimestamp();
                    context.ClearRenderTargetView(view, new Color4(0, 0, 0, 0));
                    scene.Render(context, view, frame);
                    double submit = Ms(start);
                    // wait for the GPU: copy one pixel back
                    context.CopySubresourceRegion(staging, 0, 0, 0, 0, texture, 0, new Vortice.Mathematics.Box(0, 0, 0, 1, 1, 1));
                    context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                    context.Unmap(staging, 0);
                    double all = Ms(start);
                    if (f < 10) continue; // warm-up
                    cpu += submit;
                    total += all;
                    totalMax = Math.Max(totalMax, all);
                }

                report.AppendLine($"gpu cubes   cpu {cpu / frames:F3} ms/frame   finished on gpu {total / frames:F3} ms/frame (max {totalMax:F2})");
                scene.ReleaseDevice();
            }
            finally
            {
                device.Dispose();
            }

            BenchTron(report, frames);
        }
        catch (Exception ex)
        {
            report.AppendLine(ex.ToString());
            Environment.ExitCode = 1;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, report.ToString());
        return true;
    }

    // --render-gpu-tron=<dir>: the same mound fed to two TronCubes (they share a seed), one drawn by WPF, one through
    // TronGpuScene, at 0.5, 1, 1.5 and 2 s; wpf-N.png, gpu-N.png and compare-N.png (WPF on top).
    private static bool TryRenderTron(string[] args)
    {
        const string option = "--render-gpu-tron=";
        var argument = args.FirstOrDefault(a => a.StartsWith(option, StringComparison.OrdinalIgnoreCase));
        if (argument is null) return false;
        string folder = Path.GetFullPath(argument[option.Length..]);
        try
        {
            Directory.CreateDirectory(folder);
            GlassSettings.PreviewTheme(DockTheme.TronAres);
            const int width = 700, height = 160;
            var stage = new Canvas { Width = width, Height = height, Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0E, 0x14)) };
            var wpf = new TronCubes { Width = width, Height = height, Visibility = Visibility.Visible };
            var gpu = new TronCubes { Width = width, Height = height, Visibility = Visibility.Visible, Mesh = new TronMesh() };
            stage.Children.Add(wpf);
            stage.Measure(new Size(width, height));
            stage.Arrange(new Rect(0, 0, width, height));
            var columns = new List<TronCubes.Column>();
            for (int f = 1; f <= 120; f++)
            {
                double hover = 350 + (60 * Math.Sin(f / 40.0));
                Mound(columns, width, 112, hover);
                wpf.Frame(columns, hover, 1 / 60.0);
                gpu.Frame(columns, hover, 1 / 60.0);
                if (f % 30 != 0) continue;
                stage.UpdateLayout();
                var shot = new RenderTargetBitmap((int)(width * Dip), (int)(height * Dip), 96 * Dip, 96 * Dip, PixelFormats.Pbgra32);
                shot.Render(stage);
                shot.Freeze();
                Save(shot, Path.Combine(folder, $"wpf-{f}.png"));
                using (var scene = new TronGpuScene())
                {
                    scene.Submit(gpu.Mesh!, System.Numerics.Matrix3x2.Identity);
                    string temp = Path.Combine(folder, "gpu-tmp");
                    GpuOffscreen.RenderFrames(scene, (int)(width * Dip), (int)(height * Dip), (float)Dip, 1, null, temp);
                    File.Copy(Path.Combine(temp, "f000.png"), Path.Combine(folder, $"gpu-{f}.png"), true);
                }

                var gpuFrame = new BitmapImage(new Uri(Path.Combine(folder, $"gpu-{f}.png")));
                Save(Stack(shot, gpuFrame), Path.Combine(folder, $"compare-{f}.png"));
            }
        }
        catch (Exception ex)
        {
            try { Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "render-error.txt"), ex.ToString()); } catch { }
            Environment.ExitCode = 1;
        }

        return true;
    }

    // DockWindow.BuildCubeColumns at the full bulge, without the side stretch
    private static void Mound(List<TronCubes.Column> columns, double width, double top, double hover)
    {
        columns.Clear();
        for (double right = width - 20; right > 20; right -= TronCubes.Cube)
        {
            double left = right - TronCubes.Cube;
            double d = ((right + left) / 2) - hover;
            double cubes = 32 * .72 * 1.5 * Math.Exp(-(d * 1.8 * d * 1.8) / (2 * 52 * 52)) / TronCubes.Cube;
            double whole = Math.Floor(cubes);
            columns.Add(new TronCubes.Column(left, top, TronCubes.Cube, whole * TronCubes.Cube, cubes - whole));
        }
    }

    // Tron's loose cubes: a full mound under a hovered icon, flowing and scattering, the way the dock feeds it
    private static void BenchTron(StringBuilder report, int frames)
    {
        GlassSettings.PreviewTheme(DockTheme.TronAres);
        const double width = 1100, height = 160, top = 112;
        var stage = new Canvas { Width = width, Height = height, Background = Brushes.Black };
        var tron = new TronCubes { Width = width, Height = height, Visibility = Visibility.Visible };
        stage.Children.Add(tron);
        stage.Measure(new Size(width, height));
        stage.Arrange(new Rect(0, 0, width, height));
        var columns = new List<TronCubes.Column>();
        double ui = 0, uiMax = 0, raster = 0;
        int particles = 0;
        var target = new RenderTargetBitmap((int)(width * Dip), (int)(height * Dip), 96 * Dip, 96 * Dip, PixelFormats.Pbgra32);
        for (int f = 0; f < frames + 60; f++)
        {
            // the hover sweeps slowly across the middle, the bulge at its full 32 DIP
            double hover = 550 + (200 * Math.Sin(f / 60.0));
            columns.Clear();
            for (double right = width - 20; right > 20; right -= TronCubes.Cube)
            {
                double left = right - TronCubes.Cube;
                double d = ((right + left) / 2) - hover;
                double cubes = 32 * .72 * 1.5 * Math.Exp(-(d * 1.8 * d * 1.8) / (2 * 52 * 52)) / TronCubes.Cube;
                double whole = Math.Floor(cubes);
                columns.Add(new TronCubes.Column(left, top, TronCubes.Cube, whole * TronCubes.Cube, cubes - whole));
            }

            long start = Stopwatch.GetTimestamp();
            tron.Frame(columns, hover, 1 / 60.0);
            double ms = Ms(start);
            if (f < 60) continue; // let the scatter fill up
            ui += ms;
            uiMax = Math.Max(uiMax, ms);
            particles = tron.ParticleCount;
            start = Stopwatch.GetTimestamp();
            target.Clear();
            target.Render(stage);
            raster += Ms(start);
        }

        report.AppendLine($"tron cubes  ui {ui / frames:F3} ms/frame (max {uiMax:F2}), {particles} pieces in the air   software raster incl. blur {raster / frames:F2} ms/frame");
    }

    private static double Ms(long start) => (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
}
