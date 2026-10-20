using System.Text.Json;
using System.Windows;

namespace MacDock.App.Services;

public static class WidgetLayout
{
    private const string VisibilityKey = "widgets";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static Dictionary<string, Position>? _cache;

    private static string Path
        => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MacDock", "widgets.json");

    public static (double Left, double Top)? Get(string id, double width, double height)
    {
        var map = Load();
        if (map.TryGetValue(id, out var position))
        {
            return Clamp(position.Left, position.Top, width, height);
        }

        return null;
    }

    public static void Set(string id, double left, double top, double width, double height)
    {
        var map = Load();
        var (clampedLeft, clampedTop) = Clamp(left, top, width, height);
        map[id] = new Position { Left = clampedLeft, Top = clampedTop };
        Save(map);
    }

    public static bool GetVisible()
    {
        var map = Load();
        return !map.TryGetValue(VisibilityKey, out var entry) || entry.Visible;
    }

    public static void SetVisible(bool visible)
    {
        var map = Load();
        if (!map.TryGetValue(VisibilityKey, out var entry))
        {
            entry = new Position();
            map[VisibilityKey] = entry;
        }

        entry.Visible = visible;
        Save(map);
    }

    private static (double Left, double Top) Clamp(double left, double top, double width, double height)
    {
        var screenLeft = SystemParameters.VirtualScreenLeft;
        var screenTop = SystemParameters.VirtualScreenTop;
        var screenRight = screenLeft + SystemParameters.VirtualScreenWidth;
        var screenBottom = screenTop + SystemParameters.VirtualScreenHeight;

        // keep the whole widget on the virtual screen (at least a margin when the size is unknown)
        const double margin = 24;
        var minLeft = screenLeft + margin;
        var maxLeft = Math.Max(minLeft, screenRight - margin - Math.Max(0, width));
        var minTop = screenTop + margin;
        var maxTop = Math.Max(minTop, screenBottom - margin - Math.Max(0, height));

        return (Math.Clamp(left, minLeft, maxLeft), Math.Clamp(top, minTop, maxTop));
    }

    private static void Save(Dictionary<string, Position> map)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            MacDock.Core.Services.AtomicFile.WriteAllText(Path, JsonSerializer.Serialize(map, Options));
        }
        catch
        {
            // ignore
        }
    }

    private static Dictionary<string, Position> Load()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        try
        {
            if (System.IO.File.Exists(Path))
            {
                _cache = JsonSerializer.Deserialize<Dictionary<string, Position>>(System.IO.File.ReadAllText(Path), Options)
                    ?? new Dictionary<string, Position>();
            }
            else
            {
                _cache = new Dictionary<string, Position>();
            }
        }
        catch
        {
            _cache = new Dictionary<string, Position>();
        }

        return _cache;
    }

    private sealed class Position
    {
        public double Left { get; set; }

        public double Top { get; set; }

        public bool Visible { get; set; } = true;
    }
}
