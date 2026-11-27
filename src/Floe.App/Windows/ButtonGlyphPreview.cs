using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floe.App.Services;

namespace Floe.App.Windows;

/// <summary>
/// --render-glyphs=&lt;png&gt;: the cyber start and quick-settings glyphs through their hover and press states (rest,
/// hover .25 / .5 / 1, pressed), at the dock's size and at the taskbar's (drawn under the taskbar root's 0.706), each
/// on a row of backgrounds (the theme's red, black, white, the visualizer's light, a dark blue). A second sheet next
/// to it repeats that with a light ink. With --render-glyphs-frames=&lt;dir&gt; it also writes the frames of one
/// hover, press, release and leave as numbered PNGs, for a GIF. The glyphs sit in a real (offscreen) window, so they
/// snap to the screen's own pixels as they do in the dock.
/// </summary>
internal static class ButtonGlyphPreview
{
    private const double TaskbarScale = 0.706;

    private static readonly Color[] Grounds =
    {
        Color.FromRgb(0xE8, 0x10, 0x48), Color.FromRgb(0x06, 0x06, 0x0A), Color.FromRgb(0xF2, 0xF2, 0xF2),
        Color.FromRgb(0xFF, 0x70, 0x70), Color.FromRgb(0x10, 0x1C, 0x3A),
    };

    private static readonly Color Accent = Color.FromRgb(0x00, 0xF0, 0xFF);

    public static bool TryRender(string[] args)
    {
        var file = Arg(args, "--render-glyphs=");
        if (file is null) return false;
        var framesDir = Arg(args, "--render-glyphs-frames=");
        // each sheet opens and closes its own window; closing one must not end the app before the next
        if (Application.Current is { } app) app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var dark = Color.FromRgb(0x0A, 0x0A, 0x10);
        var light = Color.FromRgb(0xF4, 0xF4, 0xF0);
        (double Hover, double Press, string Name)[] states = { (0, 0, "rest"), (.25, 0, ".25"), (.5, 0, ".5"), (1, 0, "1"), (1, 1, "press") };

        var full = Path.GetFullPath(file);
        Save(Sheet(states, dark), full);
        Save(Sheet(states, light), Path.Combine(Path.GetDirectoryName(full)!, Path.GetFileNameWithoutExtension(full) + "-light.png"));
        if (framesDir is not null) Frames(Path.GetFullPath(framesDir), dark);
        return true;
    }

    private static string? Arg(string[] args, string prefix) =>
        args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

    /// <summary>Rows: each state at the dock's size and then the taskbar's. Columns: each ground, start then settings.</summary>
    private static Canvas Sheet((double Hover, double Press, string Name)[] states, Color ink)
    {
        const double cell = 48, label = 44;
        var grid = new Canvas { Width = label + (cell * Grounds.Length * 2), Height = cell * states.Length * 2, Background = Brushes.Gray };
        for (int s = 0; s < states.Length; s++)
        {
            for (int taskbar = 0; taskbar < 2; taskbar++)
            {
                double y = ((s * 2) + taskbar) * cell;
                var text = new TextBlock
                {
                    Text = states[s].Name + (taskbar == 1 ? " tb" : " dk"),
                    Foreground = Brushes.White,
                    FontSize = 11,
                    Width = label,
                };
                Canvas.SetLeft(text, 4);
                Canvas.SetTop(text, y + 16);
                grid.Children.Add(text);
                for (int col = 0; col < Grounds.Length; col++)
                {
                    for (int kind = 0; kind < 2; kind++)
                    {
                        double x = label + (((col * 2) + kind) * cell);
                        grid.Children.Add(Cell(x, y, cell, Grounds[col], kind, taskbar == 1, ink, states[s].Hover, states[s].Press));
                    }
                }
            }
        }

        return grid;
    }

