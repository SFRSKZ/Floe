using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floe.App.Services;
using Floe.Core.Interop;

namespace Floe.App.Effects.Visualizer;

/// <summary>
/// Music visualizer of star dust over a soft aurora along the bottom edge of <see cref="Capsule"/>.
/// <para>
/// It listens to the spectrum, not only the loudness. The aurora's height follows the bands (lows under the
/// middle, highs out to the sides) and swells with the bass. The dust drifts up at three depths: far grains
/// small and crisp, near ones large and out of focus. A kick blooms the light and lifts the dust, a snare
/// makes it shimmer and a hi-hat lets a few grains flare.
/// </para>
/// <para>
/// After MilkDrop, the dust is drawn over the last frame dimmed and lifted a little, which leaves a short,
/// soft motion blur. Everything is drawn on the CPU into one bitmap at a reduced resolution; the render
/// thread only scales it up.
/// </para>
/// </summary>
public sealed class StardustVisualizer : FrameworkElement
{
    private const double FadeOutSeconds = 0.40;
    private const double FadeInSeconds = 0.22;
    private const int Bands = 24;
    private const int MaxParticles = 2500;
    private const int PaletteSize = 16;
    // Gaussian sprites for the grains, by blur: crisp far dust up to out-of-focus near dust.
    private static readonly float[] Sigmas = { .55f, .9f, 1.5f, 2.4f, 3.4f };

    private static readonly Color BasePrimary = Color.FromRgb(0x5C, 0xA8, 0xFF);

    // Shared palette, rebuilt when the tint changes: 0..1 rgb from the tint to a neighbouring hue.
    private static readonly float[] PaletteR = new float[PaletteSize];
    private static readonly float[] PaletteG = new float[PaletteSize];
    private static readonly float[] PaletteB = new float[PaletteSize];
    private static HologramColor? _paletteTint;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Random _random = new(0x5DA57);
    private readonly SpectrumFrame _frame = new(Bands);
    private readonly float[] _band = new float[Bands];

    // particles, structure of arrays; depth 0 is far, 1 is near
    private readonly float[] _x = new float[MaxParticles];
    private readonly float[] _y = new float[MaxParticles];
    private readonly float[] _vx = new float[MaxParticles];
    private readonly float[] _vy = new float[MaxParticles];
    private readonly float[] _age = new float[MaxParticles];
    private readonly float[] _life = new float[MaxParticles];
    private readonly float[] _bright = new float[MaxParticles];
    private readonly float[] _phase = new float[MaxParticles];
    private readonly float[] _depth = new float[MaxParticles];
    private readonly float[] _flare = new float[MaxParticles];
    private readonly byte[] _hue = new byte[MaxParticles];
    private int _count;

    // The wave: grains held by springs on two sine waves across the strip. Two thirds ride the main wave,
    // a third a fainter one flowing the other way. Four sines each; their amplitudes follow four band groups.
    private static readonly float[] WaveFrequency = { .22f, .5f, .95f, 1.6f };
    private static readonly float[] WaveSpeed = { 1.8f, -3.1f, 4.6f, -6.5f };
    private const float SpringStiffness = 90;
    private const float SpringDamping = 18;
    private readonly float[] _waveAmp = new float[4];
    private float[] _wx = Array.Empty<float>(), _wy = Array.Empty<float>(), _wvx = Array.Empty<float>(), _wvy = Array.Empty<float>();
    private float[] _wPhase = Array.Empty<float>();
    private int _waveCount;
    private double _wavePhase;

