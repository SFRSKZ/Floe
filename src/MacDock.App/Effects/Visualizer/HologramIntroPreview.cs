using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MacDock.App.Windows;

namespace MacDock.App.Effects.Visualizer;

/// <summary>
/// Offscreen render hook for the "now playing" hologram intro, used by the diagnostic
/// <c>--render-intro=&lt;png&gt;</c> switch. Composes four moments of the timeline side by
/// side over a dark stage with a fake dock rim, rendered at twice the overlay's size so
/// the chamber, ribbons and hologram scanlines are inspectable.
/// </summary>
public static class HologramIntroPreview
{
    private const string Option = "--render-intro=";

    private const int CellWidth = 640;
    private const int CellHeight = 392;
    private const int Width = CellWidth * 4;
    private const int Height = CellHeight;
    private const double ViewInset = 8;
    private const double Scale = (CellWidth - (ViewInset * 2)) / HologramIntroWindow.OverlayWidth;

    public static bool TryRender(string[] args)
    {
        var argument = args.FirstOrDefault(a => a.StartsWith(Option, StringComparison.OrdinalIgnoreCase));
        if (argument is null)
        {
            return false;
        }

        string? path = null;
        try
        {
            path = Path.GetFullPath(argument[Option.Length..]);
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            var stage = new Canvas
            {
                Width = Width,
                Height = Height,
                Background = new SolidColorBrush(Color.FromRgb(0x07, 0x0B, 0x12)),
            };

            var art = CreateTestArt();
            double[] times =
            {
                0.40,
                0.80,
                1.10,
                HologramIntroWindow.ImpactTime + 0.20,
            };

            double rimInCell = CellHeight - 88;
            double viewTop = rimInCell - (HologramIntroWindow.RimY * Scale);
            double viewHeight = HologramIntroWindow.OverlayHeight * Scale;

            for (int i = 0; i < times.Length; i++)
            {
                var cell = new Canvas { Width = CellWidth, Height = CellHeight };
                Canvas.SetLeft(cell, i * CellWidth);
                stage.Children.Add(cell);

                // Fake dock rim line for the projector to sink into.
                var rim = new Border
                {
                    Width = CellWidth - 84,
                    Height = 32,
                    CornerRadius = new CornerRadius(16),
                    Background = new LinearGradientBrush(
                        new GradientStopCollection
                        {
                            new(Color.FromArgb(0x2E, 0xDF, 0xF2, 0xFF), 0),
                            new(Color.FromArgb(0x5A, 0x9F, 0xD4, 0xFF), 0.35),
                            new(Color.FromArgb(0x3A, 0xFF, 0xFF, 0xFF), 0.5),
                            new(Color.FromArgb(0x5A, 0x9F, 0xD4, 0xFF), 0.65),
                            new(Color.FromArgb(0x2E, 0xDF, 0xF2, 0xFF), 1),
                        }, new Point(0, 0.5), new Point(1, 0.5)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xE8, 0xF8, 0xFF)),
                    BorderThickness = new Thickness(1.6),
                };
                Canvas.SetLeft(rim, (CellWidth - rim.Width) / 2);
                Canvas.SetTop(rim, rimInCell - (rim.Height / 2));
                cell.Children.Add(rim);

                var visual = new HologramIntroVisual
                {
                    Width = HologramIntroWindow.OverlayWidth,
                    Height = HologramIntroWindow.OverlayHeight,
                };
                visual.Reset(HologramIntroWindow.OverlayWidth / 2, art);
                visual.SetTimeForPreview(times[i]);

                var view = new Viewbox
                {
                    Width = CellWidth - (ViewInset * 2),
                    Height = viewHeight,
                    Stretch = Stretch.Fill,
                    Child = visual,
                };
                Canvas.SetLeft(view, ViewInset);
                Canvas.SetTop(view, viewTop);
                cell.Children.Add(view);

                var label = new TextBlock
                {
                    Text = $"t = {times[i]:0.00} s",
                    Foreground = new SolidColorBrush(Color.FromArgb(0x8A, 0xD8, 0xF0, 0xFF)),
                    FontSize = 20,
                };
                Canvas.SetLeft(label, ViewInset + 8);
                Canvas.SetTop(label, CellHeight - 30);
                cell.Children.Add(label);

                if (i > 0)
                {
                    var divider = new System.Windows.Shapes.Rectangle
                    {
                        Width = 1.5,
                        Height = CellHeight,
                        Fill = new SolidColorBrush(Color.FromArgb(0x14, 0x9F, 0xD4, 0xFF)),
                    };
                    Canvas.SetLeft(divider, (i * CellWidth) - 0.5);
                    stage.Children.Add(divider);
                }
            }

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

    /// <summary>A colourful gradient square standing in for the album art.</summary>
    private static BitmapSource CreateTestArt()
    {
        const int size = 192;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var gradient = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(0xFF, 0x5B, 0x7A), 0),
                    new(Color.FromRgb(0xFF, 0xC2, 0x57), 0.24),
                    new(Color.FromRgb(0x5B, 0xE3, 0xC0), 0.48),
                    new(Color.FromRgb(0x4A, 0x8C, 0xFF), 0.72),
                    new(Color.FromRgb(0x9A, 0x5B, 0xFF), 1),
                }, 35);
            dc.DrawRectangle(gradient, null, new Rect(0, 0, size, size));

            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)), null,
                new Point(size * 0.30, size * 0.34), size * 0.16, size * 0.16);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x3C, 0x0B, 0x1B, 0x33)), null,
                new Point(size * 0.74, size * 0.70), size * 0.40, size * 0.26);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)), null,
                new Rect(size * 0.10, size * 0.78, size * 0.80, size * 0.06));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
