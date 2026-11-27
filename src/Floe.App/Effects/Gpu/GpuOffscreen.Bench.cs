using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Floe.App.Services;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Floe.App.Effects.Gpu;

public static partial class GpuOffscreen
{
    /// <summary>
    /// Handles <c>--bench-gpu-look=Look;out.txt;frames;width;height</c>: draws the look's frames on the mock track and
    /// waits for the GPU after each, so the time per frame is what the look costs the graphics card. Shaders come
    /// from the disk cache (a first run compiles them, outside the timing).
    /// </summary>
    public static bool TryBenchLook(string[] args)
    {
        const string option = "--bench-gpu-look=";
        var argument = args.FirstOrDefault(a => a.StartsWith(option, StringComparison.OrdinalIgnoreCase));
        if (argument is null) return false;
        var parts = argument[option.Length..].Split(';');
        string output = Path.GetFullPath(parts.Length > 1 && parts[1].Length > 0 ? parts[1] : Path.Combine(Path.GetTempPath(), "floe-gpu-bench.txt"));
        int frames = parts.Length > 2 && int.TryParse(parts[2], out var f) ? f : 300;
        int width = parts.Length > 3 && int.TryParse(parts[3], out var w) ? w : 3840;
        int height = parts.Length > 4 && int.TryParse(parts[4], out var h) ? h : 189;
        try
        {
            if (!Enum.TryParse<VisualizerStyle>(parts[0].Trim(), true, out var look) || !GpuSceneCatalog.Has(look))
            {
                File.AppendAllText(output, $"{parts[0]}\tunknown look\n");
                return true;
            }

            var device = GpuDevice.Create();
            try
            {
                var context = device.ImmediateContext;
                using var texture = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1,
                    BindFlags.RenderTarget, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
                using var target = device.CreateRenderTargetView(texture, null);
                using var query = device.CreateQuery(new QueryDescription(QueryType.Event));
                using var scene = GpuSceneCatalog.Create(look);
                scene.Initialize(device);
                scene.Resize(width, height);
                var frame = new GpuFrame { Width = width, Height = height, PixelsPerDip = 1.5f, Fade = 1, Performance = Performance.Level };

                void Wait()
                {
                    context.End(query);
                    while (!context.IsDataAvailable(query)) { }
                }

                var times = new double[frames];
                for (int i = -20; i < frames; i++)
                {
                    MockFrame(frame, 4 + (Math.Max(i, 0) / 60f));
                    frame.Dt = 1 / 60f;
                    frame.DockLeft = .37f;
                    frame.DockRight = .63f;
                    long start = Stopwatch.GetTimestamp();
                    context.ClearRenderTargetView(target, new Color4(0, 0, 0, 0));
                    scene.Render(context, target, frame);
                    Wait();
                    if (i >= 0) times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                }

                Array.Sort(times);
                File.AppendAllText(output, $"{look}\t{width}x{height}\tmedian {times[frames / 2]:0.00} ms\tp95 {times[(int)(frames * .95)]:0.00} ms\n");
                scene.ReleaseDevice();
            }
            finally
            {
                device.Dispose();
            }
        }
        catch (Exception ex)
        {
            File.AppendAllText(output, $"{parts[0]}\tFAILED {ex.Message}\n");
        }

        return true;
    }
}
