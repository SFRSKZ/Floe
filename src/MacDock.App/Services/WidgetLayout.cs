using System.Text.Json;

namespace MacDock.App.Services;

public static class WidgetLayout
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static Dictionary<string, Position>? _cache;

    private static string Path
        => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MacDock", "widgets.json");

    public static (double Left, double Top)? Get(string id)
    {
        var map = Load();
        if (map.TryGetValue(id, out var position))
        {
            return (position.Left, position.Top);
        }

        return null;
    }

    public static void Set(string id, double left, double top)
    {
        var map = Load();
        map[id] = new Position { Left = left, Top = top };

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
    }
}
