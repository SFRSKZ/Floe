using System.IO;
using Floe.App.Services;
using Floe.Core.Interop;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// --render-gpu-hologram=&lt;dir&gt; writes 180 PNG frames (30 fps) of <see cref="HologramGpuScene"/> on the real dock
/// strip, 3840x152 (1920x76 DIP), fed a made-up beat. --holo-warp=&lt;0..3&gt; holds one warp; without it they take
/// turns every two seconds; --holo-plain leaves the echoes out. --hologram-tint=&lt;name&gt; previews a tint without saving it.
/// </summary>
public static class HologramGpuPreview
{
    private const int Frames = 180;

    public static bool TryRender(string[] args)
    {
        const string option = "--render-gpu-hologram=";
        var argument = args.FirstOrDefault(a => a.StartsWith(option, StringComparison.OrdinalIgnoreCase));
        if (argument is null) return false;

        string folder = Path.GetFullPath(argument[option.Length..]);
        const string warpOption = "--holo-warp=";
        string? warp = args.FirstOrDefault(a => a.StartsWith(warpOption, StringComparison.OrdinalIgnoreCase))?[warpOption.Length..];
        const string tintOption = "--hologram-tint=";
        string? tint = args.FirstOrDefault(a => a.StartsWith(tintOption, StringComparison.OrdinalIgnoreCase))?[tintOption.Length..];

        try
        {
            if (Enum.TryParse<HologramColor>(tint, true, out var color)) GlassSettings.PreviewTint(color);
            using var scene = new HologramGpuScene { CycleSeconds = 2 };
            if (int.TryParse(warp, out var forced)) scene.ForcedWarp = forced;
            if (args.Contains("--holo-plain", StringComparer.OrdinalIgnoreCase)) scene.Echo = 0;
            GpuOffscreen.RenderFrames(scene, 3840, 152, 2, Frames, (frame, index) => Fake(frame.Audio, index / 30.0), folder);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Floe.Gpu] hologram preview failed: {ex}");
            try { File.WriteAllText(Path.Combine(folder, "render-error.txt"), ex.ToString()); } catch { }
            Environment.ExitCode = 1;
        }

        return true;
    }

    // A kick every half second over a level that swells with it.
    private static void Fake(SpectrumFrame frame, double t)
    {
        double beat = t % .5;
        double kick = Math.Exp(-beat * 9);
        frame.Kick = beat < 1 / 30.0 ? 1 : 0;
        frame.Bass = (float)(.4 + (.6 * kick));
        frame.Mid = (float)(.5 + (.3 * Math.Sin(t * 2.1)));
        frame.Treble = (float)(.4 + (.3 * Math.Sin(t * 3.7)));
        frame.Level = (float)(.5 + (.4 * kick));
    }
}