    // the dust picture, premultiplied rgb, at _scale pixels per DIP; the aurora is added only on present
    private float[] _r = Array.Empty<float>(), _g = Array.Empty<float>(), _b = Array.Empty<float>();
    private float[] _r2 = Array.Empty<float>(), _g2 = Array.Empty<float>(), _b2 = Array.Empty<float>();
    private float[] _auraHeight = Array.Empty<float>(), _auraR = Array.Empty<float>(), _auraG = Array.Empty<float>(), _auraB = Array.Empty<float>(), _auraScratch = Array.Empty<float>();
    private float[][] _sprites = Array.Empty<float[]>();
    private int[] _spriteRadius = Array.Empty<int>();
    private int[] _pixels = Array.Empty<int>();
    private WriteableBitmap? _bitmap;
    private int _w, _h;
    private double _scale;

    private AudioSpectrum? _spectrum;
    private Rect _capsule = Rect.Empty;
    private bool _active;
    private bool _hooked;
    private double _fade;
    private double _bass;
    private double _level;
    private double _pulse;
    private double _shimmer;
    private double _lastFrameSeconds = -1;
    private double _sinceRepaint;
    private double _time;
    private double _emitCarry;

    public StardustVisualizer()
    {
        IsHitTestVisible = false;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);
        Loaded += (_, _) =>
        {
            if (_active) Hook();
        };
        Unloaded += (_, _) =>
        {
            Unhook();
            _spectrum?.Dispose();
            _spectrum = null;
            _lastFrameSeconds = -1;
        };
    }

    /// <summary>Bounds in this element's coordinates; the aurora and the dust rise from its bottom edge.</summary>
    public Rect Capsule
    {
        get => _capsule;
        set
        {
            if (_capsule == value) return;
            _capsule = value;
            InvalidateVisual();
        }
    }

    /// <summary>On listens and draws; off stops the dust, fades out (~400 ms) and unhooks.</summary>
    public bool IsActive
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            if (value)
            {
                _lastFrameSeconds = -1;
                if (IsLoaded) Hook();
            }
            else
            {
                // nobody listens: release the loopback capture
                _spectrum?.Dispose();
                _spectrum = null;
            }

            InvalidateVisual();
        }
    }

    /// <summary>
    /// The dust wave look: most of the dust held on two waves the spectrum shapes, a beat scattering it,
    /// with less of it drifting free. Off is plain star dust.
    /// </summary>
    public bool Wave { get; init; }

    /// <summary>Offscreen renders: set to feed this frame instead of the live capture.</summary>
    internal SpectrumFrame? DebugFrame { get; set; }

    /// <summary>Advances the dust and the picture by <paramref name="dt"/>.</summary>
    internal void Step(double dt)
    {
        _time += dt;
        if (DebugFrame is not null) _fade = 1;
        if (!EnsureBuffers()) return;
        EnsurePalette();
        var f = DebugFrame ?? _frame;
        bool live = _active || DebugFrame is not null;

        // bands: rise quickly, settle slowly
        float rise = (float)(1 - Math.Exp(-dt / .05)), settle = (float)(1 - Math.Exp(-dt / .28));
        for (int i = 0; i < Bands; i++)
        {
            float target = live ? f.Bands[i] : 0;
            _band[i] += (target - _band[i]) * (target > _band[i] ? rise : settle);
        }

        double bass = live ? f.Bass : 0, level = live ? f.Level : 0;
        _bass += (bass - _bass) * (1 - Math.Exp(-dt / (bass > _bass ? .04 : .25)));
        _level += (level - _level) * (1 - Math.Exp(-dt / (level > _level ? .05 : .35)));
        _pulse *= Math.Exp(-dt / .22);
        _shimmer *= Math.Exp(-dt / .15);
        if (live && f.Kick > 0)
        {
            if (Wave) Scatter(.15f + (.35f * (float)Math.Max(_bass, f.Kick)));
            _pulse = Math.Max(_pulse, f.Kick);
            Lift(f.Kick);
        }

        if (live && f.Snare > 0) _shimmer = Math.Max(_shimmer, f.Snare);

        Blur(dt);
        if (Wave) MoveWave(dt, live);
        if (live) Emit(dt, f);
        Move(dt);
        Splat();
        if (Wave) SplatWave();
        Aurora();
        Present();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_bitmap is null || _fade <= .001 || _capsule.IsEmpty) return;
        dc.DrawImage(_bitmap, _capsule);
    }

    // ---- the picture ------------------------------------------------------------------------------

    private bool EnsureBuffers()
    {
        if (_capsule.IsEmpty || _capsule.Width < 2 || _capsule.Height < 2) return false;
        double scale = Performance.Level switch
        {
            PerformanceLevel.Full => 1.0,
            PerformanceLevel.Balanced => .75,
            _ => .5,
        };
        int w = Math.Max(2, (int)Math.Round(_capsule.Width * scale)), h = Math.Max(2, (int)Math.Round(_capsule.Height * scale));
        if (w == _w && h == _h && _bitmap is not null) return true;
        _w = w;
        _h = h;
        _scale = scale;
        int n = w * h;
        _r = new float[n];
        _g = new float[n];
        _b = new float[n];
        _r2 = new float[n];
        _g2 = new float[n];
        _b2 = new float[n];
        _auraHeight = new float[w];
        _auraR = new float[w];
        _auraG = new float[w];
        _auraB = new float[w];
        _auraScratch = new float[w];
        _pixels = new int[n];
        if (Wave) AllocateWave();
        _bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);

        _sprites = new float[Sigmas.Length][];
        _spriteRadius = new int[Sigmas.Length];
        for (int s = 0; s < Sigmas.Length; s++)
        {
            float sigma = (float)(Sigmas[s] * scale);
            int radius = Math.Max(1, (int)MathF.Ceiling(sigma * 2.5f));
            int side = (radius * 2) + 1;
            var sprite = new float[side * side];
            float sum = 0;
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    float v = MathF.Exp(-((x * x) + (y * y)) / (2 * sigma * sigma));
                    sprite[((y + radius) * side) + x + radius] = v;
                    sum += v;
                }
            }

            // every sprite carries the same light, spread wider; the peak is kept for the crisp ones
            float norm = 1 / Math.Max(1, sum * .35f);
            for (int i = 0; i < sprite.Length; i++) sprite[i] *= norm;
            _sprites[s] = sprite;
            _spriteRadius[s] = radius;
        }

        return true;
    }

    // MilkDrop's feedback, held short: the last frame lifted a touch and dimmed fast, a soft motion blur.
    private void Blur(double dt)
    {
        int w = _w, h = _h;
        float decay = (float)Math.Exp(-dt / .055);
        float lift = (float)(.35 * _scale * dt * 30);
        var (r, g, b, r2, g2, b2) = (_r, _g, _b, _r2, _g2, _b2);
        Parallel.For(0, h, y =>
        {
            float sy = y + lift;
            int y0 = (int)sy;
            float fy = sy - y0;
            int row = y * w;
            if (y0 + 1 >= h)
            {
                Array.Clear(r2, row, w);
                Array.Clear(g2, row, w);
                Array.Clear(b2, row, w);
                return;
            }

            int a = y0 * w, c = a + w;
            float wa = (1 - fy) * decay, wc = fy * decay;
            for (int x = 0; x < w; x++)
            {
                r2[row + x] = (r[a + x] * wa) + (r[c + x] * wc);
                g2[row + x] = (g[a + x] * wa) + (g[c + x] * wc);
                b2[row + x] = (b[a + x] * wa) + (b[c + x] * wc);
            }
        });

        (_r, _r2) = (_r2, _r);
        (_g, _g2) = (_g2, _g);
        (_b, _b2) = (_b2, _b);
    }

    private void Splat()
    {
        float scale = (float)_scale;
        float t = (float)_time;
        float shimmer = (float)_shimmer;
        for (int p = 0; p < _count; p++)
        {
            float age = _age[p], life = _life[p];
            float envelope = Math.Min(1, age / .25f) * Math.Min(1, (life - age) / .45f);
            float twinkle = .75f + (.25f * MathF.Sin(_phase[p] + (t * (2.2f + (_phase[p] % 3)))));
            float depth = _depth[p];
            float flare = _flare[p];
            float i = 1.9f * _bright[p] * envelope * (twinkle + (shimmer * .8f * (1 - depth))) * (1 + (2.5f * flare));
            if (i <= .004f) continue;
            int hue = _hue[p];
            // near dust is out of focus: wide and faint, like bokeh
            int s = flare > .2f ? 2 : Math.Min(Sigmas.Length - 1, (int)(depth * Sigmas.Length));
            Sprite(_x[p] * scale, _y[p] * scale, s, PaletteR[hue] * i, PaletteG[hue] * i, PaletteB[hue] * i);
        }
    }

    private void Sprite(float px, float py, int s, float cr, float cg, float cb)
    {
        int radius = _spriteRadius[s], side = (radius * 2) + 1;
        var sprite = _sprites[s];
        int cx = (int)MathF.Round(px), cy = (int)MathF.Round(py);
        if (s == 0)
        {
            // the crisp ones move between pixels smoothly
            Dot(px, py, cr * 1.4f, cg * 1.4f, cb * 1.4f);
            return;
        }

        int x0 = Math.Max(0, cx - radius), x1 = Math.Min(_w - 1, cx + radius);
        int y0 = Math.Max(0, cy - radius), y1 = Math.Min(_h - 1, cy + radius);
        for (int y = y0; y <= y1; y++)
        {
            int srow = (y - cy + radius) * side, row = y * _w;
            for (int x = x0; x <= x1; x++)
            {
                float v = sprite[srow + x - cx + radius];
                int i = row + x;
                _r[i] += cr * v;
                _g[i] += cg * v;
                _b[i] += cb * v;
            }
        }
    }

    private void Dot(float px, float py, float cr, float cg, float cb)
    {
        int x0 = (int)MathF.Floor(px - .5f), y0 = (int)MathF.Floor(py - .5f);
        float fx = px - .5f - x0, fy = py - .5f - y0;
        Add(x0, y0, (1 - fx) * (1 - fy), cr, cg, cb);
        Add(x0 + 1, y0, fx * (1 - fy), cr, cg, cb);
        Add(x0, y0 + 1, (1 - fx) * fy, cr, cg, cb);
        Add(x0 + 1, y0 + 1, fx * fy, cr, cg, cb);
    }

    private void Add(int x, int y, float weight, float cr, float cg, float cb)
    {
        if ((uint)x >= (uint)_w || (uint)y >= (uint)_h) return;
        int i = (y * _w) + x;
        _r[i] += cr * weight;
        _g[i] += cg * weight;
        _b[i] += cb * weight;
    }

    // The aurora, per column: how high it reaches and its colour; drawn on present, never fed back.
    private void Aurora()
    {
        int w = _w;
        double height = _capsule.Height * _scale;
        float bass = (float)_bass, pulse = (float)_pulse, level = (float)_level;
        for (int x = 0; x < w; x++)
        {
            float xn = MathF.Abs((x / (float)w) - .5f) * 2;
            // bands laid out from the middle outwards, interpolated so the curve stays smooth
            float at = xn * (Bands - 1);
            int b0 = Math.Min(Bands - 2, (int)at);
            float band = _band[b0] + ((_band[b0 + 1] - _band[b0]) * (at - b0));
            float centre = MathF.Exp(-(xn * xn) / (2 * .22f * .22f));
            float reach = (.13f + (.80f * MathF.Pow(band, 1.2f) * (.6f + (.4f * level))) + (.25f * (bass + pulse))) * (float)height;
            _auraHeight[x] = Math.Max(1, reach);
            int hue = (int)(xn * (PaletteSize - 1));
            float glow = .38f + (.65f * band) + (.45f * pulse);
            _auraR[x] = PaletteR[hue] * glow;
            _auraG[x] = PaletteG[hue] * glow;
            _auraB[x] = PaletteB[hue] * glow;
        }

        // neighbouring bands can differ a lot; smooth the ridge into rolling light
        int radius = Math.Max(2, w / 70);
        foreach (var row in new[] { _auraHeight, _auraR, _auraG, _auraB })
        {
            // three box passes come close to a gaussian
            for (int pass = 0; pass < 3; pass++)
            {
                BoxBlur(row, _auraScratch, radius);
                Array.Copy(_auraScratch, row, w);
            }
        }
    }

    // Blurs src into dst along x, by a running sum, clamping at the ends.
    private static void BoxBlur(float[] src, float[] dst, int radius)
    {
        int n = src.Length;
        float sum = 0, inv = 1f / ((radius * 2) + 1);
        for (int k = -radius; k <= radius; k++) sum += src[Math.Clamp(k, 0, n - 1)];
        for (int x = 0; x < n; x++)
        {
            dst[x] = sum * inv;
            sum += src[Math.Min(n - 1, x + radius + 1)] - src[Math.Max(0, x - radius)];
        }
    }

    private void Present()
    {
        float fade = (float)Math.Clamp(_fade, 0, 1);
        var (r, g, b, pixels) = (_r, _g, _b, _pixels);
        var (height, ar, ag, ab) = (_auraHeight, _auraR, _auraG, _auraB);
        int w = _w, h = _h;
        Parallel.For(0, h, y =>
        {
            float above = h - y - .5f;
            // everything thins out towards the top, so nothing ends on the strip's edge
            float top = Math.Min(1, (y + .5f) / (h * .45f));
            top *= top * (3 - (2 * top)) * fade;
            for (int x = 0, i = y * w; x < w; x++, i++)
            {
                // a soft column of light, brightest on the edge and fading smoothly upwards
                float u = above / height[x];
                float aura = u < 3 ? MathF.Exp(-u * u * 1.6f) : 0;
                float vr = Knee(r[i] + (ar[x] * aura)) * top;
                float vg = Knee(g[i] + (ag[x] * aura)) * top;
                float vb = Knee(b[i] + (ab[x] * aura)) * top;
                float a = Math.Max(vr, Math.Max(vg, vb));
                pixels[i] = ((int)(a * 255) << 24) | ((int)(vr * 255) << 16) | ((int)(vg * 255) << 8) | (int)(vb * 255);
            }
        });

        _bitmap!.WritePixels(new Int32Rect(0, 0, _w, _h), _pixels, _w * 4, 0);
    }

    // soft knee, so piled-up light rolls into white instead of clipping flat
    private static float Knee(float v) => v <= 0 ? 0 : v < .7f ? v : Math.Min(1, .7f + (.3f * (1 - MathF.Exp(-(v - .7f) * 3))));

    // ---- the wave ---------------------------------------------------------------------------------

    private void AllocateWave()
    {
        // one grain every ~1.2 DIP of width
        int n = Math.Clamp((int)(_capsule.Width / 1.2 * (Performance.Level == PerformanceLevel.Lite ? .5 : 1)), 60, 2400);
        _waveCount = n;
        _wx = new float[n];
        _wy = new float[n];
        _wvx = new float[n];
        _wvy = new float[n];
        _wPhase = new float[n];
        for (int i = 0; i < n; i++)
        {
            Home(i, out _wx[i], out _wy[i]);
            _wPhase[i] = (float)(_random.NextDouble() * 100);
        }
    }

    // Where grain i belongs on its wave right now.
    private void Home(int i, out float hx, out float hy)
    {
        int n = _waveCount, main = n * 2 / 3;
        bool second = i >= main;
        float u = second ? (i - main) / (float)Math.Max(1, n - main - 1) : i / (float)Math.Max(1, main - 1);
        float width = (float)_capsule.Width, height = (float)_capsule.Height;
        // about one long swell per 300 DIP, however wide the screen
        float x = u * width / 12;
        float phase = (float)_wavePhase, y = 0;
        for (int k = 0; k < 4; k++)
        {
            y += _waveAmp[k] * (second
                ? MathF.Sin((x * WaveFrequency[k]) - (phase * WaveSpeed[k] * .7f) + (k * 2.1f))
                : MathF.Sin((x * WaveFrequency[k]) + (phase * WaveSpeed[k]) + k));
        }

        hx = u * width;
        hy = Math.Clamp((height * .6f) + (y * height * .38f * (second ? .55f : 1)), 5, height - 3);
    }

    private void MoveWave(double dt, bool live)
    {
        if (_waveCount == 0) return;
        float Avg(int a, int b)
        {
            float sum = 0;
            for (int k = a; k <= b; k++) sum += _band[k];
            return sum / (b - a + 1);
        }

        // the same weights as the bands fall off: lows swing widest
        float gain = live ? 1 : 0;
        _waveAmp[0] = Avg(0, 3) * gain;
        _waveAmp[1] = Avg(4, 7) * .8f * gain;
        _waveAmp[2] = Avg(8, 16) * .7f * gain;
        _waveAmp[3] = Avg(17, 23) * .6f * gain;
        // no sound, no flow
        _wavePhase += dt * _level * 1.6;

        float d = (float)dt;
        for (int i = 0; i < _waveCount; i++)
        {
            Home(i, out float hx, out float hy);
            // a stiff spring, nearly critically damped: back home in about a tenth of a second
            _wvx[i] += ((SpringStiffness * (hx - _wx[i])) - (SpringDamping * _wvx[i])) * d;
            _wvy[i] += ((SpringStiffness * (hy - _wy[i])) - (SpringDamping * _wvy[i])) * d;
            _wx[i] += _wvx[i] * d;
            _wy[i] += _wvy[i] * d;
        }
    }

    // A beat throws the grains off their wave; the springs pull them back.
    private void Scatter(float strength)
    {
        for (int i = 0; i < _waveCount; i++)
        {
            double angle = _random.NextDouble() * Math.PI * 2, speed = strength * 400 * _random.NextDouble();
            _wvx[i] += (float)(Math.Cos(angle) * speed);
            _wvy[i] += (float)(Math.Sin(angle) * speed);
        }
    }

    private void SplatWave()
    {
        float scale = (float)_scale, t = (float)_time, fade = (float)Math.Min(1, _fade * 1.5);
        int main = _waveCount * 2 / 3;
        float energy = .45f + (.55f * (float)_level);
        for (int i = 0; i < _waveCount; i++)
        {
            bool second = i >= main;
            float twinkle = .7f + (.3f * MathF.Sin(_wPhase[i] + (t * (2.5f + (_wPhase[i] % 3)))));
            // a grain thrown far from home glows a little brighter
            float speed = MathF.Min(1, (MathF.Abs(_wvx[i]) + MathF.Abs(_wvy[i])) / 300);
            float b = (second ? .8f : 1.4f) * energy * twinkle * (1 + speed) * fade;
            float xn = MathF.Abs((_wx[i] / (float)_capsule.Width) - .5f) * 2;
            int hue = Math.Clamp((int)((second ? .55f + (.45f * xn) : xn * .6f) * (PaletteSize - 1)), 0, PaletteSize - 1);
            Sprite(_wx[i] * scale, _wy[i] * scale, second ? 1 : 0, PaletteR[hue] * b, PaletteG[hue] * b, PaletteB[hue] * b);
        }
    }

    // ---- the dust ---------------------------------------------------------------------------------

    private void Emit(double dt, SpectrumFrame f)
    {
        double width = _capsule.Width, bottom = _capsule.Height;
        double cx = width / 2;
        double density = Math.Clamp(width / 1600, .4, 1.6) * (Performance.Level == PerformanceLevel.Lite ? .5 : 1);

        // each band sends dust up from its own place along the edge, mirrored about the middle
        for (int b = 0; b < Bands; b++)
        {
            double e = _band[b];
            _emitCarry += (Wave ? (e * e * 7) + .2 : (e * e * 16) + .4) * density * dt;
            while (_emitCarry >= 1)
            {
                _emitCarry -= 1;
                double xn = (b + _random.NextDouble()) / Bands * .5;
                double x = cx + ((_random.Next(2) == 0 ? -1 : 1) * xn * width);
                double depth = Math.Pow(_random.NextDouble(), 1.8);
                // near dust drifts faster, far dust barely moves: parallax
                double speed = (6 + (22 * e)) * (.5 + depth);
                Spawn(x, bottom * (.55 + (.45 * _random.NextDouble())), Gaussian() * 3, -speed,
                    2.2 + (2 * _random.NextDouble()), (.35 + (.45 * e)) * (1 - (.55 * depth)), depth, Hue(xn * 2));
            }
        }

        if (f.Kick > 0)
        {
            // a soft puff of fine dust from the middle
            int n = (int)((10 + (25 * f.Kick)) * density);
            for (int i = 0; i < n; i++)
            {
                double x = cx + (Gaussian() * width * .12);
                double depth = _random.NextDouble() * .5;
                Spawn(x, bottom - (_random.NextDouble() * 6), Gaussian() * 12, -(20 + (35 * f.Kick * _random.NextDouble())),
                    1.4 + _random.NextDouble(), .6 + (.3 * f.Kick), depth, Hue(Math.Abs(x - cx) / cx));
            }
        }

        if (f.Hat > 0)
        {
            // a few grains flare up
            int flares = 1 + (int)(3 * f.Hat);
            for (int k = 0; k < flares && _count > 0; k++)
            {
                int p = _random.Next(_count);
                if (_depth[p] < .5f) _flare[p] = Math.Max(_flare[p], f.Hat);
            }
        }
    }

    private byte Hue(double xn) => (byte)Math.Clamp((int)((xn * (PaletteSize - 4)) + _random.Next(4)), 0, PaletteSize - 1);

    // A kick lifts the dust already in the air, the near grains most.
    private void Lift(float kick)
    {
        for (int p = 0; p < _count; p++) _vy[p] -= kick * (8 + (14 * _depth[p]));
    }

    private void Spawn(double x, double y, double vx, double vy, double life, double bright, double depth, byte hue)
    {
        if (_count >= MaxParticles) return;
        int p = _count++;
        _x[p] = (float)x;
        _y[p] = (float)y;
        _vx[p] = (float)vx;
        _vy[p] = (float)vy;
        _age[p] = 0;
        _life[p] = (float)life;
        _bright[p] = (float)bright;
        _phase[p] = (float)(_random.NextDouble() * 100);
        _depth[p] = (float)depth;
        _flare[p] = 0;
        _hue[p] = hue;
    }

    private void Move(double dt)
    {
        float d = (float)dt, t = (float)_time;
        // the lift a kick gave settles back to a slow drift
        float drag = (float)Math.Exp(-dt * 1.4), flareDecay = (float)Math.Exp(-dt / .18);
        float width = (float)_capsule.Width, height = (float)_capsule.Height;
        for (int p = 0; p < _count;)
        {
            _age[p] += d;
            // a slow sway, so the dust meanders instead of rising straight
            _vx[p] += MathF.Sin((_y[p] * .05f) + (t * .5f) + (_phase[p] % 6.3f)) * 5 * d;
            _vy[p] -= (2 + (4 * _depth[p])) * d;
            _vx[p] *= drag;
            _vy[p] *= drag;
            _x[p] += _vx[p] * d;
            _y[p] += _vy[p] * d;
            _flare[p] *= flareDecay;
            if (_age[p] >= _life[p] || _y[p] < -6 || _y[p] > height + 6 || _x[p] < -8 || _x[p] > width + 8)
            {
                Remove(p);
                continue;
            }

            p++;
        }
    }

    private void Remove(int p)
    {
        int last = --_count;
        _x[p] = _x[last];
        _y[p] = _y[last];
        _vx[p] = _vx[last];
        _vy[p] = _vy[last];
        _age[p] = _age[last];
        _life[p] = _life[last];
        _bright[p] = _bright[last];
        _phase[p] = _phase[last];
        _depth[p] = _depth[last];
        _flare[p] = _flare[last];
        _hue[p] = _hue[last];
    }

    private double Gaussian()
    {
        double u = 1 - _random.NextDouble(), v = _random.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v);
    }

    // From the tint in the middle to a hue 50° along at the sides; white stays white.
    private static void EnsurePalette()
    {
        if (_paletteTint == GlassSettings.HologramTint) return;
        _paletteTint = GlassSettings.HologramTint;
        var primary = HologramVisualizer.Tint(BasePrimary);
        for (int i = 0; i < PaletteSize; i++)
        {
            double t = i / (double)(PaletteSize - 1);
            var (r, g, b) = RotateHue(primary.R / 255.0, primary.G / 255.0, primary.B / 255.0, 50 * t);
            PaletteR[i] = (float)Math.Clamp(r, 0, 1);
            PaletteG[i] = (float)Math.Clamp(g, 0, 1);
            PaletteB[i] = (float)Math.Clamp(b, 0, 1);
        }
    }

    private static (double R, double G, double B) RotateHue(double r, double g, double b, double degrees)
    {
        double a = degrees * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a), k = 1 / 3.0, q = Math.Sqrt(k);
        double m0 = c + ((1 - c) * k), m1 = (k * (1 - c)) - (q * s), m2 = (k * (1 - c)) + (q * s);
        return ((r * m0) + (g * m1) + (b * m2), (r * m2) + (g * m0) + (b * m1), (r * m1) + (g * m2) + (b * m0));
    }

    // ---- lifecycle --------------------------------------------------------------------------------

    private void Hook()
    {
        if (_hooked) return;
        CompositionTarget.Rendering += OnRendering;
        _hooked = true;
    }

    private void Unhook()
    {
        if (!_hooked) return;
        CompositionTarget.Rendering -= OnRendering;
        _hooked = false;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        double now = e is RenderingEventArgs args ? args.RenderingTime.TotalSeconds : _clock.Elapsed.TotalSeconds;
        if (now <= _lastFrameSeconds) return;
        double dt = _lastFrameSeconds < 0 ? 1.0 / 60 : Math.Clamp(now - _lastFrameSeconds, .001, .1);
        _lastFrameSeconds = now;

        // See VisualizerPacing: the strip shares the render thread with the dock.
        _sinceRepaint += dt;
        if (_sinceRepaint < VisualizerPacing.RepaintInterval) return;
        dt = Math.Min(_sinceRepaint, .1);
        _sinceRepaint = 0;

        if (_active)
        {
            _fade = Math.Min(1, _fade + (dt / FadeInSeconds));
            _spectrum ??= new AudioSpectrum();
            _spectrum.Read(_frame, dt);
        }
        else
        {
            _fade = Math.Max(0, _fade - (dt / FadeOutSeconds));
            if (_fade <= 0)
            {
                _count = 0;
                _wavePhase = 0;
                Array.Clear(_waveAmp);
                _bass = _level = _pulse = _shimmer = 0;
                Array.Clear(_band);
                Array.Clear(_r);
                Array.Clear(_g);
                Array.Clear(_b);
                _lastFrameSeconds = -1;
                Unhook();
                InvalidateVisual();
                return;
            }
        }

        Step(dt);
        VisualizerPacing.MarkRepaint();
        InvalidateVisual();
    }
}
