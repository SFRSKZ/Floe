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
    // The dock's backdrop, so a transparent strip looks like it does on screen; FLOE_GPU_BACKDROP=RRGGBB
    // swaps it for a lighter wallpaper, where dark effects show.
    private static readonly int Backdrop = int.TryParse(Environment.GetEnvironmentVariable("FLOE_GPU_BACKDROP"),
        System.Globalization.NumberStyles.HexNumber, null, out var backdrop) ? backdrop : 0x0B0E14;
    private static readonly byte BackdropR = (byte)(Backdrop >> 16);
    private static readonly byte BackdropG = (byte)(Backdrop >> 8);
    private static readonly byte BackdropB = (byte)Backdrop;

    private static readonly MediaColor BasePrimary = MediaColor.FromRgb(0x5C, 0xA8, 0xFF);
    private static readonly float[] PaletteR = new float[GpuFrame.PaletteSize];
    private static readonly float[] PaletteG = new float[GpuFrame.PaletteSize];
    private static readonly float[] PaletteB = new float[GpuFrame.PaletteSize];

    /// <summary>
    /// Initialises <paramref name="scene"/>, renders <paramref name="frames"/> frames and writes
    /// f000.png … to <paramref name="dir"/>. <paramref name="prepare"/> may fill Audio or flip Wave.
    /// </summary>
    public static void RenderFrames(IGpuScene scene, int width, int height, float pixelsPerDip, int frames, Action<GpuFrame, int>? prepare, string dir, int edge = 0)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        // the scene draws the bottom strip; on another edge (1 top, 2 left, 3 right) the picture is turned onto it
        // through the same pass the live strip uses
        int sceneWidth = width, sceneHeight = height;
        if (edge >= 2) (width, height) = (height, width);
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
            scene.Resize(sceneWidth, sceneHeight);
            using var turn = edge != 0 ? new EdgeTurnPass() : null;
            // --clear=a,b,...: the spans the taskbar keeps clear, cut as the live layer cuts them
            using var feather = edge == 0 && _lookClear is { Length: >= 2 } ? new ClearFeatherPass() : null;
            feather?.Initialize(device);
            turn?.Initialize(device);
            turn?.Resize(sceneWidth, sceneHeight);

            for (int index = 0; index < frames; index++)
            {
                float time = index / 30f;
                frame.Dt = 1f / 30f;
                frame.Time = time;
                frame.Fade = 1;
                frame.Width = sceneWidth;
                frame.Height = sceneHeight;
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
                if (turn?.SceneTarget is { } strip)
                {
                    context.ClearRenderTargetView(strip, new Color4(0, 0, 0, 0));
                    scene.Render(context, strip, frame);
                    turn.Apply(context, target, width, height, edge);
                }
                else
                {
                    scene.Render(context, target, frame);
                    if (feather is not null) feather.Apply(context, target, width, height, _lookClear!, (float)(40 * pixelsPerDip));
                }

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

    /// <summary>
    /// Handles <c>--render-gpu-look=Look;dir;frames;start</c>: renders one catalog visualizer look
    /// offscreen (3840×126 at 1.5 px/dip) against the synthetic 16 s mock track, so a scene can be
    /// judged without the dock. <paramref name="dir"/> is optional, as are the frame count and the
    /// start time in seconds (default 30 frames from t=8, the drop).
    /// </summary>
    // --edge=top|left|right with --render-gpu-look: the strip turned onto that edge, as the live layer does
    private static int LookEdge(string[] args)
        => args.FirstOrDefault(a => a.StartsWith("--edge=", StringComparison.OrdinalIgnoreCase)) is { } arg
            && Enum.TryParse<Floe.Core.Models.DockEdge>(arg["--edge=".Length..], true, out var edge) ? (int)edge : 0;

    private static float[]? _lookClear;

    public static bool TryRenderLook(string[] args)
    {
        if (args.FirstOrDefault(a => a.StartsWith("--clear=", StringComparison.OrdinalIgnoreCase)) is { } clearArg)
        {
            _lookClear = clearArg["--clear=".Length..].Split(',')
                .Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        }

        const string option = "--render-gpu-look=";
        var argument = args.FirstOrDefault(a => a.StartsWith(option, StringComparison.OrdinalIgnoreCase));
        if (argument is null) return false;

        var parts = argument[option.Length..].Split(';');
        string lookName = parts[0].Trim();
        string folder = parts.Length > 1 && parts[1].Length > 0
            ? Path.GetFullPath(parts[1])
            : Path.Combine(Path.GetTempPath(), "floe-look-" + lookName);
        int frames = parts.Length > 2 && int.TryParse(parts[2], out var count) ? Math.Max(1, count) : 30;
        float start = parts.Length > 3
            && float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var at)
                ? at
                : 8f;

        try
        {
            if (!Enum.TryParse<VisualizerStyle>(lookName, true, out var look) || !GpuSceneCatalog.Has(look))
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "render-error.txt"), $"unknown visualizer look: {lookName}");
                Environment.ExitCode = 2;
                return true;
            }

            using var scene = GpuSceneCatalog.Create(look);
            try
            {
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                foreach (var field in new[] { "_showSource", "_feedbackSource" })
                {
                    if (typeof(FeedbackGpuScene).GetField(field, flags)?.GetValue(scene) is string source)
                    {
                        Directory.CreateDirectory(folder);
                        File.WriteAllText(Path.Combine(folder, field.TrimStart('_') + ".hlsl"), source);                    }
                }
            }
            catch
            {
                // diagnostics only
            }

            // optional strip size and dock gap, so a look can be judged at the taskbar's 48 DIP:
            // ;width;height;pixelsPerDip;dockLeft;dockRight
            static float Part(string[] p, int i, float fallback) => p.Length > i
                && float.TryParse(p[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
            int width = (int)Part(parts, 4, 3840), height = (int)Part(parts, 5, 126);
            float ppd = Part(parts, 6, 1.5f), dockLeft = Part(parts, 7, .37f), dockRight = Part(parts, 8, .63f);
            RenderFrames(scene, width, height, ppd, frames, (frame, index) =>
            {
                MockFrame(frame, start + (index / 30f));
                frame.DockLeft = dockLeft;
                frame.DockRight = dockRight;
            }, folder, LookEdge(args));
            File.WriteAllText(
                Path.Combine(folder, "render-info.txt"),
                $"{look}: {width}x{height} at {ppd} px/dip, dock {dockLeft}..{dockRight}, {frames} frames from t={start:0.00}s of the 16 s mock track, "
                + "composited over #0B0E14.\n");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Floe.Gpu] look preview failed: {ex}");
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "render-error.txt"), ex.ToString());
            }
            catch
            {
                // nothing else to report with
            }

            Environment.ExitCode = 1;
        }

        return true;
    }

    // The design harness's mock track, in C#: a 16 s loop at 120 BPM with intro/build/drop/breakdown,
    // 24 log bands and the three onsets, so the offscreen frames move like a song does.
    private static void MockFrame(GpuFrame frame, float tRaw)
    {
        const float beat = 0.5f;
        const float bar = 4 * beat;
        float t = ((tRaw % 16f) + 16f) % 16f;
        frame.Time = tRaw;
        frame.Dt = 1f / 30f;
        frame.Fade = 1f - SmoothStep(15.25f, 15.95f, t);

        bool intro = t < 4f, build = t >= 4f && t < 8f, drop = t >= 8f && t < 12f;
        float inBeat = (t % beat) / beat;
        int beatInBar = (int)((t % bar) / beat);
        float kick = drop
            ? MathF.Exp(-inBeat * 9f)
            : build && beatInBar % 2 == 0
                ? 0.85f * MathF.Exp(-inBeat * 9f)
                : intro && beatInBar == 0
                    ? 0.55f * MathF.Exp(-inBeat * 9f)
                    : t >= 12f && beatInBar % 2 == 0
                        ? MathF.Exp(-inBeat * 9f) * Math.Clamp(1f - (t - 12f) / 4f, 0f, 1f)
                        : 0f;

        float posInBar = t % bar;
        float snareAge = posInBar >= beat && posInBar < 2 * beat ? posInBar - beat
            : posInBar >= 3 * beat ? posInBar - 3 * beat
            : 10f;
        float snare = build || drop ? MathF.Exp(-snareAge / beat * 11f) : 0f;
        float eighth = (t % (beat / 2f)) / (beat / 2f);
        float hat = build || drop ? MathF.Exp(-eighth * 20f) : 0f;
        bool silent = t > 15.2f;

        float baseLevel = intro ? 0.30f
            : build ? 0.30f + 0.55f * SmoothStep(4f, 8f, t)
            : drop ? 0.92f
            : 0.92f * (1f - SmoothStep(12f, 15.2f, t)) + 0.03f;

        var audio = frame.Audio;
        audio.Level = silent ? 0f : Math.Clamp(baseLevel * (0.86f + 0.14f * kick), 0f, 1f);
        audio.Bass = silent ? 0f : Math.Clamp(0.06f + 0.78f * kick + 0.10f * (0.5f + 0.5f * MathF.Sin(t * 1.1f)), 0f, 1f);
        audio.Mid = silent ? 0f : Math.Clamp(0.10f + 0.34f * (0.5f + 0.5f * MathF.Sin((t * 2.3f) + 1.2f)) + 0.30f * audio.Level, 0f, 1f);
        audio.Treble = silent ? 0f : Math.Clamp(0.06f + 0.66f * hat + 0.12f * (0.5f + 0.5f * MathF.Sin(t * 3.7f)), 0f, 1f);
        audio.Balance = 0.40f * MathF.Sin(2f * MathF.PI * t / 16f);
        audio.Kick = kick;
        audio.Snare = snare;
        audio.Hat = hat;

        for (int i = 0; i < audio.Bands.Length; i++)
        {
            float u = i / (float)Math.Max(1, audio.Bands.Length - 1);
            float low = MathF.Exp(-u * 6.5f);
            float mid = MathF.Exp(-MathF.Pow((u - 0.42f) / 0.27f, 2f));
            float high = MathF.Exp(-MathF.Pow((u - 0.86f) / 0.18f, 2f));
            float wobble = 0.5f + 0.5f * MathF.Sin((t * (0.8f + (i * 0.13f))) + (i * 1.7f));
            float value = 0.02f
                + (0.58f * low * (0.30f + (0.78f * kick)))
                + (0.46f * mid * (0.22f + (0.62f * audio.Mid)) * (0.55f + (0.45f * wobble)))
                + (0.55f * high * (0.16f + (0.84f * hat)));
            value *= silent ? 0f : 0.92f + (0.08f * MathF.Sin((i * 3.1f) + (t * 0.9f)));
            audio.Bands[i] = Math.Clamp(value, 0f, 1f);
        }
    }

    private static float SmoothStep(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - (2f * t));
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
