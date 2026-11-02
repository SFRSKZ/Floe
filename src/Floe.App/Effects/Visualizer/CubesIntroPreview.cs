using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floe.App.Services;
using Floe.App.Windows;

namespace Floe.App.Effects.Visualizer;

/// <summary>
/// Offscreen render hook for the "now playing" cubes intro, used by the diagnostic
/// <c>--render-cubes-intro=&lt;png&gt;</c> switch: a contact sheet of eight moments (six of the intro, two of
/// the scatter) over a dark stage with a fake dock bar. <c>--render-cubes-frames=&lt;folder&gt;</c> writes every
/// frame at 30 fps instead, intro then outro, for stitching into an animation.
/// </summary>
public static class CubesIntroPreview
{
    private const string SheetOption = "--render-cubes-intro=";
    private const string FramesOption = "--render-cubes-frames=";
    // with --dust the star dust version plays instead of the glass cubes
    private static bool _dust;

    private const double Scale = 1.25;
    private const int Columns = 4;
    private static readonly int CellWidth = (int)(CubesIntroWindow.OverlayWidth * Scale);
    private static readonly int CellHeight = (int)(CubesIntroWindow.OverlayHeight * Scale);

    private static readonly (double Time, bool Outro)[] Moments =
    {
        (0.25, false), (0.55, false), (0.95, false), (1.35, false),
        (1.75, false), (2.50, false), (0.50, true), (1.10, true),
    };

    public static bool TryRender(string[] args)
    {
        var sheet = args.FirstOrDefault(a => a.StartsWith(SheetOption, StringComparison.OrdinalIgnoreCase));
        var frames = args.FirstOrDefault(a => a.StartsWith(FramesOption, StringComparison.OrdinalIgnoreCase));
        if (sheet is null && frames is null)
        {
            return false;
        }

        _dust = args.Contains("--dust", StringComparer.OrdinalIgnoreCase);
        string? path = null;
        try
        {
            var art = CreateTestArt();
            if (sheet is not null)
            {
                path = Path.GetFullPath(sheet[SheetOption.Length..]);
                RenderSheet(path, art);
            }
            else
            {
                path = Path.GetFullPath(frames![FramesOption.Length..]);
                RenderFrames(path, art);
            }
        }
        catch (Exception ex)
        {
            try
            {
                if (path is not null)
                {
                    File.WriteAllText(path.TrimEnd('\\', '/') + ".error.txt", ex.ToString());
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

    private static void RenderSheet(string path, BitmapSource art)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        int rows = (Moments.Length + Columns - 1) / Columns;
        var stage = new Canvas
        {
            Width = CellWidth * Columns,
            Height = CellHeight * rows,
            Background = new SolidColorBrush(Color.FromRgb(0x07, 0x0B, 0x12)),
        };

        for (int i = 0; i < Moments.Length; i++)
        {
            var (time, outro) = Moments[i];
            var cell = BuildCell(art, time, outro, $"{(outro ? "dağılma" : "giriş")}  t = {time:0.00} s");
            Canvas.SetLeft(cell, (i % Columns) * CellWidth);
            Canvas.SetTop(cell, (i / Columns) * CellHeight);
            stage.Children.Add(cell);
        }

        Save(stage, path);
    }

    private static void RenderFrames(string folder, BitmapSource art)
    {
        Directory.CreateDirectory(folder);
        const double fps = 30;
        int n = 0;
        for (double t = 0; t <= CubesIntroVisual.EndTime; t += 1 / fps)
        {
            Save(BuildCell(art, t, false, $"t = {t:0.00} s"), Path.Combine(folder, $"frame_{n++:000}.png"));
        }

        for (int hold = 0; hold < 12; hold++)
        {
            Save(BuildCell(art, CubesIntroVisual.EndTime + 0.1, false, "çalıyor"), Path.Combine(folder, $"frame_{n++:000}.png"));
        }

        for (double t = 0; t <= CubesIntroVisual.OutroSeconds + 0.05; t += 1 / fps)
        {
            Save(BuildCell(art, t, true, $"durdu  t = {t:0.00} s"), Path.Combine(folder, $"frame_{n++:000}.png"));
        }
    }

    private static Canvas BuildCell(BitmapSource art, double time, bool outro, string caption)
    {
        var cell = new Canvas
        {
            Width = CellWidth,
            Height = CellHeight,
            Background = new SolidColorBrush(Color.FromRgb(0x07, 0x0B, 0x12)),
            ClipToBounds = true,
        };

        var visual = new CubesIntroVisual
        {
            Width = CubesIntroWindow.OverlayWidth,
            Height = CubesIntroWindow.OverlayHeight,
        };
        visual.Dust = _dust;
        visual.Reset(CubesIntroWindow.OverlayWidth / 2, art, true);
        double veil = 1;
        if (outro)
        {
            visual.SetTimeForPreview(time, true);
            veil = 0;
        }
        else if (time <= CubesIntroVisual.EndTime)
        {
            visual.SetTimeForPreview(time, false);
            veil = visual.ContentVeil;
        }
        else
        {
            visual.SetTimeForPreview(-1, false);
        }

        // a wallpaper behind, so the glass has something to show through
        var stage = new Canvas
        {
            Width = CubesIntroWindow.OverlayWidth,
            Height = CubesIntroWindow.OverlayHeight,
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(0x1B, 0x2A, 0x4A), 0),
                    new(Color.FromRgb(0x3E, 0x2D, 0x5C), 0.45),
                    new(Color.FromRgb(0x7A, 0x4A, 0x5E), 0.75),
                    new(Color.FromRgb(0x2A, 0x3D, 0x4F), 1),
                }, 20),
        };
        // the dock's own spring, roughly: the halves part as the music starts and close when it stops
        double open = outro ? 1 - Smooth(time / 0.55) : Smooth(time / 0.55);
        AddDock(stage, art, veil, open);
        stage.Children.Add(visual);

        var view = new Viewbox { Width = CellWidth, Height = CellHeight, Stretch = Stretch.Fill, Child = stage };
        cell.Children.Add(view);

        var label = new TextBlock
        {
            Text = caption,
            Foreground = new SolidColorBrush(Color.FromArgb(0x8A, 0xD8, 0xF0, 0xFF)),
            FontSize = 18,
        };
        Canvas.SetLeft(label, 14);
        Canvas.SetTop(label, 10);
        cell.Children.Add(label);
        return cell;
    }

