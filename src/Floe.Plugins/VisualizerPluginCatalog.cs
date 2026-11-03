using System.Text.Json;
using Floe.Core.Services;

namespace Floe.Plugins;

/// <summary>One folder that claimed to be a plugin but failed validation.</summary>
public sealed record PluginRejection(string Folder, IReadOnlyList<string> Problems);

/// <summary>
/// Finds visualizer plugins under the two roots Floe supports: the user's plugins folder and the
/// plugins that ship next to the executable. A user plugin with the same id wins over a bundled one.
/// </summary>
public sealed partial class VisualizerPluginCatalog
{
    private readonly List<VisualizerPlugin> _plugins = new();
    private readonly List<PluginRejection> _rejected = new();

    public IReadOnlyList<VisualizerPlugin> Plugins => _plugins;

    public IReadOnlyList<PluginRejection> Rejected => _rejected;

    public VisualizerPlugin? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        foreach (var plugin in _plugins)
        {
            if (string.Equals(plugin.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return plugin;
            }
        }

        return null;
    }

    public static string[] DefaultRoots()
    {
        var user = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Floe",
            "plugins");
        var shipped = Path.Combine(AppContext.BaseDirectory, "plugins");
        return new[] { user, shipped };
    }

    public static VisualizerPluginCatalog Discover(IEnumerable<string>? roots = null)
    {
        var catalog = new VisualizerPluginCatalog();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots ?? DefaultRoots())
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            string[] folders;
            try
            {
                folders = Directory.GetDirectories(root);
            }
            catch (Exception)
            {
                continue;
            }

            Array.Sort(folders, StringComparer.OrdinalIgnoreCase);
            foreach (var folder in folders)
            {
                var name = Path.GetFileName(folder);
                if (name.StartsWith('.') || name.StartsWith('_'))
                {
                    continue;
                }

                var load = VisualizerPluginReader.Load(folder, source: Path.GetFullPath(root));
                if (!load.Ok || load.Plugin is null)
                {
                    catalog._rejected.Add(new PluginRejection(folder, load.Problems));
                    continue;
                }

                if (!seen.Add(load.Plugin.Id))
                {
                    catalog._rejected.Add(new PluginRejection(folder, new[] { $"id '{load.Plugin.Id}' is already provided by another folder" }));
                    continue;
                }

                catalog._plugins.Add(load.Plugin);
            }
        }

        catalog._plugins.Sort((a, b) =>
            string.Compare(a.DisplayName("en"), b.DisplayName("en"), StringComparison.OrdinalIgnoreCase));
        return catalog;
    }
}

/// <summary>
/// The user's plugin choices, kept in <c>%APPDATA%\Floe\plugins.json</c>: which plugin is selected,
/// which ones are disabled, and the option values entered in Settings.
/// </summary>
public sealed class VisualizerPluginSettings
{
    public const string DefaultQuality = "auto";

    public string? SelectedId { get; set; }

    public string Quality { get; set; } = DefaultQuality;

    public List<string> Disabled { get; set; } = new();

    public Dictionary<string, Dictionary<string, JsonElement>> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Floe",
        "plugins.json");

    public bool IsEnabled(string id) => !Disabled.Any(d => string.Equals(d, id, StringComparison.OrdinalIgnoreCase));

    public void SetEnabled(string id, bool enabled)
    {
        Disabled.RemoveAll(d => string.Equals(d, id, StringComparison.OrdinalIgnoreCase));
        if (!enabled)
        {
            Disabled.Add(id);
        }
    }

    public Dictionary<string, JsonElement>? OptionsFor(string id) =>
        Options.TryGetValue(id, out var values) ? values : null;

    public void SetOption(string id, string optionId, JsonElement value)
    {
        var bag = Options.TryGetValue(id, out var existing) ? existing : null;
        if (bag is null)
        {
            bag = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            Options[id] = bag;
        }

        bag[optionId] = value;
    }

    public static VisualizerPluginSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path))
            {
                return new VisualizerPluginSettings();
            }

            var settings = JsonSerializer.Deserialize<VisualizerPluginSettings>(File.ReadAllText(path));
            return settings ?? new VisualizerPluginSettings();
        }
        catch
        {
            return new VisualizerPluginSettings();
        }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(this));
        }
        catch
        {
            // settings are best effort
        }
    }
}

/// <summary>Merges a plugin's declared defaults with the user's stored values into one JSON object.</summary>
public static class PluginOptionsDocument
{
    public static string Merge(VisualizerPluginManifest manifest, IReadOnlyDictionary<string, JsonElement>? stored)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var option in manifest.Options)
            {
                writer.WritePropertyName(option.Id);
                var value = stored is not null && stored.TryGetValue(option.Id, out var saved) ? saved : option.Default;
                if (value is { } v && v.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
                {
                    v.WriteTo(writer);
                }
                else
                {
                    WriteDefault(writer, option);
                }
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteDefault(Utf8JsonWriter writer, PluginOption option)
    {
        switch (option.Type)
        {
            case "bool":
                writer.WriteBooleanValue(false);
                break;
            case "enum":
                writer.WriteStringValue(option.Values.Length > 0 ? option.Values[0] : string.Empty);
                break;
            default:
                writer.WriteNumberValue(option.Min ?? 0);
                break;
        }
    }
}
