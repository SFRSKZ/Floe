using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floe.App.Effects.Visualizer;
using Floe.App.Services;
using Floe.Core.Interop;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Color = System.Windows.Media.Color;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// --render-gpu-stardust=&lt;dir&gt; writes 90 PNG frames (30 fps) of <see cref="StardustGpuScene"/> over a dark stage,
/// fed the same made-up beat as the CPU preview; --stardust-wave renders the dust wave look and --stardust-live
/// feeds it what the speakers play instead, in real time. --dust-colors=&lt;name&gt; previews other colours.
/// <para>
/// It runs its own little offscreen swap: a hardware D3D11 device (WARP when there is none), a 1800x220 render
/// target read back into WPF bitmaps. The GPU host owns its own path, so the two never share device state.
/// </para>
/// </summary>
public static class StardustGpuPreview
{
    private const int Width = 1800;
    private const int Height = 220;
    private const int Frames = 90;
    private static readonly Color BasePrimary = Color.FromRgb(0x5C, 0xA8, 0xFF);
    private static readonly Color Stage = Color.FromRgb(0x0B, 0x0E, 0x14);

    public static bool TryRender(string[] args)
    {
        const string option = "--render-gpu-stardust=";
        var argument = args.FirstOrDefault(a => a.StartsWith(option, StringComparison.OrdinalIgnoreCase));
        if (argument is null) return false;
        try
        {
            const string dustOption = "--dust-colors=";
            var dustArgument = args.FirstOrDefault(a => a.StartsWith(dustOption, StringComparison.OrdinalIgnoreCase));
            if (dustArgument is not null && Enum.TryParse<DustPalette>(dustArgument[dustOption.Length..], true, out var palette))
            {
                GlassSettings.PreviewDustColors(palette);
            }

            bool live = args.Contains("--stardust-live", StringComparer.OrdinalIgnoreCase);
            bool wave = args.Contains("--stardust-wave", StringComparer.OrdinalIgnoreCase);
            var dir = Path.GetFullPath(argument[option.Length..]);
            Directory.CreateDirectory(dir);

            var device = CreateDevice(out var context);
            try
            {
                using var target = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, Width, Height, 1, 1,
                    BindFlags.RenderTarget, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
                using var view = device.CreateRenderTargetView(target, null);
                using var staging = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, Width, Height, 1, 1,
                    BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read, 1, 0, ResourceOptionFlags.None));

                using var scene = new StardustGpuScene();
                scene.Initialize(device);
                scene.Resize(Width, Height);

                var red = new float[GpuFrame.PaletteSize];
                var green = new float[GpuFrame.PaletteSize];
                var blue = new float[GpuFrame.PaletteSize];
                var frame = new GpuFrame { Width = Width, Height = Height, PixelsPerDip = 2, Dt = 1 / 30f, Fade = 1, Wave = wave, Performance = Performance.Level };

                using var spectrum = live ? new AudioSpectrum() : null;
                using var log = live ? new StreamWriter(Path.Combine(dir, "spectrum.txt")) : null;
                if (live) Thread.Sleep(400);
                var started = DateTime.UtcNow;

                for (int f = 0; f < Frames; f++)
                {
                    double t = f / 30.0;
                    if (spectrum is not null)
                    {
                        // hold the real pace, so the capture has fresh samples for every frame
                        var due = started + TimeSpan.FromSeconds(t);
                        var wait = due - DateTime.UtcNow;
                        if (wait > TimeSpan.Zero) Thread.Sleep(wait);
                        spectrum.Read(frame.Audio, 1 / 30.0);
                        log!.WriteLine($"{f:000} level={frame.Audio.Level:F2} bass={frame.Audio.Bass:F2} mid={frame.Audio.Mid:F2} treble={frame.Audio.Treble:F2} kick={frame.Audio.Kick:F2} snare={frame.Audio.Snare:F2} hat={frame.Audio.Hat:F2} bands={string.Join(' ', frame.Audio.Bands.Select(b => (int)(b * 9)))}");
                    }
                    else
                    {
                        Fake(frame.Audio, t);
                    }

                    frame.Time = (f + 1) / 30f;
                    VisualizerPalette.Fill(red, green, blue, frame.Time, BasePrimary);
                    for (int i = 0; i < GpuFrame.PaletteSize; i++) frame.Palette[i] = new Vector3(red[i], green[i], blue[i]);

                    context.ClearRenderTargetView(view, new Color4(0, 0, 0, 0));
                    scene.Render(context, view, frame);
                    context.CopyResource(staging, target);
                    Save(context, staging, Path.Combine(dir, $"f{f:000}.png"));
                }
            }
            finally
            {
                device.Dispose();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Environment.ExitCode = 1;
        }

