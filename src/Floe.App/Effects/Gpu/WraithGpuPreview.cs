using System.IO;
using Floe.Core.Interop;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// --render-gpu-wraith=&lt;dir&gt; writes 120 PNG frames (30 fps, 1800x240 at 2 px/DIP) of
/// <see cref="WraithGpuScene"/> over the dock's backdrop, fed the same made-up beat as the other previews;
/// --stardust-live feeds it what the speakers play instead, in real time.
/// <para>
/// The frames go through <see cref="GpuOffscreen.RenderFrames"/>, which owns the device and the readback:
/// 120 frames because a feedback field needs a couple of seconds before it has built its fog.
/// </para>
/// </summary>
public static class WraithGpuPreview
{
    private const int Width = 1800;
    private const int Height = 240;
    private const int Frames = 120;

    public static bool TryRender(string[] args)
    {
        const string option = "--render-gpu-wraith=";
        var argument = args.FirstOrDefault(a => a.StartsWith(option, StringComparison.OrdinalIgnoreCase));
        if (argument is null) return false;

        string folder = Path.GetFullPath(argument[option.Length..]);
        bool live = args.Contains("--stardust-live", StringComparer.OrdinalIgnoreCase);
        try
        {
            using var scene = new WraithGpuScene();
            using var spectrum = live ? new AudioSpectrum() : null;
            if (live) Thread.Sleep(400);
            var started = DateTime.UtcNow;

            GpuOffscreen.RenderFrames(scene, Width, Height, 2, Frames, (frame, index) =>
            {
                if (spectrum is null)
                {
                    Fake(frame.Audio, index / 30.0);
                }
                else
                {
                    // hold the real pace, so the capture has fresh samples for every frame
                    var due = started + TimeSpan.FromSeconds(index / 30.0);
                    var wait = due - DateTime.UtcNow;
                    if (wait > TimeSpan.Zero) Thread.Sleep(wait);
                    spectrum.Read(frame.Audio, 1 / 30.0);
                }
            }, folder);

            File.WriteAllText(
                Path.Combine(folder, "render-info.txt"),
                "WraithGpuScene: two feedback passes (R16G16B16A16_FLOAT ping-pong), 1800x240 at 2 px/dip, "
                + $"{Frames} frames at 30 fps, {(live ? "live AudioSpectrum" : "Fake() beat")}, composited over #0B0E14.\n");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Floe.Gpu] wraith preview failed: {ex}");
            try { File.WriteAllText(Path.Combine(folder, "render-error.txt"), ex.ToString()); } catch { }
            Environment.ExitCode = 1;
        }

        return true;
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
