using System.IO;
using Floe.App.Services;
using Floe.Core.Interop;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// --render-gpu-oled=&lt;dir&gt; writes 180 PNG frames (30 fps) of <see cref="OledGpuScene"/> on the real dock strip,
/// 3840x152 (1920x76 DIP), fed the same made-up beat as the other previews, plus a made-up scope trace. --oled-effect=&lt;0..10&gt; holds
/// one preset; without it the presets take turns every two seconds so one run shows several. --oled-theme=legacy|ares
/// dresses the panel in that Tron theme, its colours and its grid floor. A made-up song sits in the left wing;
/// --oled-idle plays nothing instead, so the robot comes out.
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
        const string themeOption = "--oled-theme=";
        string? theme = args.FirstOrDefault(a => a.StartsWith(themeOption, StringComparison.OrdinalIgnoreCase))?[themeOption.Length..];

        try
        {
            bool idle = args.Contains("--oled-idle", StringComparer.OrdinalIgnoreCase);
            using var scene = new OledGpuScene { CycleSeconds = 2, MusicPlaying = !idle };
            if (int.TryParse(effect, out var forced)) scene.ForcedEffect = forced;
            if (string.Equals(theme, "legacy", StringComparison.OrdinalIgnoreCase)) scene.Theme = DockTheme.TronLegacy;
            else if (string.Equals(theme, "ares", StringComparison.OrdinalIgnoreCase)) scene.Theme = DockTheme.TronAres;
            scene.SetDock(.265f, .735f);
            scene.SetMedia("Zülüf Dökülmüş Yüze (Live at Harbiye Açıkhava)", "Minor Empire", 83, 245, !idle, Cover());
            GpuOffscreen.RenderFrames(scene, 3840, 152, 2, Frames, (frame, index) =>
            {
                if (idle) return;
                Fake(frame.Audio, index / 30.0);
            }, folder);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Floe.Gpu] oled preview failed: {ex}");
            try { File.WriteAllText(Path.Combine(folder, "render-error.txt"), ex.ToString()); } catch { }
            Environment.ExitCode = 1;
        }

        return true;
    }

    // A made-up cover: a sun over stripes, so the dither has edges and greys to show.
    private static byte[] Cover()
    {
        int n = OledOverlay.CoverPixels;
        var pixels = new byte[n * n * 4];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                double u = (x / (double)n) - .5, v = (y / (double)n) - .4, r = Math.Sqrt((u * u) + (v * v));
                double g = r < .25 ? 1 - (r * 2) : y > n * .65 ? ((y / 4) % 2 == 0 ? .7 : .15) : .1 + (.3 * y / n);
                byte b = (byte)(Math.Clamp(g, 0, 1) * 255);
                int i = ((y * n) + x) * 4;
                pixels[i] = b; pixels[i + 1] = b; pixels[i + 2] = b; pixels[i + 3] = 255;
            }
        }

        return pixels;
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

        // a bass note that thumps with the kick under a busier line on top
        var wave = frame.Wave;
        for (int i = 0; i < wave.Length; i++)
        {
            double s = i / (double)wave.Length;
            wave[i] = (float)Math.Clamp((.55 + (.4 * kick)) * Math.Sin(s * Math.Tau * 2) + (.3 * Math.Sin((s * 37) + (t * 5))) + (.12 * Math.Sin((s * 140) - (t * 11))), -1, 1);
        }
    }
}
