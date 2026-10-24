using System.Numerics;
using Floe.App.Services;
using Floe.Core.Interop;

namespace Floe.App.Effects.Gpu;

/// <summary>What a scene needs to draw one frame; the host refills one instance every frame.</summary>
public sealed class GpuFrame
{
    public const int PaletteSize = 16;

    /// <summary>Seconds since the last frame, clamped to 0.1.</summary>
    public float Dt { get; set; }

    /// <summary>Seconds since the scene started.</summary>
    public float Time { get; set; }

    /// <summary>0..1: fades in when the music starts, out when it stops; multiply all output by it.</summary>
    public float Fade { get; set; }

    /// <summary>The target in pixels, and pixels per DIP (the strip is ~76 DIP tall).</summary>
    public int Width { get; set; }
    public int Height { get; set; }
    public float PixelsPerDip { get; set; } = 1;

    /// <summary>The latest analysis (bands, bass/mid/treble, level, kick/snare/hat onsets).</summary>
    public SpectrumFrame Audio { get; } = new(24);

    /// <summary>
    /// The user's colours, rgb 0..1, from the middle of the strip (index 0) out to its ends (index 15);
    /// already resolved from GlassSettings (tint, gradient palettes, custom, the turning rainbow).
    /// </summary>
    public Vector3[] Palette { get; } = new Vector3[PaletteSize];

    public PerformanceLevel Performance { get; set; }

    /// <summary>The dust wave look of the stardust scene (see StardustVisualizer.Wave).</summary>
    public bool Wave { get; set; }
}
