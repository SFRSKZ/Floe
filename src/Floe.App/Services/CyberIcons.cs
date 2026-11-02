using System.Windows;
using System.Windows.Media;

namespace Floe.App.Services;

/// <summary>
/// The line icons of the cyber themes' own keys and weather, drawn in one colour. Outlines from Tabler Icons (MIT, tabler.io/icons).
/// </summary>
public static class CyberIcons
{
    private const double Box = 24;
    private const double Stroke = 1.8;

    private const string Cloud = "M6 14h12l3 -3v-2l-3 -3h-2l-3 -3h-3l-3 3v2h-1l-3 3v1z";

    private static readonly Dictionary<string, string[]> Paths = new()
    {
        // the cyber themes' own hard edged media keys and weather
        ["floe-play"] = new[] { "M7 4v16l13 -8z" },
        ["floe-pause"] = new[] { "M7 4h3v16h-3z", "M14 4h3v16h-3z" },
        ["floe-previous"] = new[] { "M19 5v14l-10 -7z", "M6 5v14" },
        ["floe-next"] = new[] { "M5 5v14l10 -7z", "M18 5v14" },
        ["wx-sunny"] = new[] { "M9 7h6l2 2v6l-2 2h-6l-2 -2v-6z", "M12 2v2", "M12 20v2", "M2 12h2", "M20 12h2", "M5 5l1.5 1.5", "M19 5l-1.5 1.5", "M5 19l1.5 -1.5", "M19 19l-1.5 -1.5" },
        ["wx-partly"] = new[] { "M8 21h9l4 -4v-2l-2 -2h-2l-3 -3h-2l-3 3v1l-3 3v1z", "M4 10v-3l2 -2h3l2 2", "M7.5 1v1.5", "M1 7h1.5", "M2.5 2.5l1 1", "M12.5 2.5l-1 1" },
        ["wx-cloudy"] = new[] { Cloud.Replace("M6 14", "M6 17") },
        ["wx-overcast"] = new[] { Cloud, "M5 18h14", "M8 21h8" },
        ["wx-fog"] = new[] { "M6 6h12", "M3 11h18", "M6 16h12", "M9 21h6" },
        ["wx-light_rain"] = new[] { Cloud, "M9 17l-1 4", "M15 17l-1 4" },
        ["wx-rain"] = new[] { Cloud, "M7 17l-1 4", "M12 17l-1 4", "M17 17l-1 4" },
        ["wx-heavy_rain"] = new[] { Cloud, "M6 17l-1 4", "M10 17l-1 4", "M14 17l-1 4", "M18 17l-1 4" },
        ["wx-sleet"] = new[] { Cloud, "M8 17l-1 4", "M14.5 17.5l3 3", "M17.5 17.5l-3 3" },
        ["wx-light_snow"] = new[] { Cloud, "M12 17v4", "M10 19h4" },
        ["wx-snow"] = new[] { Cloud, "M8 17v4", "M6 19h4", "M16 17v4", "M14 19h4" },
        ["wx-heavy_snow"] = new[] { Cloud, "M6 17v4", "M4 19h4", "M12 17v4", "M10 19h4", "M18 17v4", "M16 19h4" },
        ["wx-thunderstorm"] = new[] { Cloud, "M13 13l-3 4h4l-3 5" },

        ["adjustments-horizontal"] = new[] { "M14 6m-2 0a2 2 0 1 0 4 0a2 2 0 1 0 -4 0", "M4 6l8 0", "M16 6l4 0", "M8 12m-2 0a2 2 0 1 0 4 0a2 2 0 1 0 -4 0", "M4 12l2 0", "M10 12l10 0", "M17 18m-2 0a2 2 0 1 0 4 0a2 2 0 1 0 -4 0", "M4 18l11 0", "M19 18l1 0" },
        ["chevron-up"] = new[] { "M6 15l6 -6l6 6" },
    };

    /// <summary>One of the icons by its Tabler name.</summary>
    public static ImageSource Draw(string icon, Brush brush)
    {
        var group = Canvas(brush);
        foreach (var path in Paths[icon]) group.Children.Add(Line(Geometry.Parse(path), brush));
        return Finish(group);
    }

    /// <summary>The weather by the condition <see cref="WeatherService"/> gives.</summary>
    public static ImageSource Weather(string condition, Brush brush) => condition switch
    {
        "mostly_sunny" or "partly_cloudy" => Draw("wx-partly", brush),
        _ => Draw(Paths.ContainsKey($"wx-{condition}") ? $"wx-{condition}" : "wx-cloudy", brush),
    };

    // an empty box first, so every icon keeps the same 24 unit frame whatever its own bounds
    private static DrawingGroup Canvas(Brush brush)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, Box, Box))));
        return group;
    }

    private static GeometryDrawing Line(Geometry geometry, Brush brush) =>
        new(null, new Pen(brush, Stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, geometry);

    private static ImageSource Finish(DrawingGroup group) => new DrawingImage(group);
}
