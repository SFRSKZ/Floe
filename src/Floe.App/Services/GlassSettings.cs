using System;
using System.IO;
using System.Text.Json;

namespace Floe.App.Services;

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
    Cubes,
    Stardust,
}

public enum HologramColor
{
    Blue,
    Cyan,
    Violet,
    Pink,
    Gold,
    Green,
    White,
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
        "Floe",
        "glass.json");

    public static event Action? Changed;

    public static RimStyle Rim { get; private set; } = RimStyle.Plain;

    public static bool Visualizer { get; private set; } = true;

    public static BulgeStyle Bulge { get; private set; } = BulgeStyle.Soft;

    public static VisualizerStyle VisualizerLook { get; private set; } = VisualizerStyle.Glow;

    public static HologramColor HologramTint { get; private set; } = HologramColor.Blue;

    public static NowPlayingIntro Intro { get; private set; } = NowPlayingIntro.Drop;

    /// <summary>Edge of one cube of the Cubes visualizer, in DIP.</summary>
    public static double CubeSize { get; private set; } = 17;

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
            HologramTint = Enum.TryParse<HologramColor>(model.HologramTint, true, out var tint) ? tint : HologramColor.Blue;
            Intro = Enum.TryParse<NowPlayingIntro>(model.Intro, true, out var intro) ? intro : NowPlayingIntro.Drop;
            CubeSize = model.CubeSize <= 0 ? 17 : Math.Clamp(Math.Round(model.CubeSize), 8, 24);
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

    public static void SetHologramTint(HologramColor tint)
    {
        if (HologramTint == tint)
        {
            return;
        }

        HologramTint = tint;
        Save();
        Changed?.Invoke();
    }

    // Diagnostic override for offscreen renders; the user's choice is not persisted.
    internal static void PreviewTint(HologramColor tint) => HologramTint = tint;

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

    public static void SetCubeSize(double size)
    {
        size = Math.Clamp(Math.Round(size), 8, 24);
        if (CubeSize == size)
        {
            return;
        }

        CubeSize = size;
        Save();
        Changed?.Invoke();
    }

    private static void Save()
    {
        try
        {
            Floe.Core.Services.AtomicFile.WriteAllText(ConfigPath, JsonSerializer.Serialize(new Model
            {
                Rim = Rim.ToString(),
                Visualizer = Visualizer,
                Bulge = Bulge.ToString(),
                VisualizerLook = VisualizerLook.ToString(),
                HologramTint = HologramTint.ToString(),
                Intro = Intro.ToString(),
                CubeSize = CubeSize,
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

        public double CubeSize { get; set; } = 17;

        public string HologramTint { get; set; } = nameof(HologramColor.Blue);

        public string Intro { get; set; } = nameof(NowPlayingIntro.Drop);
    }
}
