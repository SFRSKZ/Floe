using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using MacDock.App.ViewModels;

namespace MacDock.App.Services;

public enum AuraColorMode
{
    Palette,
    Icon,
}

public enum ClickTone
{
    Dark,
    Light,
}

/// <summary>Colours of the click effect's rim; the values are the shader's Palette constant.</summary>
public enum ClickPalette
{
    Rainbow,
    Clear,
    Icon,
}

public enum AuraEffect
{
    Glow,
    Burst,
    Neon,
    Glass,
    Aurora,
}

public sealed class AuraPalette
{
    public AuraPalette(string name, Color first, Color second, Color third)
    {
        Name = name;
        Colors = new[] { first, second, third };
    }

    public string Name { get; }

    public Color[] Colors { get; }
}

public static class AuraSettings
{
    public static event Action? Changed;

    public static IReadOnlyList<AuraPalette> Palettes { get; } = new[]
    {
        new AuraPalette("Aurora", Rgb(0x5B, 0xC8, 0xFF), Rgb(0xA8, 0x55, 0xF7), Rgb(0x34, 0xE0, 0x7A)),
        new AuraPalette("Gün Batımı", Rgb(0xFF, 0x8A, 0x3D), Rgb(0xFF, 0x4D, 0x8D), Rgb(0xB1, 0x4D, 0xFF)),
        new AuraPalette("Okyanus", Rgb(0x22, 0xD3, 0xEE), Rgb(0x3B, 0x82, 0xF6), Rgb(0x63, 0x66, 0xF1)),
        new AuraPalette("Neon", Rgb(0xFF, 0x2D, 0x95), Rgb(0x00, 0xE5, 0xFF), Rgb(0xC6, 0xFF, 0x00)),
        new AuraPalette("Ateş", Rgb(0xFF, 0xD1, 0x66), Rgb(0xFF, 0x6B, 0x35), Rgb(0xEF, 0x2D, 0x56)),
        new AuraPalette("Buz", Rgb(0xE0, 0xF2, 0xFE), Rgb(0xA5, 0xB4, 0xFC), Rgb(0x7D, 0xD3, 0xFC)),
        new AuraPalette("Orman", Rgb(0x9A, 0xE6, 0x6A), Rgb(0x2E, 0xC4, 0x8B), Rgb(0x0E, 0x9F, 0x8C)),
        new AuraPalette("Mono", Rgb(0xFF, 0xFF, 0xFF), Rgb(0xC7, 0xC9, 0xD3), Rgb(0x8A, 0x8F, 0x9C)),
    };

