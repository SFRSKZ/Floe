using System.IO;
using Floe.Core.Interop;
using Vortice.Direct3D11;
using Vortice.Mathematics;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// --render-led-pass=&lt;dir&gt; writes 60 PNG frames (30 fps) of <see cref="StardustGpuScene"/> at 1920x152 through
/// <see cref="LedScreenPass"/>, on the dock's real 2x strip, fed the same made-up beat as the other previews.
/// <para>
/// The scene draws into the pass's offscreen picture and the pass turns that into the LED grid; the readback,
/// the backdrop and the PNGs are <see cref="GpuOffscreen"/>'s, so this preview shows exactly what the dock shows.
/// </para>
/// </summary>
public static class LedPassPreview
{
    private const int Width = 1920;
    private const int Height = 152;
    private const float PixelsPerDip = 2;
    private const int Frames = 60;

    public static bool TryRender(string[] args)
    {
        const string option = "--render-led-pass=";
        var argument = args.FirstOrDefault(a => a.StartsWith(option, StringComparison.OrdinalIgnoreCase));
        if (argument is null) return false;

        string folder = Path.GetFullPath(argument[option.Length..]);
        try
        {
            using var layered = new LedScene(new StardustGpuScene(), new LedScreenPass());
            GpuOffscreen.RenderFrames(layered, Width, Height, PixelsPerDip, Frames, (frame, index) => Fake(frame.Audio, index / 30.0), folder);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Floe.Gpu] led pass preview failed: {ex}");
            try { File.WriteAllText(Path.Combine(folder, "render-error.txt"), ex.ToString()); } catch { }
            Environment.ExitCode = 1;
        }

        return true;
    }

    // Feeds the scene's picture through the pass, so GpuOffscreen still does the readback, the backdrop and the files.
    private sealed class LedScene : IGpuScene
    {
        private readonly IGpuScene _scene;
        private readonly LedScreenPass _pass;

        public LedScene(IGpuScene scene, LedScreenPass pass)
        {
            _scene = scene;
            _pass = pass;
        }

        public void Initialize(ID3D11Device device)
        {
            _pass.Initialize(device);
            _scene.Initialize(device);
        }

        public void Resize(int width, int height)
        {
            _pass.Resize(width, height);
            _scene.Resize(width, height);
        }

        public void Render(ID3D11DeviceContext context, ID3D11RenderTargetView target, GpuFrame frame)
        {
            if (_pass.SceneTarget is not { } sceneTarget)
            {
                _scene.Render(context, target, frame);
                return;
            }

            context.ClearRenderTargetView(sceneTarget, new Color4(0, 0, 0, 0));
            _scene.Render(context, sceneTarget, frame);
            _pass.Apply(context, target, frame.Width, frame.Height, frame.PixelsPerDip, frame.Time);
        }

        public void ReleaseDevice()
        {
            _scene.ReleaseDevice();
            _pass.ReleaseDevice();
        }

        public void Dispose()
        {
            _scene.Dispose();
            _pass.Dispose();
        }
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
