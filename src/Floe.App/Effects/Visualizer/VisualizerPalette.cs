using System.Windows.Media;
using Floe.App.Services;

namespace Floe.App.Effects.Visualizer;

/// <summary>The shared tint/gradient controls for dust and continuous contour visualizers.</summary>
internal static class VisualizerPalette
{
    private static readonly Color[] Sunset = { Rgb(0xFFD27A), Rgb(0xFF8A5C), Rgb(0xF04F82), Rgb(0x8C55E0) };
    private static readonly Color[] Northern = { Rgb(0x7CFFC0), Rgb(0x34D8C8), Rgb(0x4A8CFF), Rgb(0xB070FF) };
    private static readonly Color[] Ocean = { Rgb(0xA8F4FF), Rgb(0x3CC6F2), Rgb(0x2A80E2), Rgb(0x4652D0) };
    private static readonly Color[] Neon = { Rgb(0xFF3DCB), Rgb(0x8C5CFF), Rgb(0x2EE6FF), Rgb(0xB8FF40) };
    private static readonly Color[] Silk = { Rgb(0x51B8AC), Rgb(0x75D9ED), Rgb(0xB7A7ED), Rgb(0xE8D8B9) };

    public static void Fill(Span<float> red, Span<float> green, Span<float> blue, double time, Color basePrimary)
    {
        var kind = GlassSettings.DustColors;
        var primary = HologramVisualizer.Tint(basePrimary);
        Color[]? stops = kind switch
        {
            DustPalette.Sunset => Sunset,
            DustPalette.Aurora => Northern,
            DustPalette.Ocean => Ocean,
            DustPalette.Neon => Neon,
            DustPalette.Silk => Silk,
            DustPalette.Custom => GlassSettings.CustomPalette,
            _ => null,
        };
        for (int i = 0; i < red.Length; i++)
        {
            double t = i / (double)(red.Length - 1);
            double r, g, b;
            if (stops is not null)
            {
                double at = t * (stops.Length - 1);
                int k = Math.Min(stops.Length - 2, (int)at);
                double u = at - k;
                r = (stops[k].R + ((stops[k + 1].R - stops[k].R) * u)) / 255;
                g = (stops[k].G + ((stops[k + 1].G - stops[k].G) * u)) / 255;
                b = (stops[k].B + ((stops[k + 1].B - stops[k].B) * u)) / 255;
            }
            else if (kind == DustPalette.Rainbow)
            {
                (r, g, b) = RotateHue(1, .42, .48, (time * 18) + (t * 300));
            }
            else
            {
                (r, g, b) = RotateHue(primary.R / 255.0, primary.G / 255.0, primary.B / 255.0, 50 * t);
            }

            red[i] = (float)Math.Clamp(r, 0, 1);
            green[i] = (float)Math.Clamp(g, 0, 1);
            blue[i] = (float)Math.Clamp(b, 0, 1);
        }
    }

    private static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static (double R, double G, double B) RotateHue(double r, double g, double b, double degrees)
    {
        double a = degrees * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a), k = 1 / 3.0, q = Math.Sqrt(k);
        double m0 = c + ((1 - c) * k), m1 = (k * (1 - c)) - (q * s), m2 = (k * (1 - c)) + (q * s);
        return ((r * m0) + (g * m1) + (b * m2), (r * m2) + (g * m0) + (b * m1), (r * m1) + (g * m2) + (b * m0));
    }
}
