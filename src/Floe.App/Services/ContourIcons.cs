using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Floe.App.Services;

/// <summary>
/// The outline icon set of the cyber themes: black strokes on clear, used as a mask so the dock can paint them in any
/// colour. An app is matched by its name or process; one the set does not know keeps its own icon.
/// </summary>
public static class ContourIcons
{
    private const int Pixels = 192;
    private const int LooseMatch = 5;
    private static readonly string Folder = $"pack://application:,,,/{typeof(ContourIcons).Assembly.GetName().Name};component/Assets/ContourIcons/";

    // what Windows and the running apps call themselves where the set was named after the Start menu
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["fileexplorer"] = "dosya-gezgini",
        ["explorer"] = "dosya-gezgini",
        ["code"] = "visual-studio-code",
        ["chrome"] = "google-chrome",
        ["msedge"] = "microsoft-edge",
        ["windowsterminal"] = "terminal",
        ["cmd"] = "komut-istemi",
        ["commandprompt"] = "komut-istemi",
        ["settings"] = "ayarlar",
        ["taskmanager"] = "gorev-yoneticisi",
        ["photos"] = "fotograflar",
        ["camera"] = "kamera",
        ["snippingtool"] = "ekran-alintisi-araci",
    };

    private static readonly Lazy<Dictionary<string, string>> Catalog = new(ReadCatalog);
    private static readonly Lazy<(string Name, string File)[]> Names = new(ReadNames);
    private static readonly Dictionary<string, ImageBrush?> Masks = new();
    private static readonly Dictionary<string, ImageBrush> Loaded = new();

    /// <summary>The whole set, in catalog order.</summary>
    public static IReadOnlyList<(string Name, string File)> All => Names.Value;

    /// <summary>The icon as a mask to lay over a fill, or nothing when the set has no icon for the app.</summary>
    public static ImageBrush? Mask(string name, string? process = null)
    {
        var key = $"{name}|{process}";
        if (Masks.TryGetValue(key, out var known)) return known;
        var file = Find(Plain(name)) ?? Find(Plain(process ?? string.Empty));
        return Masks[key] = file is null ? null : Load(file);
    }

    private static string? Find(string plain)
    {
        if (plain.Length == 0) return null;
        if (Aliases.TryGetValue(plain, out var alias) || Catalog.Value.TryGetValue(plain, out alias)) return alias;
        if (plain.Length < LooseMatch) return null;

        // "Blender" finds "Blender 5.0" and "OrcaSlicer 2.3" finds "OrcaSlicer"; the longest name is the closest
        return Catalog.Value.Where(entry => entry.Key.Length >= LooseMatch && (entry.Key.Contains(plain) || plain.Contains(entry.Key)))
            .OrderByDescending(entry => entry.Key.Length).Select(entry => entry.Value).FirstOrDefault();
    }

    private static string Plain(string name) => new(name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static Dictionary<string, string> ReadCatalog()
    {
        var catalog = new Dictionary<string, string>();
        foreach (var (name, file) in Names.Value) catalog[Plain(name)] = file;
        return catalog;
    }

    private static (string Name, string File)[] ReadNames()
    {
        var names = new List<(string, string)>();
        var stream = Application.GetResourceStream(new Uri($"{Folder}names.csv"))?.Stream;
        if (stream is null) return Array.Empty<(string, string)>();
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split(';');
            if (parts.Length == 2) names.Add((parts[0], parts[1]));
        }

        return names.ToArray();
    }

    /// <summary>One icon of the set by its file name, as a mask.</summary>
    public static ImageBrush Load(string file)
    {
        if (Loaded.TryGetValue(file, out var known)) return known;
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri($"{Folder}{file}.png");
        image.DecodePixelWidth = Pixels;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        var brush = new ImageBrush(image) { Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
        brush.Freeze();
        return Loaded[file] = brush;
    }
}
