using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using Floe.App.ViewModels;

namespace Floe.App.Services;

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

    /// <summary>Asks the dock to play its sleep derezz once, so a new speed can be seen.</summary>
    public static event Action? DerezzPreviewRequested;

    public static void PreviewDerezz() => DerezzPreviewRequested?.Invoke();

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
        "Floe",
        "aura.json");

    public static AuraColorMode Mode { get; private set; } = AuraColorMode.Palette;

    public static AuraEffect Effect { get; private set; } = AuraEffect.Glow;

    /// <summary>Spectral ("Hayalet") effect strength, 0.5 .. 2.5.</summary>
    public static double SpectralIntensity { get; private set; } = 1.5;

    public static bool ClickEffect { get; private set; } = true;

    /// <summary>The glass tinkle a click plays; independent of the visual click effect.</summary>
    public static bool ClickSound { get; private set; } = true;

    /// <summary>The click's glass shows the icon as an LED screen of light dots instead of clear glass.</summary>
    public static bool ClickScreen { get; private set; }

    /// <summary>The click as Van Gogh's brush: swirling impasto strokes of the icon's own colours.</summary>
    public static bool ClickPaint { get; private set; }

    public static ClickTone ClickTone { get; private set; } = ClickTone.Dark;

    public static ClickPalette ClickPalette { get; private set; } = ClickPalette.Clear;

    /// <summary>Playback speed of the click effect, 0.5 .. 2 (1 = 900 ms).</summary>
    public static double ClickSpeed { get; private set; } = 1.0;

    public static bool Derezz { get; private set; } = true;

    /// <summary>Playback speed of the sleep (derezz) effect, 0.5 .. 2.</summary>
    public static double DerezzSpeed { get; private set; } = 1.0;

    /// <summary>Edge of one sleep (derezz) cube in dock units, 4 .. 12.</summary>
    public static double DerezzCell { get; private set; } = 6;

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
            ClickSound = model.ClickSound;
            ClickScreen = model.ClickScreen;
            ClickPaint = model.ClickPaint;
            ClickTone = Enum.TryParse<ClickTone>(model.ClickTone, true, out var tone) && Enum.IsDefined(tone) ? tone : ClickTone.Dark;
            ClickPalette = Enum.TryParse<ClickPalette>(model.ClickPalette, true, out var palette) && Enum.IsDefined(palette) ? palette : ClickPalette.Clear;
            ClickSpeed = model.ClickSpeed <= 0 ? 1.0 : Math.Clamp(model.ClickSpeed, 0.5, 2.0);
            Derezz = model.Derezz;
            DerezzSpeed = model.DerezzSpeed <= 0 ? 1.0 : Math.Clamp(model.DerezzSpeed, 0.5, 2.0);
            DerezzCell = model.DerezzCell <= 0 ? 6 : Math.Clamp(Math.Round(model.DerezzCell), 4, 24);
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

    public static void SetClickPaint(bool enabled)
    {
        if (ClickPaint == enabled) return;
        ClickPaint = enabled;
        Save();
        Changed?.Invoke();
    }

    public static void SetClickScreen(bool enabled)
    {
        if (ClickScreen == enabled) return;
        ClickScreen = enabled;
        Save();
        Changed?.Invoke();
    }

    public static void SetClickSound(bool enabled)
    {
        if (ClickSound == enabled) return;
        ClickSound = enabled;
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

    public static void SetDerezz(bool enabled)
    {
        if (Derezz == enabled) return;
        Derezz = enabled;
        Save();
        Changed?.Invoke();
    }

    public static void SetDerezzCell(double cell)
    {
        cell = Math.Clamp(Math.Round(cell), 4, 24);
        if (Math.Abs(DerezzCell - cell) < 0.001) return;
        DerezzCell = cell;
        Save();
        Changed?.Invoke();
    }

    public static void SetDerezzSpeed(double speed)
    {
        speed = Math.Clamp(speed, 0.5, 2.0);
        if (Math.Abs(DerezzSpeed - speed) < 0.001) return;
        DerezzSpeed = speed;
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
        if (GlassSettings.Tron) return ThemePalette.Glows;
        if (GlassSettings.Cyber) return new[] { CyberLook.Current.Accent, CyberLook.Current.Strip, CyberLook.Current.Accent };

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
            Floe.Core.Services.AtomicFile.WriteAllText(ConfigPath, JsonSerializer.Serialize(new Model
            {
                ClickEffect = ClickEffect,
                ClickSound = ClickSound,
                ClickScreen = ClickScreen,
                ClickPaint = ClickPaint,
                ClickTone = ClickTone.ToString(),
                ClickPalette = ClickPalette.ToString(),
                ClickSpeed = ClickSpeed,
                Derezz = Derezz,
                DerezzSpeed = DerezzSpeed,
                DerezzCell = DerezzCell,
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

        public bool ClickSound { get; set; } = true;

        public bool ClickScreen { get; set; }

        public bool ClickPaint { get; set; }

        public string ClickTone { get; set; } = nameof(global::Floe.App.Services.ClickTone.Dark);

        public string ClickPalette { get; set; } = nameof(global::Floe.App.Services.ClickPalette.Clear);

        public double ClickSpeed { get; set; } = 1.0;

        public bool Derezz { get; set; } = true;

        public double DerezzSpeed { get; set; } = 1.0;

        public double DerezzCell { get; set; } = 6;

        public string Mode { get; set; } = nameof(AuraColorMode.Palette);

        public string Effect { get; set; } = nameof(AuraEffect.Glow);

        public double SpectralIntensity { get; set; } = 1.5;

        public int PaletteIndex { get; set; }
    }
}
