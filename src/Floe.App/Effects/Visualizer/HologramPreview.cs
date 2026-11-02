using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floe.App.Services;

namespace Floe.App.Effects.Visualizer;

/// <summary>
/// Offscreen render hook for the hologram visualizer, used by the diagnostic
/// <c>--render-hologram=&lt;png&gt;</c> and <c>--render-hologram-low=&lt;png&gt;</c>
/// switches. Both compose the same element over a dark stage: the regular
/// switch at level 0.8, the low switch at 0.15.
/// </summary>
public static class HologramPreview
{
    private const int Width = 760;
    private const int Height = 150;
    private const double CapsuleWidth = 640;
    private const double CapsuleHeight = 70;

    public static bool TryRender(string[] args)
    {
        const string normalOption = "--render-hologram=";
        const string lowOption = "--render-hologram-low=";

        double level = 0.8;
        string option = normalOption;
        var argument = args.FirstOrDefault(a => a.StartsWith(normalOption, StringComparison.OrdinalIgnoreCase));
        if (argument is null)
        {
            argument = args.FirstOrDefault(a => a.StartsWith(lowOption, StringComparison.OrdinalIgnoreCase));
            if (argument is null)
            {
                return false;
            }

            level = 0.15;
            option = lowOption;
        }

        string? path = null;
        try
        {
            // Optional palette override, e.g. --hologram-tint=Gold. Preview only:
            // the user's saved tint is left untouched.
            const string tintOption = "--hologram-tint=";
            var tintArgument = args.FirstOrDefault(a => a.StartsWith(tintOption, StringComparison.OrdinalIgnoreCase));
            if (tintArgument is not null &&
                Enum.TryParse<HologramColor>(tintArgument[tintOption.Length..], true, out var tint))
            {
                GlassSettings.PreviewTint(tint);
            }

            path = Path.GetFullPath(argument[option.Length..]);
            string? folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            var capsule = new Rect(
                (Width - CapsuleWidth) * 0.5,
                Height - 12 - CapsuleHeight,
                CapsuleWidth,
                CapsuleHeight);

            var stage = new Canvas
            {
                Width = Width,
                Height = Height,
                Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0E, 0x14))
            };

            var hologram = new HologramVisualizer
            {
                Width = Width,
                Height = Height,
                Capsule = capsule,
                DebugLevel = level,
                DebugTime = 1.5
            };
            stage.Children.Add(hologram);

            // The glass shell is only an outline here: the light must read as
            // coming from inside/below it, not painted over the face.
            var shell = new Border
            {
                Width = CapsuleWidth,
                Height = CapsuleHeight,
                CornerRadius = new CornerRadius(CapsuleHeight / 2),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x9F, 0xD4, 0xFF)),
                Background = Brushes.Transparent
            };
            Canvas.SetLeft(shell, capsule.Left);
            Canvas.SetTop(shell, capsule.Top);
            stage.Children.Add(shell);

            stage.Measure(new Size(Width, Height));
            stage.Arrange(new Rect(0, 0, Width, Height));
            stage.UpdateLayout();

            var target = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
            target.Render(stage);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(target));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }
        catch (Exception ex)
        {
            try
            {
                if (path is not null)
                {
                    File.WriteAllText(path + ".error.txt", ex.ToString());
                }
            }
            catch
            {
                // diagnostics must never replace the original failure
            }

            Environment.ExitCode = 1;
        }

        return true;
    }
}