        return true;
    }

    private static ID3D11Device CreateDevice(out ID3D11DeviceContext context)
    {
        var levels = new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };
        try
        {
            D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Hardware, DeviceCreationFlags.BgraSupport, levels, out var device, out context);
            return device;
        }
        catch
        {
            // no GPU, or a remote session: WARP runs the same shaders
            D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Warp, DeviceCreationFlags.BgraSupport, levels, out var device, out context);
            return device;
        }
    }

    // Reads the frame back and lays it over the opaque stage, since the scene writes premultiplied alpha.
    private static void Save(ID3D11DeviceContext context, ID3D11Texture2D staging, string path)
    {
        var row = new byte[Width * 4];
        var pixels = new byte[Width * Height * 4];
        var mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            for (int y = 0; y < Height; y++)
            {
                Marshal.Copy(IntPtr.Add(mapped.DataPointer, y * (int)mapped.RowPitch), row, 0, row.Length);
                int o = y * Width * 4;
                for (int x = 0; x < row.Length; x += 4)
                {
                    int inverse = 255 - row[x + 3];
                    pixels[o + x] = (byte)Math.Min(255, row[x] + ((Stage.B * inverse + 127) / 255));
                    pixels[o + x + 1] = (byte)Math.Min(255, row[x + 1] + ((Stage.G * inverse + 127) / 255));
                    pixels[o + x + 2] = (byte)Math.Min(255, row[x + 2] + ((Stage.R * inverse + 127) / 255));
                    pixels[o + x + 3] = 255;
                }
            }
        }
        finally
        {
            context.Unmap(staging, 0);
        }

        var bitmap = BitmapSource.Create(Width, Height, 192, 192, PixelFormats.Pbgra32, null, pixels, Width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    // A kick every half second, a snare on the off-beat, hats in between, over a slowly moving spectrum.
    private static void Fake(SpectrumFrame frame, double t)
    {
        double beat = t % .5;
        double kick = Math.Exp(-beat * 9);
        frame.Kick = beat < 1 / 30.0 ? 1 : 0;
        frame.Snare = Math.Abs(beat - .25) < 1 / 60.0 && ((int)(t * 2) % 2) == 1 ? .8f : 0;
        frame.Hat = ((int)(t * 30) % 4) == 2 ? .6f : 0;
        frame.Bass = (float)(.4 + (.6 * kick));
        frame.Mid = (float)(.5 + (.3 * Math.Sin(t * 2.1)));
        frame.Treble = (float)(.4 + (.3 * Math.Sin(t * 3.7)));
        frame.Level = (float)(.5 + (.4 * kick));
        for (int b = 0; b < frame.Bands.Length; b++)
        {
            double x = b / (double)frame.Bands.Length;
            double shape = (b < 4 ? .5 + (.5 * kick) : .55) * (1 - (.4 * x));
            frame.Bands[b] = (float)Math.Clamp(shape * (.6 + (.4 * Math.Sin((t * (1.5 + x * 3)) + (b * .7)))), 0, 1);
        }
    }
}
