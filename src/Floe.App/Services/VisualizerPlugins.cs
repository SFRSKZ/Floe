using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Floe.App.Effects.Visualizer;
using Floe.Plugins;

namespace Floe.App.Services;

/// <summary>
/// The bridge between Floe's settings and the visualizer plugins: where they live, which one is
/// selected, what the user chose for its options, and the colours they inherit from the dock.
/// </summary>
internal static class VisualizerPlugins
{
    private static VisualizerPluginCatalog? _catalog;
    private static VisualizerPluginSettings? _settings;

    public static VisualizerPluginSettings Settings => _settings ??= VisualizerPluginSettings.Load();

    public static VisualizerPluginCatalog Catalog => _catalog ??= VisualizerPluginCatalog.Discover();

    /// <summary>The folder a user drops plugins into.</summary>
    public static string UserFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Floe",
        "plugins");

    public static void Reload()
    {
        _settings = VisualizerPluginSettings.Load();
        _catalog = VisualizerPluginCatalog.Discover();
    }

    public static VisualizerPlugin? Selected
    {
        get
        {
            var plugin = Catalog.Find(GlassSettings.PluginId);
            if (plugin is not null && Settings.IsEnabled(plugin.Id))
            {
                return plugin;
            }

            // the saved id may belong to a plugin that was removed or disabled: use the first one
            foreach (var candidate in Catalog.Plugins)
            {
                if (Settings.IsEnabled(candidate.Id))
                {
                    return candidate;
                }
            }

            return null;
        }
    }

    public static IReadOnlyList<VisualizerPlugin> Enabled
    {
        get
        {
            var list = new List<VisualizerPlugin>();
            foreach (var plugin in Catalog.Plugins)
            {
                if (Settings.IsEnabled(plugin.Id))
                {
                    list.Add(plugin);
                }
            }

            return list;
        }
    }

    public static string OptionsJson(VisualizerPlugin plugin) =>
        PluginOptionsDocument.Merge(plugin.Manifest, Settings.OptionsFor(plugin.Id));

    public static void Select(string id)
    {
        Settings.SelectedId = id;
        Settings.Save();
        GlassSettings.SetPlugin(id);
    }

    public static void SetEnabled(string id, bool enabled)
    {
        Settings.SetEnabled(id, enabled);
        Settings.Save();
    }

    /// <summary>The plugin's four colours, taken from the same palette Floe's dust visualizers use.</summary>
    public static (string Core, string Second, string Accent, string Deep) Palette(double time, Color basePrimary)
    {
        Span<float> red = stackalloc float[16];
        Span<float> green = stackalloc float[16];
        Span<float> blue = stackalloc float[16];
        VisualizerPalette.Fill(red, green, blue, time, basePrimary);

        return (Hex(red[7], green[7], blue[7]),
                Hex(red[11], green[11], blue[11]),
                Hex(red[15], green[15], blue[15]),
                Hex(red[7] * 0.22f + 0.01f, green[7] * 0.24f + 0.02f, blue[7] * 0.30f + 0.03f));
    }

    private static string Hex(float r, float g, float b) =>
        $"#{(byte)(Math.Clamp(r, 0, 1) * 255):X2}{(byte)(Math.Clamp(g, 0, 1) * 255):X2}{(byte)(Math.Clamp(b, 0, 1) * 255):X2}";

    public static string DisplayName(VisualizerPlugin plugin) =>
        plugin.DisplayName(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

    public static void OpenUserFolder()
    {
        try
        {
            Directory.CreateDirectory(UserFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", UserFolder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Floe.Plugin] open folder: {ex.Message}");
        }
    }

    public static JsonElement ReadOption(VisualizerPlugin plugin, string optionId)
    {
        if (Settings.OptionsFor(plugin.Id) is { } values && values.TryGetValue(optionId, out var value))
        {
            return value;
        }

        foreach (var option in plugin.Manifest.Options)
        {
            if (option.Id == optionId)
            {
                return option.Default ?? default;
            }
        }

        return default;
    }
}
