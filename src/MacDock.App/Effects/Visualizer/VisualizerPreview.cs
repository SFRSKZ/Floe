using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MacDock.App.Effects.Visualizer;

/// <summary>
/// Offscreen render hook for the visualizer glow, used by the diagnostic
/// <c>--render-visualizer=&lt;png&gt;</c> switch. It composes the same glow the
/// dock draws over a dark stage and writes a PNG, so the look can be reviewed
/// without audio or a live window.
/// </summary>
public static class VisualizerPreview
{
    private const int Width = 700;
    private const int Height = 140;

    public static bool TryRender(string[] args)
    {
        const string option = "--render-visualizer=";
        var argument = args.FirstOrDefault(a => a.StartsWith(option, StringComparison.OrdinalIgnoreCase));
        if (argument is null)
        {
            return false;
        }

        string? path = null;
        try
        {
            path = Path.GetFullPath(argument[option.Length..]);
            string? folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            var capsule = new Rect(50, 32, 600, 62);

            var stage = new Canvas
            {
                Width = Width,
                Height = Height,
                Background = new LinearGradientBrush(
                    Color.FromRgb(0x12, 0x12, 0x18),
                    Color.FromRgb(0x05, 0x05, 0x08),
                    90)
            };

            var glow = new VisualizerGlow
            {
                Width = Width,
                Height = Height,
                Capsule = capsule,
                DebugLevel = 0.62
            };
            stage.Children.Add(glow);

            // The capsule fill sits above the glow, so the light reads as a
            // halo escaping from under the glass rather than a coloured wash
            // over its face.
            var shell = new Border
            {
                Width = capsule.Width,
                Height = capsule.Height,
                CornerRadius = new CornerRadius(capsule.Height / 2),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)),
                Background = new LinearGradientBrush(
                    Color.FromRgb(0x2C, 0x2C, 0x36),
                    Color.FromRgb(0x16, 0x16, 0x1D),
                    90)
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
