using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floe.App.Services;

namespace Floe.App.Effects.Visualizer;

/// <summary>
/// --render-cubes=&lt;dir&gt; writes 90 PNG frames (30 fps) of the cube visualizer over a dark stage, the level
/// swinging like a beat; --hologram-tint=&lt;name&gt; previews another tint.
/// </summary>
public static class CubePreview
{
    private const int Width = 900;
    private const int Height = 110;
    private const int Frames = 90;

    public static bool TryRender(string[] args)
    {
        const string option = "--render-cubes=";
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

            var dir = Path.GetFullPath(argument[option.Length..]);
            Directory.CreateDirectory(dir);
            var stage = new Canvas { Width = Width, Height = Height, Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0E, 0x14)) };
            var cubes = new CubeVisualizer { Width = Width, Height = Height, Capsule = new Rect(0, Height - 82, Width, 82) };
            stage.Children.Add(cubes);
            stage.Measure(new Size(Width, Height));
            stage.Arrange(new Rect(0, 0, Width, Height));

            for (int f = 0; f < Frames; f++)
            {
                double t = f / 30.0;
                // a kick every half second over a slower swell
                double beat = Math.Exp(-((t % .5) * 9));
                cubes.DebugLevel = Math.Clamp(.25 + (.35 * beat) + (.3 * Math.Sin(t * 1.3)), 0, 1);
                cubes.Step(1 / 30.0);
                cubes.InvalidateVisual();
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
}
