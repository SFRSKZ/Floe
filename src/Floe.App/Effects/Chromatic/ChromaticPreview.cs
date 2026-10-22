using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Floe.App.Effects.MetalRim;

namespace Floe.App.Effects.Chromatic;

/// <summary>Hidden chromatic material diagnostic; the real ps_2_0 surface through WPF's software renderer.</summary>
internal static class ChromaticPreview
{
    private static readonly Color Glass = Color.FromRgb(0x22, 0x26, 0x2C);
    private static readonly Color Card = Color.FromRgb(0xF4, 0xF4, 0xF6);
    private static readonly Color Text = Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF);
    private static readonly Color Ink = Color.FromArgb(0xCC, 0x20, 0x24, 0x2A);
    private static readonly Color Tint = Color.FromRgb(0x5B, 0xC8, 0xFF);

    internal static bool TryRender(string[] args)
    {
        const string option = "--renderchromatic=";
        var argument = args.FirstOrDefault(a => a.StartsWith(option, StringComparison.OrdinalIgnoreCase));
        if (argument is null)
        {
            return false;
        }

        var folder = Path.GetFullPath(argument[option.Length..]);
        try
        {
            Directory.CreateDirectory(folder);
            if (!ChromaticEffect.IsAvailable)
            {
                throw new InvalidOperationException("Chromatic ps_2_0 compilation failed; see floe.log.");
            }

            Save(Scale(ChromaticField.Texture, 4), Path.Combine(folder, "field.png"));
            Save(Scale(ChromaticNoise.Texture, 4), Path.Combine(folder, "grain.png"));
            Save(Sheet(), Path.Combine(folder, "chromatic-sheet.png"));
            Save(Zoom(), Path.Combine(folder, "chromatic-zoom.png"));
            File.WriteAllText(Path.Combine(folder, "render-info.txt"),
                "ps_2_0 compiled; WPF software rasterizer (RenderTargetBitmap). Rows t=0,1.2,2.4,3.6 s; " +
                "left rest, right hover; plain glass card for comparison. Tint #5BC8FF, speed .7. " +
                "chromatic-zoom.png = 4x nearest of one face on a light card.\n");
        }
        catch (Exception ex)
        {
            ShaderCompiler.Log($"Chromatic preview failed: {ex}");
            try { File.WriteAllText(Path.Combine(folder, "render-error.txt"), ex.ToString()); } catch { }
            Environment.ExitCode = 1;
        }
        return true;
    }

    private static BitmapSource Sheet()
    {
        const int width = 760;
        const int height = 440;
        var host = new Canvas { Width = width, Height = height, Background = new SolidColorBrush(Glass) };
        host.Children.Add(new Border
        {
            Width = width,
            Height = 150,
            Background = new SolidColorBrush(Card),
            IsHitTestVisible = false,
        });
        Canvas.SetTop(host.Children[^1], 290);

        var caption = new TextBlock
        {
            Text = "Chromatic Button material — fixed tint #5BC8FF, reflections only",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(0x8A, 0xFF, 0xFF, 0xFF)),
        };
        Canvas.SetLeft(caption, 20);
        Canvas.SetTop(caption, 12);
        host.Children.Add(caption);

        double[] times = [0, 1.2, 2.4, 3.6];
        for (var row = 0; row < times.Length; row++)
        {
            Add(host, Face("İkon efekti", 128, 32, times[row], 0), 20, 44 + (row * 58));
            Add(host, Face("İkon efekti", 128, 32, times[row], 1), 168, 44 + (row * 58));
            Add(host, Face("Kapat", 58, 28, times[row], 0), 316, 46 + (row * 58));
            Add(host, Face("Kapat", 58, 28, times[row], 1), 390, 44 + (row * 58));
            Add(host, Plain("İkon efekti", 128, 32), 470, 44 + (row * 58));
            Add(host, Face("Palet", 86, 32, times[row], 0), 614, 44 + (row * 58));
        }

        var note = new TextBlock
        {
            Text = "left: rest   second: hover lift + brighten   third/fourth: Close button   fifth: today's plain glass   right: short toggle",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
        };
        Canvas.SetLeft(note, 20);
        Canvas.SetTop(note, 272);
        host.Children.Add(note);

        var lightCaption = new TextBlock
        {
            Text = "on a light card, the reference's context — no dark rim, no feather",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Ink),
        };
        Canvas.SetLeft(lightCaption, 20);
        Canvas.SetTop(lightCaption, 304);
        host.Children.Add(lightCaption);

        Add(host, Face("İkon efekti", 128, 32, 2.0, 0), 20, 324);
        Add(host, Face("İkon efekti", 128, 32, 2.0, 1), 168, 322);
        Add(host, Face("Kapat", 58, 28, 2.0, 0), 316, 326);
        Add(host, Face("Palet", 86, 32, 2.0, 0), 390, 324);

        var big = new Grid { Width = 236, Height = 72 };
        big.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Tint),
            Effect = new ChromaticEffect
            {
                Size = new Size(236, 72),
                Time = 2.0,
                Tint = Tint,
            },
        });
        big.Children.Add(Label("İkon efekti", Ink));
        Add(host, big, 500, 314);

        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        return Raster(host, width, height);
    }

    /// <summary>One face at 4x, on the reference's light card, for judging the edge.</summary>
    private static BitmapSource Zoom()
    {
        var host = new Canvas { Width = 180, Height = 64, Background = new SolidColorBrush(Card) };
        Add(host, Face("İkon efekti", 128, 32, 0.8, 0), 26, 16);
        host.Measure(new Size(180, 64));
        host.Arrange(new Rect(0, 0, 180, 64));
        host.UpdateLayout();
        return Scale(Raster(host, 180, 64), 4);
    }

    private static Grid Face(string label, double width, double height, double time, double hover)
    {
        var face = new Border
        {
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Tint),
            Effect = new ChromaticEffect
            {
                Size = new Size(width, height),
                Time = time,
                Hover = hover,
                Tint = Tint,
            },
        };

        var host = new Grid { Width = width, Height = height };
        if (hover > 0.5)
        {
            // same 1.5 DIP lift as the settings window triggers
            host.RenderTransform = new TranslateTransform(0, -1.5);
        }
        host.Children.Add(face);
        host.Children.Add(Label(label, Text));
        return host;
    }

    private static TextBlock Label(string label, Color foreground) => new()
    {
        Text = label,
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = 13,
        Foreground = new SolidColorBrush(foreground),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Effect = new DropShadowEffect
        {
            Color = Color.FromArgb(0xFF, 0x0C, 0x24, 0x30),
            BlurRadius = 1,
            ShadowDepth = 1,
            Direction = 270,
            Opacity = 0.4,
        },
    };

    private static Grid Plain(string label, double width, double height)
    {
        var host = new Grid { Width = width, Height = height };
        host.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
        });
        host.Children.Add(Label(label, Text));
        return host;
    }

    private static void Add(Canvas host, UIElement element, double left, double top)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        host.Children.Add(element);
    }

    private static BitmapSource Scale(BitmapSource source, int factor)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(source, new Rect(0, 0, source.PixelWidth * factor, source.PixelHeight * factor));
        }
        return Raster(visual, source.PixelWidth * factor, source.PixelHeight * factor);
    }

    private static BitmapSource Raster(Visual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }
}
