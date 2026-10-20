using System;
using System.IO;
using System.Text.Json;

namespace MacDock.App.Services;

public enum RimStyle
{
    Plain,
    MetalSilver,
    MetalGold,
    MetalChromatic,
}

/// <summary>Dock glass look: rim style and the music visualizer glow.</summary>
public static class GlassSettings
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MacDock",
        "glass.json");

    public static event Action? Changed;

    public static RimStyle Rim { get; private set; } = RimStyle.Plain;

    public static bool Visualizer { get; private set; } = true;

    public static void Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return;
            }

            var model = JsonSerializer.Deserialize<Model>(File.ReadAllText(ConfigPath));
            if (model is null)
            {
                return;
            }

            Rim = Enum.TryParse<RimStyle>(model.Rim, true, out var rim) ? rim : RimStyle.Plain;
            Visualizer = model.Visualizer;
        }
        catch
        {
            // fall back to defaults
        }
    }

    public static void SetRim(RimStyle rim)
    {
        if (Rim == rim)
        {
            return;
        }

        Rim = rim;
        Save();
        Changed?.Invoke();
    }

    public static void SetVisualizer(bool enabled)
    {
        if (Visualizer == enabled)
        {
            return;
        }

        Visualizer = enabled;
        Save();
        Changed?.Invoke();
    }

    private static void Save()
    {
        try
        {
            MacDock.Core.Services.AtomicFile.WriteAllText(ConfigPath, JsonSerializer.Serialize(new Model
            {
                Rim = Rim.ToString(),
                Visualizer = Visualizer,
            }));
        }
        catch
        {
            // settings are best effort
        }
    }

    private sealed class Model
    {
        public string Rim { get; set; } = nameof(RimStyle.Plain);

        public bool Visualizer { get; set; } = true;
    }
}
