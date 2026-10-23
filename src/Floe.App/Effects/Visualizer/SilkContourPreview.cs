using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floe.App.Services;
using Floe.Core.Interop;

namespace Floe.App.Effects.Visualizer;

/// <summary>
/// --render-silk-contour=&lt;dir&gt; writes 150 frames of the real WPF host, with a 76-DIP strip.
/// The deterministic sequence includes music then silence; --contour-live instead captures the default
/// Windows output for five seconds and logs its analysed spectrum. --stage-light checks a light backdrop.
/// </summary>
public static class SilkContourPreview
{
    public static bool TryRender(string[] args)
    {
        const string option = "--render-silk-contour=";
        var argument = args.FirstOrDefault(a => a.StartsWith(option, StringComparison.OrdinalIgnoreCase));
        if (argument is null) return false;
        var dir = Path.GetFullPath(argument[option.Length..]);
        Directory.CreateDirectory(dir);
        const string paletteOption = "--dust-colors=";
        var paletteArgument = args.FirstOrDefault(a => a.StartsWith(paletteOption, StringComparison.OrdinalIgnoreCase));
        GlassSettings.PreviewDustColors(paletteArgument is not null && Enum.TryParse<DustPalette>(paletteArgument[paletteOption.Length..], true, out var palette) ? palette : DustPalette.Silk);
        const string tintOption = "--hologram-tint=";
        var tintArgument = args.FirstOrDefault(a => a.StartsWith(tintOption, StringComparison.OrdinalIgnoreCase));
        if (tintArgument is not null && Enum.TryParse<HologramColor>(tintArgument[tintOption.Length..], true, out var tint)) GlassSettings.PreviewTint(tint);

        const int width = 1920, height = 120, frames = 150;
        bool live = args.Contains("--contour-live", StringComparer.OrdinalIgnoreCase);
        Brush backdrop = args.Contains("--stage-light", StringComparer.OrdinalIgnoreCase)
            ? new SolidColorBrush(Color.FromRgb(0xB8, 0xC4, 0xD0))
            : new SolidColorBrush(Color.FromRgb(0x0B, 0x0E, 0x14));
        var stage = new Canvas { Width = width, Height = height, Background = backdrop };
        var frame = new SpectrumFrame(24);
        var contour = new SilkContourVisualizer
        {
            Width = width,
            Height = height,
            Capsule = new Rect(0, height - 76, width, 76),
            DebugFrame = frame,
        };
        stage.Children.Add(contour);
        stage.Measure(new Size(width, height));
        stage.Arrange(new Rect(0, 0, width, height));
        using var spectrum = live ? new AudioSpectrum() : null;
        using var log = live ? new StreamWriter(Path.Combine(dir, "spectrum.txt")) : null;
        if (live) Thread.Sleep(250);
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < frames; i++)
        {
            double time = i / 30.0;
            if (spectrum is not null)
            {
                double wait = time - clock.Elapsed.TotalSeconds;
                if (wait > 0) Thread.Sleep(TimeSpan.FromSeconds(wait));
                spectrum.Read(frame, 1 / 30.0);
                log!.WriteLine($"{i:000} level={frame.Level:F3} bass={frame.Bass:F3} mid={frame.Mid:F3} treble={frame.Treble:F3} kick={frame.Kick:F3} snare={frame.Snare:F3} hat={frame.Hat:F3}");
            }
            else
            {
                Feed(frame, time);
            }

            contour.Step(1 / 30.0);
            contour.InvalidateVisual();
            stage.UpdateLayout();
            var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            target.Render(stage);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(target));
            using var stream = File.Create(Path.Combine(dir, $"f{i:000}.png"));
            encoder.Save(stream);
        }

        return true;
    }

    private static void Feed(SpectrumFrame frame, double time)
    {
        if (time < .25 || time >= 3)
        {
            frame.Level = frame.Bass = frame.Mid = frame.Treble = frame.Kick = frame.Snare = frame.Hat = 0;
            Array.Clear(frame.Bands);
            return;
        }

        double beat = time % .5, swell = Math.Exp(-beat * 10);
        frame.Level = (float)(.45 + (.35 * swell));
        frame.Bass = (float)(.35 + (.5 * swell));
        frame.Mid = (float)(.45 + (.20 * Math.Sin(time * 1.7)));
        frame.Treble = (float)(.35 + (.15 * Math.Sin(time * 2.9)));
        frame.Kick = beat < 1 / 30.0 ? .8f : 0;
        frame.Snare = Math.Abs(beat - .25) < 1 / 60.0 ? .6f : 0;
        frame.Hat = ((int)(time * 30) % 4) == 2 ? .4f : 0;
        for (int band = 0; band < frame.Bands.Length; band++)
        {
            frame.Bands[band] = (float)Math.Clamp((.4 + (.25 * Math.Sin((time * 1.3) + (band * .45)))) * (1 - (band / 48.0)), 0, 1);
        }
    }
}
