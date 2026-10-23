using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floe.App.Services;
using Floe.Core.Interop;

namespace Floe.App.Effects.Visualizer;

/// <summary>
/// --render-stardust=&lt;dir&gt; writes 90 PNG frames (30 fps) of the star dust visualizer over a dark stage, fed a
/// made-up beat; --stardust-live feeds it what the speakers play instead, in real time, and writes what the
/// analysis heard to spectrum.txt. --hologram-tint=&lt;name&gt; previews another tint.
/// </summary>
public static class StardustPreview
{
    private const int Width = 900;
    private const int Height = 110;
    private const int Frames = 90;

    public static bool TryRender(string[] args)
    {
        const string option = "--render-stardust=";
        var argument = args.FirstOrDefault(a => a.StartsWith(option, StringComparison.OrdinalIgnoreCase));
        if (argument is null) return false;
        try
        {
            const string tintOption = "--hologram-tint=";
            var tintArgument = args.FirstOrDefault(a => a.StartsWith(tintOption, StringComparison.OrdinalIgnoreCase));
            if (tintArgument is not null && Enum.TryParse<HologramColor>(tintArgument[tintOption.Length..], true, out var tint))
            {
                GlassSettings.PreviewTint(tint);
            }

            bool live = args.Contains("--stardust-live", StringComparer.OrdinalIgnoreCase);
            var dir = Path.GetFullPath(argument[option.Length..]);
            Directory.CreateDirectory(dir);
            var stage = new Canvas { Width = Width, Height = Height, Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0E, 0x14)) };
            var dust = new StardustVisualizer { Width = Width, Height = Height, Capsule = new Rect(0, Height - 82, Width, 82) };
            stage.Children.Add(dust);
            stage.Measure(new Size(Width, Height));
            stage.Arrange(new Rect(0, 0, Width, Height));

            var frame = new SpectrumFrame(24);
            dust.DebugFrame = frame;
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
                    spectrum.Read(frame, 1 / 30.0);
                    log!.WriteLine($"{f:000} level={frame.Level:F2} bass={frame.Bass:F2} mid={frame.Mid:F2} treble={frame.Treble:F2} kick={frame.Kick:F2} snare={frame.Snare:F2} hat={frame.Hat:F2} bands={string.Join(' ', frame.Bands.Select(b => (int)(b * 9)))}");
                }
                else
                {
                    Fake(frame, t);
                }

                dust.Step(1 / 30.0);
                dust.InvalidateVisual();
                stage.UpdateLayout();
                var target = new RenderTargetBitmap(Width * 2, Height * 2, 192, 192, PixelFormats.Pbgra32);
                target.Render(stage);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(target));
                using var stream = File.Create(Path.Combine(dir, $"f{f:000}.png"));
                encoder.Save(stream);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
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
