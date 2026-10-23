using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using System.Windows.Media.Imaging;
using Floe.App.Effects.Visualizer;
using Floe.App.Services;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// Renders a scene offscreen to PNG frames, for previews and diagnostics. No window, no dock state:
/// the frames come back through a staging texture and are composited over the dock's backdrop colour.
/// </summary>
public static class GpuOffscreen
{
    // The dock's backdrop, so a transparent strip looks like it does on screen.
    private const byte BackdropR = 0x0B;
    private const byte BackdropG = 0x0E;
    private const byte BackdropB = 0x14;

    private static readonly MediaColor BasePrimary = MediaColor.FromRgb(0x5C, 0xA8, 0xFF);
    private static readonly float[] PaletteR = new float[GpuFrame.PaletteSize];
    private static readonly float[] PaletteG = new float[GpuFrame.PaletteSize];
    private static readonly float[] PaletteB = new float[GpuFrame.PaletteSize];

    /// <summary>
    /// Initialises <paramref name="scene"/>, renders <paramref name="frames"/> frames and writes
    /// f000.png … to <paramref name="dir"/>. <paramref name="prepare"/> may fill Audio or flip Wave.
    /// </summary>
    public static void RenderFrames(IGpuScene scene, int width, int height, float pixelsPerDip, int frames, Action<GpuFrame, int>? prepare, string dir)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        frames = Math.Max(0, frames);
        Directory.CreateDirectory(dir);

        var device = GpuDevice.Create();
        try
        {
            var context = device.ImmediateContext;
            var frame = new GpuFrame();

            using var targetTexture = device.CreateTexture2D(new Texture2DDescription(
                Format.B8G8R8A8_UNorm,
                (uint)width,
                (uint)height,
                1,
                1,
                BindFlags.RenderTarget,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                1,
                0,
                ResourceOptionFlags.None));
            using var target = device.CreateRenderTargetView(targetTexture, null);
            using var staging = device.CreateTexture2D(new Texture2DDescription(
                Format.B8G8R8A8_UNorm,
                (uint)width,
                (uint)height,
                1,
                1,
                BindFlags.None,
                ResourceUsage.Staging,
                CpuAccessFlags.Read,
                1,
                0,
                ResourceOptionFlags.None));

            scene.Initialize(device);
            scene.Resize(width, height);

            for (int index = 0; index < frames; index++)
            {
                float time = index / 30f;
                frame.Dt = 1f / 30f;
                frame.Time = time;
                frame.Fade = 1;
                frame.Width = width;
                frame.Height = height;
                frame.PixelsPerDip = pixelsPerDip;
                frame.Performance = Performance.Level;
                frame.Wave = false;

                VisualizerPalette.Fill(PaletteR, PaletteG, PaletteB, time, BasePrimary);
                for (int i = 0; i < GpuFrame.PaletteSize; i++)
                {
                    frame.Palette[i] = new System.Numerics.Vector3(PaletteR[i], PaletteG[i], PaletteB[i]);
                }

                prepare?.Invoke(frame, index);

                context.ClearRenderTargetView(target, new Color4(0, 0, 0, 0));
                scene.Render(context, target, frame);
                context.CopyResource(staging, targetTexture);

                var pixels = ReadPixels(context, staging, width, height);
                Composite(pixels);
                Save(pixels, width, height, Path.Combine(dir, $"f{index:D3}.png"));
            }
        }
        finally
        {
            scene.ReleaseDevice();
            device.Dispose();
        }
    }

    /// <summary>Handles <c>--render-gpu-test=&lt;dir&gt;</c>: 30 frames of the diagnostics scene at 2x.</summary>
    public static bool TryRenderTest(string[] args)
    {
        const string option = "--render-gpu-test=";
        var argument = args.FirstOrDefault(a => a.StartsWith(option, StringComparison.OrdinalIgnoreCase));
        if (argument is null) return false;

        string folder = Path.GetFullPath(argument[option.Length..]);
        try
        {
            using var scene = new GpuTestScene();
            RenderFrames(scene, 1920, 152, 2, 30, null, folder);
            File.WriteAllText(
                Path.Combine(folder, "render-info.txt"),
                "GpuTestScene: D3D11 + BGRA render target and staging readback; 1920x152 at 2 px/dip, 30 frames at 30 fps, "
                + "composited over #0B0E14.\n");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Floe.Gpu] preview failed: {ex}");
            try { File.WriteAllText(Path.Combine(folder, "render-error.txt"), ex.ToString()); } catch { }
            Environment.ExitCode = 1;
        }

        return true;
    }

    private static byte[] ReadPixels(ID3D11DeviceContext context, ID3D11Texture2D staging, int width, int height)
    {
        int stride = width * 4;
        var pixels = new byte[stride * height];
        var mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(IntPtr.Add(mapped.DataPointer, y * (int)mapped.RowPitch), pixels, y * stride, stride);
            }
        }
        finally
        {
            context.Unmap(staging, 0);
        }

        return pixels;
    }

    // The scene writes premultiplied BGRA; the PNG is the same light over the dock's dark backdrop.
    private static void Composite(byte[] pixels)
    {
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int alpha = pixels[i + 3];
            int inverse = 255 - alpha;
            pixels[i] = (byte)Math.Min(255, pixels[i] + (((BackdropB * inverse) + 127) / 255));
            pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] + (((BackdropG * inverse) + 127) / 255));
            pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] + (((BackdropR * inverse) + 127) / 255));
            pixels[i + 3] = 255;
        }
    }

    private static void Save(byte[] pixels, int width, int height, string path)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
