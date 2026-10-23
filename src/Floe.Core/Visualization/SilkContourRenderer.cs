using System.Numerics;
using Floe.Core.Interop;

namespace Floe.Core.Visualization;

public enum ContourQuality
{
    Full,
    Balanced,
    Lite,
}

/// <summary>
/// A shallow satin surface, drawn as continuous cross-sections of one moving field. This renderer has
/// no window or audio-device dependency: its premultiplied pixels can also be inspected offscreen.
/// Only Resize allocates image buffers; Step reuses them, including the short, faint light trails.
/// </summary>
public sealed class SilkContourRenderer
{
    private const int Bands = 24;
    private const int PaletteSize = 64;
    private readonly float[] _bands = new float[Bands];
    private readonly Vector3[] _palette = new Vector3[PaletteSize];
    private Vector3[] _light = Array.Empty<Vector3>();
    private Vector3[] _trail = Array.Empty<Vector3>();
    private float[] _primary = Array.Empty<float>(), _primaryCos = Array.Empty<float>();
    private float[] _secondary = Array.Empty<float>(), _secondaryCos = Array.Empty<float>();
    private float[] _detail = Array.Empty<float>(), _detailCos = Array.Empty<float>();
    private float[] _spectrum = Array.Empty<float>(), _wing = Array.Empty<float>();
    private float[] _topFade = Array.Empty<float>();
    private Vector3[] _columnColor = Array.Empty<Vector3>();
    private double _widthDip, _heightDip;
    private ContourQuality _quality;
    private double _phase, _quiet;
    private float _energy, _bass, _mid, _treble, _kick, _snare, _hat;

    public SilkContourRenderer() => SetPalette(new[] { 0x51B8AC, 0x75D9ED, 0xB7A7ED, 0xE8D8B9 });

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>Reusable Pbgra32 buffer. Packed ARGB integers are BGRA bytes on little-endian machines.</summary>
    public int[] Pixels { get; private set; } = Array.Empty<int>();

    public int LineCount { get; private set; }

    public bool IsEmpty { get; private set; } = true;

    /// <summary>Resize to DIP bounds, reducing resolution and line count with the decoration budget.</summary>
    public void Resize(double widthDip, double heightDip, ContourQuality quality = ContourQuality.Full)
    {
        if (!double.IsFinite(widthDip) || !double.IsFinite(heightDip) || widthDip < 2 || heightDip < 2)
        {
            widthDip = heightDip = 0;
        }

        if (_widthDip == widthDip && _heightDip == heightDip && _quality == quality) return;
        _widthDip = widthDip;
        _heightDip = heightDip;
        _quality = quality;
        LineCount = quality switch { ContourQuality.Full => 18, ContourQuality.Balanced => 14, _ => 10 };
        double scale = quality switch { ContourQuality.Full => 1, ContourQuality.Balanced => .75, _ => .5 };
        // Even malformed or unusually large bounds cannot request an unbounded allocation.
        if (widthDip > 0) scale = Math.Min(scale, Math.Min(4096 / widthDip, 256 / heightDip));
        Width = widthDip > 0 ? Math.Clamp((int)Math.Round(widthDip * scale), 2, 4096) : 0;
        Height = heightDip > 0 ? Math.Clamp((int)Math.Round(heightDip * scale), 2, 256) : 0;
        int n = Width * Height;
        Pixels = new int[n];
        IsEmpty = true;
        _light = new Vector3[n];
        _trail = new Vector3[n];
        _primary = new float[Width];
        _primaryCos = new float[Width];
        _secondary = new float[Width];
        _secondaryCos = new float[Width];
        _detail = new float[Width];
        _detailCos = new float[Width];
        _spectrum = new float[Width];
        _wing = new float[Width];
        _columnColor = new Vector3[Width];
        _topFade = new float[Height];
        for (int y = 0; y < Height; y++)
        {
            float u = Math.Clamp(y / Math.Max(1f, Height * .19f), 0, 1);
            _topFade[y] = u * u * (3 - (2 * u));
        }

        BuildColumns();
    }

    /// <summary>Two to sixteen RGB stops, from the middle of the strip towards its ends.</summary>
    public void SetPalette(ReadOnlySpan<int> rgbStops)
    {
        if (rgbStops.Length is < 2 or > 16) throw new ArgumentException("Use two to sixteen RGB stops.", nameof(rgbStops));
        for (int i = 0; i < PaletteSize; i++)
        {
            float at = i * (rgbStops.Length - 1f) / (PaletteSize - 1);
            int k = Math.Min(rgbStops.Length - 2, (int)at);
            _palette[i] = Vector3.Lerp(Rgb(rgbStops[k]), Rgb(rgbStops[k + 1]), at - k);
        }

        BuildColumns();
    }

