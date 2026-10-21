using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MacDock.App.Effects.Visualizer;

/// <summary>
/// Offscreen render hook for the mist visualizer, used by the diagnostic
/// <c>--render-mist=&lt;png&gt;</c> and <c>--render-mist-low=&lt;png&gt;</c>
/// switches. Both compose the same 2560x76 DIP strip the visualizer really
/// covers on a 4K/150% screen: the top row at 1x, the bottom row as a 2x crop
/// of the centre 1280 DIP. A faint glass dock capsule sits over both rows.
/// The regular switch renders level 0.8, the low switch level 0.15.
/// </summary>
public static class MistPreview
{
    private const int StripWidth = 2560;
    private const int StripHeight = 76;

    private const double DockWidth = 1030;
    private const double DockHeight = 65;
    private const double DockRadius = 20;

    private const int Padding = 16;
    private const int RowGap = 14;
    private const int ZoomedHeight = StripHeight * 2;

    private static readonly Color Background = Color.FromRgb(0x0B, 0x0E, 0x14);

    public static bool TryRender(string[] args)
    {
        const string normalOption = "--render-mist=";
        const string lowOption = "--render-mist-low=";

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
            path = Path.GetFullPath(argument[option.Length..]);
            string? folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            // The whole strip is the capsule, exactly as VisualizerWindow
            // feeds it: full screen width, 76 DIP tall.
            var stage = new Canvas
            {
                Width = StripWidth,
                Height = StripHeight,
                Background = new SolidColorBrush(Background)
            };

            var mist = new MistVisualizer
            {
                Width = StripWidth,
                Height = StripHeight,
                Capsule = new Rect(0, 0, StripWidth, StripHeight),
                DebugLevel = level
            };
            stage.Children.Add(mist);

            stage.Measure(new Size(StripWidth, StripHeight));
            stage.Arrange(new Rect(0, 0, StripWidth, StripHeight));
            stage.UpdateLayout();

            var fullStrip = Render(stage, 1.0, StripWidth, StripHeight);
            var zoomedStrip = Render(stage, 2.0, StripWidth * 2, ZoomedHeight);

            // Centre 1280 DIP at 2x -> 2560 px, so it lines up with the 1x row.
            var centreCrop = new CroppedBitmap(zoomedStrip,
                new Int32Rect((StripWidth * 2 - StripWidth) / 2, 0, StripWidth, ZoomedHeight));

            int rowOneTop = Padding;
            int rowTwoTop = rowOneTop + StripHeight + RowGap;
            int canvasHeight = rowTwoTop + ZoomedHeight + Padding;

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Background), null,
                    new Rect(0, 0, StripWidth, canvasHeight));
                dc.DrawImage(fullStrip, new Rect(0, rowOneTop, StripWidth, StripHeight));
                dc.DrawImage(centreCrop, new Rect(0, rowTwoTop, StripWidth, ZoomedHeight));

                // Fake dock glass, centred over the strip: white 8% fill,
                // 25% stroke, radius 20 - the real dock is ~1030x65 DIP.
                DrawDock(dc,
                    new Rect((StripWidth - DockWidth) * 0.5,
                        rowOneTop + (StripHeight - DockHeight) * 0.5,
                        DockWidth, DockHeight),
                    DockRadius, 1.0);

                // Same capsule at 2x in the zoomed row.
                DrawDock(dc,
                    new Rect((StripWidth - DockWidth * 2) * 0.5,
                        rowTwoTop + (ZoomedHeight - DockHeight * 2) * 0.5,
                        DockWidth * 2, DockHeight * 2),
                    DockRadius * 2, 2.0);
            }

            var target = new RenderTargetBitmap(StripWidth, canvasHeight, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);

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

    private static RenderTargetBitmap Render(Visual visual, double scale, int pixelWidth, int pixelHeight)
    {
        double dpi = 96 * scale;
        var target = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    private static void DrawDock(DrawingContext dc, Rect bounds, double radius, double thickness)
    {
        var fill = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        var stroke = new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), thickness);
        dc.DrawGeometry(fill, stroke, new RectangleGeometry(bounds, radius, radius));
    }
}
