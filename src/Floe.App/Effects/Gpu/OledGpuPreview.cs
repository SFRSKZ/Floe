using System.IO;
using Floe.Core.Interop;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// --render-gpu-oled=&lt;dir&gt; writes 180 PNG frames (30 fps) of <see cref="OledGpuScene"/> on the real dock strip,
/// 3840x152 (1920x76 DIP), fed the same made-up beat as the other previews. --oled-effect=&lt;0|1|2&gt; holds one
/// effect; without it the effects take turns every two seconds so one run shows them all.
/// </summary>
public static class OledGpuPreview
{
    private const int Frames = 180;

    public static bool TryRender(string[] args)
    {
        const string option = "--render-gpu-oled=";
        var argument = args.FirstOrDefault(a => a.StartsWith(option, StringComparison.OrdinalIgnoreCase));
        if (argument is null) return false;

        string folder = Path.GetFullPath(argument[option.Length..]);
        const string effectOption = "--oled-effect=";
        string? effect = args.FirstOrDefault(a => a.StartsWith(effectOption, StringComparison.OrdinalIgnoreCase))?[effectOption.Length..];

        try
        {
            using var scene = new OledGpuScene { CycleSeconds = 2 };
            if (int.TryParse(effect, out var forced)) scene.ForcedEffect = forced;
            GpuOffscreen.RenderFrames(scene, 3840, 152, 2, Frames, (frame, index) => Fake(frame.Audio, index / 30.0), folder);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Floe.Gpu] oled preview failed: {ex}");
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