    private static FrameworkElement Cell(double x, double y, double cell, Color ground, int kind, bool taskbar, Color ink, double hover, double press)
    {
        var back = new Canvas { Width = cell, Height = cell, Background = new SolidColorBrush(ground), ClipToBounds = true };
        Canvas.SetLeft(back, x);
        Canvas.SetTop(back, y);

        double size = taskbar ? 40 : 34;
        FrameworkElement glyph;
        var accent = CyberLook.Readable(Accent, Grounds[0]);
        if (kind == 0)
        {
            var start = new CyberStartGlyph(size) { Hover = hover, Press = press };
            start.Paint(ink, accent);
            glyph = start;
        }
        else
        {
            // the settings glyph animates on its own (Engage), so the sheet shows it at rest or engaged
            var settings = new CyberSettingsGlyph(size);
            settings.Paint(ink, accent);
            if (hover >= .5) settings.Engage(true);
            glyph = settings;
        }

        // the taskbar draws its root at about 0.7, so its 40 unit glyph comes out near 28 DIP on screen
        double shown = taskbar ? size * TaskbarScale : size;
        var host = new Canvas { Width = size, Height = size };
        if (taskbar) host.RenderTransform = new ScaleTransform(TaskbarScale, TaskbarScale);
        host.Children.Add(glyph);
        Canvas.SetLeft(host, Math.Round((cell - shown) / 2));
        Canvas.SetTop(host, Math.Round((cell - shown) / 2));
        back.Children.Add(host);
        return back;
    }

    /// <summary>One pass through the glyphs' life at 30 fps: rest, hover in, hold, press, release, hover out.</summary>
    private static void Frames(string dir, Color ink)
    {
        Directory.CreateDirectory(dir);

        var timeline = new List<(double Hover, double Press)>();
        void Hold(double h, double p, int n) { for (int i = 0; i < n; i++) timeline.Add((h, p)); }
        void Ramp(bool hover, double from, double to, int ms, double fixedOther)
        {
            int n = Math.Max(1, (int)Math.Round(ms / 33.3));
            for (int i = 1; i <= n; i++)
            {
                double t = (double)i / n;
                double v = from + ((to - from) * (1 - ((1 - t) * (1 - t)))); // the glyphs' quadratic ease out
                timeline.Add(hover ? (v, fixedOther) : (fixedOther, v));
            }
        }

        Hold(0, 0, 10);
        Ramp(true, 0, 1, 380, 0);
        Hold(1, 0, 14);
        Ramp(false, 0, 1, 70, 1);
        Hold(1, 1, 10);
        Ramp(false, 1, 0, 160, 1);
        Hold(1, 0, 12);
        Ramp(true, 1, 0, 260, 0);
        Hold(0, 0, 10);

        Color[] grounds = { Grounds[0], Grounds[1], Grounds[2], Grounds[4] };
        const double cell = 48;
        for (int f = 0; f < timeline.Count; f++)
        {
            var (h, p) = timeline[f];
            var strip = new Canvas { Width = cell * grounds.Length * 2, Height = cell * 2 };
            for (int row = 0; row < 2; row++)
            {
                for (int col = 0; col < grounds.Length; col++)
                {
                    for (int kind = 0; kind < 2; kind++)
                    {
                        strip.Children.Add(Cell(((col * 2) + kind) * cell, row * cell, cell, grounds[col], kind, row == 1, ink, h, p));
                    }
                }
            }

            Save(strip, Path.Combine(dir, $"f{f:000}.png"));
        }
    }

    /// <summary>Shows the visual in an offscreen window at the screen's own scale and saves it pixel for pixel.</summary>
    private static void Save(FrameworkElement content, string file)
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = false,
            ShowInTaskbar = false,
            ShowActivated = false,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            Left = -20000,
            Top = 0,
            Content = content,
        };
        window.Show();
        content.UpdateLayout();
        // a second pass, so the glyphs that learned their pixel origin on the first one draw from it
        content.InvalidateVisual();
        content.UpdateLayout();
        foreach (var glyph in Descendants(content).OfType<HairlineGlyph>()) glyph.InvalidateVisual();
        content.UpdateLayout();

        double scale = VisualTreeHelper.GetDpi(content).DpiScaleX;
        var bitmap = new RenderTargetBitmap((int)Math.Round(content.ActualWidth * scale), (int)Math.Round(content.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(content);
        window.Close();

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }
}