    /// <summary>Forget the last track and its light, for stopping or unloading a host.</summary>
    public void Reset()
    {
        _energy = _bass = _mid = _treble = _kick = _snare = _hat = 0;
        _phase = _quiet = 0;
        Array.Clear(_bands);
        Array.Clear(_light);
        Array.Clear(_trail);
        Array.Clear(Pixels);
        IsEmpty = true;
    }

    /// <summary>
    /// Advance by elapsed seconds. Null/zero audio fades completely to transparency. The onset fields
    /// are spectral transients, not identified instruments; they gently open or light the same surface.
    /// Non-finite audio is treated as silence, and long host pauses cannot destabilise the animation.
    /// </summary>
    public void Step(SpectrumFrame? frame, double dt, bool active = true, float opacity = 1)
    {
        if (!double.IsFinite(dt) || dt <= 0) return;
        dt = Math.Min(dt, .1);
        float attack = (float)(1 - Math.Exp(-dt / .055));
        float release = (float)(1 - Math.Exp(-dt / .30));
        float peakBand = 0;
        for (int i = 0; i < Bands; i++)
        {
            float target = active && frame is not null && i < frame.Bands.Length ? Unit(frame.Bands[i]) : 0;
            peakBand = Math.Max(peakBand, target);
            _bands[i] += (target - _bands[i]) * (target > _bands[i] ? attack : release);
        }

        float bass = active && frame is not null ? Unit(frame.Bass) : 0;
        float mid = active && frame is not null ? Unit(frame.Mid) : 0;
        float treble = active && frame is not null ? Unit(frame.Treble) : 0;
        float targetEnergy = active && frame is not null ? Unit(frame.Level) : 0;
        targetEnergy = Math.Max(targetEnergy, Math.Max(bass * .8f, Math.Max(mid * .65f, Math.Max(treble * .5f, peakBand * .6f))));
        _energy = Follow(_energy, targetEnergy, attack, release);
        _bass = Follow(_bass, bass, attack, release);
        _mid = Follow(_mid, mid, attack, release);
        _treble = Follow(_treble, treble, attack, release);
        bool sound = targetEnergy > .002f;
        _quiet = sound ? 0 : _quiet + dt;
        _kick = Math.Max(_kick * (float)Math.Exp(-dt / .24), sound && frame is not null ? Unit(frame.Kick) : 0);
        _snare = Math.Max(_snare * (float)Math.Exp(-dt / .16), sound && frame is not null ? Unit(frame.Snare) : 0);
        _hat = Math.Max(_hat * (float)Math.Exp(-dt / .10), sound && frame is not null ? Unit(frame.Hat) : 0);

        if (Width == 0 || Height == 0) return;
        if (_quiet > 1.8 || _energy < .001f)
        {
            Array.Clear(Pixels);
            Array.Clear(_trail);
            IsEmpty = true;
            return;
        }

        // No free-running screensaver: flow slows with the music and stops in silence.
        _phase = (_phase + (dt * _energy * (.65 + (.22 * _bass)))) % (Math.PI * 200);
        Array.Clear(_light);
        PrepareField();
        DrawSurface();
        Present((float)dt, Unit(opacity));
    }

    private void BuildColumns()
    {
        for (int x = 0; x < Width; x++)
        {
            float u = x / Math.Max(1f, Width - 1);
            float distance = Math.Abs((2 * u) - 1);
            float at = distance * (PaletteSize - 1);
            int i = Math.Min(PaletteSize - 2, (int)at);
            _columnColor[x] = Vector3.Lerp(_palette[i], _palette[i + 1], at - i);
            // Quiet under the capsule; the two visible wings carry the brightest folds.
            _wing[x] = .28f + (.72f * (1 - MathF.Exp(-distance * distance / .12f)));
        }
    }

    private void PrepareField()
    {
        float cycles = (float)Math.Clamp(_widthDip / 780, 1.35, 4.2);
        float phase = (float)_phase;
        for (int x = 0; x < Width; x++)
        {
            float u = x / Math.Max(1f, Width - 1);
            float p = (u * MathF.Tau * cycles) - phase;
            (_primary[x], _primaryCos[x]) = MathF.SinCos(p);
            (_secondary[x], _secondaryCos[x]) = MathF.SinCos((p * 1.83f) + (phase * .24f) + 1.1f);
            (_detail[x], _detailCos[x]) = MathF.SinCos((p * 3.07f) - (phase * .16f) + .7f);
            float at = Math.Abs((2 * u) - 1) * (Bands - 1);
            int b = Math.Min(Bands - 2, (int)at);
            _spectrum[x] = _bands[b] + ((_bands[b + 1] - _bands[b]) * (at - b));
        }
    }