    private static readonly Dictionary<string, Color[]> IconCache = new();
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MacDock",
        "aura.json");

    public static AuraColorMode Mode { get; private set; } = AuraColorMode.Palette;

    public static AuraEffect Effect { get; private set; } = AuraEffect.Glow;

    /// <summary>Spectral ("Hayalet") effect strength, 0.5 .. 2.5.</summary>
    public static double SpectralIntensity { get; private set; } = 1.5;

    public static bool ClickEffect { get; private set; } = true;

    public static ClickTone ClickTone { get; private set; } = ClickTone.Dark;

    public static ClickPalette ClickPalette { get; private set; } = ClickPalette.Rainbow;

    /// <summary>Playback speed of the click effect, 0.5 .. 2 (1 = 900 ms).</summary>
    public static double ClickSpeed { get; private set; } = 1.0;

    public static int PaletteIndex { get; private set; }

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

            ClickEffect = model.ClickEffect;
            ClickTone = Enum.TryParse<ClickTone>(model.ClickTone, true, out var tone) && Enum.IsDefined(tone) ? tone : ClickTone.Dark;
            ClickPalette = Enum.TryParse<ClickPalette>(model.ClickPalette, true, out var palette) && Enum.IsDefined(palette) ? palette : ClickPalette.Rainbow;
            ClickSpeed = model.ClickSpeed <= 0 ? 1.0 : Math.Clamp(model.ClickSpeed, 0.5, 2.0);
            Mode = Enum.TryParse<AuraColorMode>(model.Mode, true, out var mode) ? mode : AuraColorMode.Palette;
            Effect = Enum.TryParse<AuraEffect>(model.Effect, true, out var effect) ? effect : AuraEffect.Glow;
            SpectralIntensity = model.SpectralIntensity <= 0 ? 1.5 : Math.Clamp(model.SpectralIntensity, 0.5, 2.5);
            PaletteIndex = Math.Clamp(model.PaletteIndex, 0, Palettes.Count - 1);
        }
        catch
        {
            // fall back to defaults
        }
    }

    public static void SetClickEffect(bool enabled)
    {
        if (ClickEffect == enabled) return;
        ClickEffect = enabled;
        Save();
        Changed?.Invoke();
    }

    public static void SetClickTone(ClickTone tone)
    {
        if (ClickTone == tone) return;
        ClickTone = tone;
        Save();
        Changed?.Invoke();
    }

    public static void SetClickPalette(ClickPalette palette)
    {
        if (ClickPalette == palette) return;
        ClickPalette = palette;
        Save();
        Changed?.Invoke();
    }

    public static void SetClickSpeed(double speed)
    {
        speed = Math.Clamp(speed, 0.5, 2.0);
        if (Math.Abs(ClickSpeed - speed) < 0.001) return;
        ClickSpeed = speed;
        Save();
        Changed?.Invoke();
    }

    public static void SetMode(AuraColorMode mode)
    {
        if (Mode == mode)
        {
            return;
        }

        Mode = mode;
        Save();
        Changed?.Invoke();
    }

    public static void SetEffect(AuraEffect effect)
    {
        if (Effect == effect)
        {
            return;
        }

        Effect = effect;
        Save();
        Changed?.Invoke();
    }

    public static void SetSpectralIntensity(double intensity)
    {
        intensity = Math.Clamp(intensity, 0.5, 2.5);
        if (Math.Abs(SpectralIntensity - intensity) < 0.01)
        {
            return;
        }

        SpectralIntensity = intensity;
        Save();
        Changed?.Invoke();
    }

    public static void SetPalette(int index)
    {
        index = Math.Clamp(index, 0, Palettes.Count - 1);
        if (PaletteIndex == index)
        {
            return;
        }

        PaletteIndex = index;
        Save();
        Changed?.Invoke();
    }

    public static Color[] GetColors(DockItemViewModel item)
    {
        if (Mode == AuraColorMode.Icon && item.Icon is not null)
        {
            var key = AppLauncher.ResolveTarget(item.App);
            if (!IconCache.TryGetValue(key, out var colors))
            {
                colors = IconService.GetIconColors(item.Icon, 3);
                IconCache[key] = colors;
            }

            return colors;
        }

        return Palettes[Math.Clamp(PaletteIndex, 0, Palettes.Count - 1)].Colors;
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            MacDock.Core.Services.AtomicFile.WriteAllText(ConfigPath, JsonSerializer.Serialize(new Model
            {
                ClickEffect = ClickEffect,
                ClickTone = ClickTone.ToString(),
                ClickPalette = ClickPalette.ToString(),
                ClickSpeed = ClickSpeed,
                Mode = Mode.ToString(),
                Effect = Effect.ToString(),
                SpectralIntensity = SpectralIntensity,
                PaletteIndex = PaletteIndex,
            }));
        }
        catch
        {
            // settings persistence is best-effort
        }
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private sealed class Model
    {
        public bool ClickEffect { get; set; } = true;

        public string ClickTone { get; set; } = nameof(global::MacDock.App.Services.ClickTone.Dark);

        public string ClickPalette { get; set; } = nameof(global::MacDock.App.Services.ClickPalette.Rainbow);

        public double ClickSpeed { get; set; } = 1.0;

        public string Mode { get; set; } = nameof(AuraColorMode.Palette);

        public string Effect { get; set; } = nameof(AuraEffect.Glow);

        public double SpectralIntensity { get; set; } = 1.5;

        public int PaletteIndex { get; set; }
    }
}
