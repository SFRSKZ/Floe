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

public enum VisualizerStyle
{
    Glow,
    Hologram,
    Mist,
}

public enum BulgeStyle
{
    Soft,
    Pedestal,
    Flat,
}

public enum NowPlayingIntro
{
    Drop,
    Hologram,
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

    public static BulgeStyle Bulge { get; private set; } = BulgeStyle.Soft;

    public static VisualizerStyle VisualizerLook { get; private set; } = VisualizerStyle.Glow;

    public static NowPlayingIntro Intro { get; private set; } = NowPlayingIntro.Drop;

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
            Bulge = Enum.TryParse<BulgeStyle>(model.Bulge, true, out var bulge) ? bulge : BulgeStyle.Soft;
            VisualizerLook = Enum.TryParse<VisualizerStyle>(model.VisualizerLook, true, out var look) ? look : VisualizerStyle.Glow;
            Intro = Enum.TryParse<NowPlayingIntro>(model.Intro, true, out var intro) ? intro : NowPlayingIntro.Drop;
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

    public static void SetBulge(BulgeStyle bulge)
    {
        if (Bulge == bulge)
        {
            return;
        }

        Bulge = bulge;
        Save();
        Changed?.Invoke();
    }

    public static void SetVisualizerLook(VisualizerStyle look)
    {
        if (VisualizerLook == look)
        {
            return;
        }

        VisualizerLook = look;
        Save();
        Changed?.Invoke();
    }

    public static void SetIntro(NowPlayingIntro intro)
    {
        if (Intro == intro)
        {
            return;
        }

        Intro = intro;
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
                Bulge = Bulge.ToString(),
                VisualizerLook = VisualizerLook.ToString(),
                Intro = Intro.ToString(),
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

        public string Bulge { get; set; } = nameof(BulgeStyle.Soft);

        public string VisualizerLook { get; set; } = nameof(VisualizerStyle.Glow);

        public string Intro { get; set; } = nameof(NowPlayingIntro.Drop);
    }
}