    private void DrawSurface()
    {
        float amplitude = .13f + (.16f * _bass) + (.025f * _kick);
        float energy = MathF.Pow(_energy, .7f);
        for (int line = 0; line < LineCount; line++)
        {
            float depth = line / (float)(LineCount - 1);
            float z = (depth * 2) - 1;
            var (s0, c0) = MathF.SinCos(z * 1.18f);
            var (s1, c1) = MathF.SinCos(-z * .65f);
            var (s2, c2) = MathF.SinCos(z * 1.4f);
            // A few neighbouring sections carry the specular crest; the others describe the terrain.
            float crest = MathF.Exp(-MathF.Pow((depth - .46f) / .105f, 2));
            float strength = (.18f + (.22f * depth) + (4.2f * crest)) * energy;
            for (int x = 0; x < Width; x++)
            {
                float broad = (_primary[x] * c0) + (_primaryCos[x] * s0);
                float fold = (_secondary[x] * c1) + (_secondaryCos[x] * s1);
                float detail = (_detail[x] * c2) + (_detailCos[x] * s2);
                float local = _spectrum[x];
                float field = (.67f * broad) + (.25f * fold) + (.08f * _mid * detail);
                // Analytically bounded: even maximum energy keeps all sections inside the strip.
                float y = (.52f + (.15f * z) + (amplitude * field) + (.018f * (local - .5f) * fold)) * (Height - 1);
                float ridge = Math.Max(0, -broad);
                float intensity = strength * _wing[x] * (.72f + (.40f * local) + (.24f * ridge) + (.16f * _snare));
                var color = Vector3.Lerp(_columnColor[x], Vector3.One, crest * ridge * (.22f + (.12f * _treble)));
                Stroke(x, y, color * intensity, crest);

                // A sparse, deterministic glint follows the same crest; no independent particle field.
                if (line == LineCount / 2 && _hat > .025f)
                {
                    float spot = MathF.Abs(MathF.Sin((x * .027f) - ((float)_phase * 1.7f)));
                    if (spot > .998f && ridge > .4f)
                    {
                        Stroke(x, y, Vector3.Lerp(color, Vector3.One, .65f) * (_hat * energy * .4f * _wing[x]), 0);
                    }
                }
            }
        }
    }

    private void Stroke(int x, float y, Vector3 color, float crest)
    {
        int row = (int)y;
        float fraction = y - row;
        // A continuous line at every raster column, with subpixel coverage at its two neighbouring rows.
        Add(x, row, color * (1 - fraction));
        Add(x, row + 1, color * fraction);
        // Narrow bloom only. The sharp stroke is regenerated each frame, never blurred by feedback.
        float glow = .065f + (.055f * crest);
        Add(x, row - 1, color * (glow * (1 - fraction)));
        Add(x, row + 2, color * (glow * fraction));
        Add(x, row - 2, color * (glow * .25f * (1 - fraction)));
        Add(x, row + 3, color * (glow * .25f * fraction));
    }

    private void Add(int x, int y, Vector3 color)
    {
        if ((uint)y < (uint)Height) _light[(y * Width) + x] += color;
    }

    private void Present(float dt, float opacity)
    {
        float retain = MathF.Exp(-dt / .11f);
        int visible = 0;
        for (int y = 0; y < Height; y++)
        {
            float fade = _topFade[y] * opacity;
            for (int x = 0, i = y * Width; x < Width; x++, i++)
            {
                var light = _light[i];
                var trail = _trail[i];
                _trail[i] = (trail * retain) + (light * (1 - retain));
                var value = (light + (trail * .12f)) * fade;
                // A soft knee avoids white clipped bands where the sheet folds over itself.
                // Use one exposure for all channels so bright cyan/lilac retains its hue.
                float exposure = 255 / (1 + Math.Max(value.X, Math.Max(value.Y, value.Z)));
                int r = (int)(value.X * exposure), g = (int)(value.Y * exposure), b = (int)(value.Z * exposure);
                int a = Math.Max(r, Math.Max(g, b));
                visible |= a;
                Pixels[i] = (a << 24) | (r << 16) | (g << 8) | b;
            }
        }

        IsEmpty = visible == 0;
    }

    private static Vector3 Rgb(int rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

    private static float Unit(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    private static float Follow(float value, float target, float rise, float fall) => value + ((target - value) * (target > value ? rise : fall));
}
