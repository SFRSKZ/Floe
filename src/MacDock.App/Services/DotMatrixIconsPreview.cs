using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MacDock.App.Services;

/// <summary>
/// Offscreen contact sheet for the dot-matrix weather icons, used by the
/// diagnostic <c>--render-weather-icons=&lt;png&gt;</c> switch. Draws all keys on
/// a black stage (7 columns x 2 rows, 64 DIP icons with their key underneath)
/// so the set can be reviewed without starting the dock.
/// </summary>
public static class DotMatrixIconsPreview
{
    private static readonly string[] Keys =
    {
        "sunny", "mostly_sunny", "partly_cloudy", "overcast", "cloudy", "fog", "light_rain",
        "rain", "heavy_rain", "sleet", "light_snow", "snow", "heavy_snow", "thunderstorm",
    };

    private const int Columns = 7;
    private const int IconSize = 64;
    private const int CellWidth = 104;
    private const int CellHeight = 112;
    private const int Margin = 20;

    public static bool TryRender(string[] args)
    {
        const string option = "--render-weather-icons=";
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

            int rows = (Keys.Length + Columns - 1) / Columns;
            int width = Margin * 2 + CellWidth * Columns;
            int height = Margin * 2 + CellHeight * rows;

            var labelBrush = new SolidColorBrush(Color.FromRgb(0x96, 0x96, 0x96));
            labelBrush.Freeze();
            var labelFace = new Typeface("Consolas");

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));

                for (int i = 0; i < Keys.Length; i++)
                {
                    string key = Keys[i];
                    int column = i % Columns;
                    int row = i / Columns;
                    double left = Margin + column * CellWidth;
                    double top = Margin + row * CellHeight;

                    // 2x supersampling keeps the dots crisp in the review sheet;
                    // the icon still occupies 64 DIP on the stage.
                    var icon = DotMatrixIcons.Render(key, IconSize, Colors.White, 2.0);
                    if (icon is not null)
                    {
                        dc.DrawImage(icon, new Rect(left, top, IconSize, IconSize));
                    }

                    var label = new FormattedText(
                        key,
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        labelFace,
                        11,
                        labelBrush,
                        1.0);

                    dc.DrawText(label, new Point(
                        left + (IconSize - label.Width) / 2,
                        top + IconSize + 6));
                }
            }

            var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
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
}