    /// <summary>A stand-in dock bar with the now-playing section (cover strip, title, buttons) at <paramref name="veil"/>.</summary>
    private static void AddDock(Canvas stage, BitmapSource art, double veil, double open)
    {
        const double barHeight = 68;
        double cx = CubesIntroWindow.OverlayWidth / 2;
        double cy = CubesIntroWindow.SlotY;
        var bar = new Border
        {
            Width = 380 + (264 * open),
            Height = barHeight,
            CornerRadius = new CornerRadius(22),
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0xB8, 0xD8, 0xF0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xE8, 0xF8, 0xFF)),
            BorderThickness = new Thickness(1.2),
        };
        Canvas.SetLeft(bar, cx - (bar.Width / 2));
        Canvas.SetTop(bar, cy - (barHeight / 2));
        stage.Children.Add(bar);

        // the dividers either side of the now-playing gap
        foreach (var side in new[] { -1.0, 1.0 })
        {
            var divider = new Border { Width = 1, Height = 34, Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)) };
            Canvas.SetLeft(divider, cx + (side * 132 * open));
            Canvas.SetTop(divider, cy - 17);
            stage.Children.Add(divider);
        }

        if (veil <= 0.001)
        {
            return;
        }

        var section = new Grid { Width = 260, Height = 56, Opacity = veil };
        var cover = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill },
        };
        section.Children.Add(cover);
        section.Children.Add(new Border { CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(0x78, 0, 0, 0)) });

        var text = new StackPanel { Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = "Şarkı adı", Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = "Sanatçı", Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)), FontSize = 10.5 });
        section.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
        foreach (var (key, size) in new[] { ("previous", 20.0), ("pause", 22.0), ("next", 20.0) })
        {
            buttons.Children.Add(new Image
            {
                Width = 30,
                Height = size,
                Stretch = Stretch.None,
                Source = DotMatrixIcons.Render(key, size, Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)),
            });
        }

        section.Children.Add(buttons);
        Canvas.SetLeft(section, cx - 130);
        Canvas.SetTop(section, cy - 28);
        stage.Children.Add(section);
    }

    private static double Smooth(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - (2 * t));
    }

    private static void Save(FrameworkElement element, string path)
    {
        int width = (int)element.Width;
        int height = (int)element.Height;
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>A bold, colourful square standing in for the album art.</summary>
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
                    new(Color.FromRgb(0xFF, 0xC2, 0x57), 0.30),
                    new(Color.FromRgb(0x5B, 0xE3, 0xC0), 0.55),
                    new(Color.FromRgb(0x4A, 0x8C, 0xFF), 0.78),
                    new(Color.FromRgb(0x9A, 0x5B, 0xFF), 1),
                }, 35);
            dc.DrawRectangle(gradient, null, new Rect(0, 0, size, size));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xD8)), null,
                new Point(size * 0.62, size * 0.40), size * 0.20, size * 0.20);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x14, 0x12, 0x2E)), null,
                new Rect(0, size * 0.70, size, size * 0.30));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
