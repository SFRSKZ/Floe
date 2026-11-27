using Floe.App.Effects.Gpu;
using Color = System.Windows.Media.Color;

namespace Floe.App.Effects.Gpu.BladeRunner;

/// <summary>
/// The shared frame of the twelve looks taken from the Blade Runner 2099 title sequence. Each look writes only
/// one HLSL function, <c>float3 Scene(float2 p)</c>: the linear light at pixel <c>p</c> (y down, as in the app).
/// This class gives them the same clock, beat, dock gap and film finish:
/// <list type="bullet">
/// <item>the clock only runs while there is sound, so in silence the picture stands still as a photo;</item>
/// <item>the feedback pass takes four turned-grid samples and may keep a fading trail (<c>Persist</c>);</item>
/// <item>the show pass tones it (ACES), adds clock-driven grain, fades the top edge; the scene runs on behind the dock.</item>
/// </list>
/// Port note: production y is down too, so nothing flips; only the namespace and SceneDoc change.
/// </summary>
public abstract class BrScene : FeedbackGpuScene
{
    private const int ConstantFloats = 76;
    private readonly float _persist;
    private readonly Color[] _colours;
    private readonly float[] _bands = new float[24];
    private float _level, _bass, _mid, _treble, _clock, _kickAge = 9, _snareAge = 9, _seed = 1;

    protected BrScene(string scene, float persist, params Color[] colours)
        : base(ConstantFloats, Header + scene + FeedbackMain, Header + "float3 Scene(float2 p) { return 0; }\n" + ShowMain)
    {
        _persist = persist;
        _colours = colours;
    }

    /// <summary>Per-look numbers, the cbuffer's <c>P</c>.</summary>
    protected virtual (float, float, float, float) Params => (0, 0, 0, 0);

    /// <summary>
    /// The film finish of the show pass, the cbuffer's <c>Film</c>: bloom amount (light above the knee spills out),
    /// anamorphic streak (the same spill stretched sideways, as the title cards' lens does), chromatic shift in
    /// pixels, and grain amount. Crisp cores stay crisp; only the light above the knee spreads.
    /// </summary>
    protected virtual (float Bloom, float Streak, float Chroma, float Grain) Film => (.4f, .3f, 1.2f, .06f);

    /// <summary>
    /// The film material, the cbuffer's <c>Mat</c>, the micro detail the title sequence has at screen-pixel scale:
    /// edge erosion in pixels (rows slip and crumble), the display grille (fine vertical lines a few pixels apart, as if
    /// the light were filmed off a screen), emulsion mottle with dark burns, and dust specks. The shapes stay the look's;
    /// this is the surface they are printed on.
    /// </summary>
    protected virtual (float Erode, float Grille, float Mottle, float Dust) Material => (1.5f, .3f, .35f, .5f);

    /// <summary>
    /// More of the material, the cbuffer's <c>Mat2</c>: row slip in pixels (the ragged, torn ends of horizontal
    /// strokes), the row mesh under the grille (0..1), burn amount (0..1), and the grille pitch in pixels.
    /// </summary>
    protected virtual (float Slip, float Mesh, float Burn, float Pitch) Material2 => (0f, .35f, .5f, 3.5f);

    protected override void Advance(GpuFrame frame)
    {
        float dt = Math.Clamp(frame.Dt, 0f, .1f);
        var a = frame.Audio;
        static float Ease(float cur, float target, float dt) =>
            cur + ((target - cur) * (1 - MathF.Exp(-dt * (target > cur ? 18f : 4.5f))));
        _level = Ease(_level, a.Level, dt);
        _bass = Ease(_bass, a.Bass, dt);
        _mid = Ease(_mid, a.Mid, dt);
        _treble = Ease(_treble, a.Treble, dt);
        for (int i = 0; i < 24; i++) _bands[i] = Ease(_bands[i], Math.Clamp(a.Bands[i * a.Bands.Length / 24], 0, 1), dt);

        // everything that moves rides this clock: no sound, no motion
        if (a.Level > .012f) _clock = (_clock + (dt * (.2f + (.8f * _level) + (.6f * _bass)))) % 5000f;
        _kickAge += dt;
        _snareAge += dt;
        if (Beat.Hit)
        {
            _seed = (_seed + 1) % 997;
            if (Beat.Snare) _snareAge = 0;
            else _kickAge = 0;
        }
    }

    protected override void Fill(GpuFrame frame)
    {
        var d = Data;
        d[0] = frame.Width;
        d[1] = frame.Height;
        d[2] = frame.PixelsPerDip;
        d[3] = Math.Clamp(frame.Fade, 0, 1);
        d[4] = frame.DockLeft;
        d[5] = frame.DockRight;
        d[6] = _level;
        d[7] = _bass;
        d[8] = _mid;
        d[9] = _treble;
        d[10] = _clock;
        d[11] = Beat.Pulse;
        d[12] = _kickAge;
        d[13] = _snareAge;
        d[14] = _seed;
        d[15] = _persist;
        var (p0, p1, p2, p3) = Params;
        d[16] = p0; d[17] = p1; d[18] = p2; d[19] = p3;
        for (int i = 0; i < 24; i++) d[20 + i] = _bands[i];
        for (int i = 0; i < 5; i++) Put(44 + (i * 4), i < _colours.Length ? _colours[i] : Color.FromRgb(255, 255, 255));
        var film = Film;
        d[64] = film.Bloom; d[65] = film.Streak; d[66] = film.Chroma; d[67] = film.Grain;
        var mat = Material;
        d[68] = mat.Erode; d[69] = mat.Grille; d[70] = mat.Mottle; d[71] = mat.Dust;
        var m2 = Material2;
        d[72] = m2.Slip; d[73] = m2.Mesh; d[74] = m2.Burn; d[75] = m2.Pitch;
    }

    protected static Color Hex(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private const string Header = """
        cbuffer BladeStrip : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float DockL;
            float DockR;
            float Level;
            float Bass;
            float Mid;
            float Treble;
            float Clock;
            float Pulse;
            float KickAge;
            float SnareAge;
            float Seed;
            float Persist;
            float4 P;
            float4 Bands[6];
            float4 C0;
            float4 C1;
            float4 C2;
            float4 C3;
            float4 C4;
            float4 Film;
            float4 Mat;
            float4 Mat2;
        };

        """ + ShaderHelpers + """

        static const float PI = 3.14159265;

        float3 Lin(float4 c) { return pow(abs(c.rgb), 2.2); }
        float Kick() { return exp(-KickAge * 5.0); }
        float Snr() { return exp(-SnareAge * 7.0); }
        float BandW(float u) { return Band(saturate(u * .92 + .04)); }
        float Hash3(float a, float b, float c) { return Hash(float2(a + c * 17.13, b - c * 5.71)); }

        // x: 0 at the dock edge, 1 at the screen edge; y: -1 left wing, +1 right, 0 under the dock; z: pixels from the dock edge
        float3 Wing(float x)
        {
            // the wings meet at the dock's centre, so the scene runs on behind the dock instead of stopping at its edges
            float l = DockL < DockR ? .5 * (DockL + DockR) * Size.x : .5 * Size.x, r = l;
            if (x < l) return float3(saturate((l - x) / max(l, 1.0)), -1, l - x);
            return float3(saturate((x - r) / max(Size.x - r, 1.0)), 1, x - r);
        }

        // the width of one wing in pixels
        float WingPx(float side)
        {
            float l = DockL < DockR ? .5 * (DockL + DockR) * Size.x : .5 * Size.x, r = l;
            return side < 0 ? l : Size.x - r;
        }

        float Edge(float d, float w) { return saturate(.5 - (d - w)); }

        float3 Aces(float3 x) { return saturate((x * (2.51 * x + .03)) / (x * (2.43 * x + .59) + .14)); }

        """;

    private const string FeedbackMain = """

        // worn edges: a slow meander plus a fine nudge, so no edge is a ruler; and torn rows, where each 2-px row slips
        // sideways (the ragged ends of horizontal strokes)
        float2 Tear(float2 p)
        {
            float2 slow = float2(Noise(p * float2(.045, .08) + float2(Clock * .5, 0)), Noise(p * float2(.08, .045) + 13.1 - Clock * .4)) - .5;
            float2 fine = float2(Noise(p * .22 + Clock * 1.3), Noise(p * .22 + 31.7 - Clock)) - .5;
            float row = floor(p.y / 2.0);
            float tick = floor(Clock * 9.0);
            float slip = (Hash(float2(row, tick)) - .5) + (Noise(float2(p.x * .015 + tick * .3, row * .7)) - .5) * 2.0;
            return p + (slow * 2.6 + fine * .9) * Mat.x + float2(slip * Mat2.x, 0);
        }

        float3 Emulsion(float2 p, float3 c)
        {
            // mottle: the light is uneven inside every surface, in two sizes
            float m1 = Noise(p * float2(.09, .07) + float2(Clock * .6, 0));
            float m2 = Noise(p * float2(.42, .3) + 9.1 + Clock * .25);
            c *= lerp(1.0, .4 + 1.0 * (m1 * .65 + m2 * .35), Mat.z);
            // burns: dark rusty patches with a crusty rim
            float bn = Noise(p * float2(.011, .028) + float2(Clock * .12, 3.7));
            float burn = smoothstep(.6, .78, bn);
            float crust = smoothstep(.55, .62, bn) * (1 - burn) * Noise(p * .55);
            c *= 1 - Mat2.z * (.9 * burn * (.55 + .45 * Noise(p * .7)));
            c *= 1 + Mat2.z * crust * float3(.9, .35, -.2);
            // hue drift across the film
            float hue = Noise(p * .02 + 5.3) - .5;
            c *= 1 + float3(hue, -hue * .3, -hue) * Mat.z * .6;
            // dust: bright specks on the dark, dark specks on the bright
            float2 cell = floor(p / 1.5);
            float tick = floor(Clock * 14.0);
            float lum = max(c.r, max(c.g, c.b));
            float bright = step(1 - .002 * Mat.w, Hash(cell + tick * 17.3));
            float dark = step(1 - .006 * Mat.w, Hash(cell * 1.37 + 3.1 + tick * 5.9));
            c += bright * lerp(.35, .9, saturate(lum)) * float3(1, .94, .82);
            c *= 1 - dark * .85 * saturate(lum * 3);
            return max(c, 0);
        }

        float4 main(float4 pos : SV_Position) : SV_Target
        {
            float2 p = Tear(pos.xy);
            // two taps are enough now that the shapes are drawn with soft edges; four cost twice as much
            float3 c = Scene(p + float2(-.25, -.25)) + Scene(p + float2(.25, .25));
            c = Emulsion(pos.xy, c * .5);
            float3 last = Last.Load(int3(pos.xy, 0)).rgb;
            return float4(max(c, last * Persist), 1);
        }
        """;

    private const string ShowMain = """

        float4 main(float4 pos : SV_Position) : SV_Target
        {
            float2 inv = 1.0 / Size;
            float2 uv = pos.xy * inv;
            // a slight lens colour split, stronger toward the screen edges
            float edgeK = abs(uv.x - .5) * 2.0;
            float2 ca = float2(Film.z * edgeK, 0) * inv;
            float3 h;
            h.r = Last.SampleLevel(Linear, uv + ca, 0).r;
            h.g = Last.Load(int3(pos.xy, 0)).g;
            h.b = Last.SampleLevel(Linear, uv - ca, 0).b;
            h = max(h, 0);

            // bloom: only the light above the knee spills, in rings of growing radius; the streak is the same spill
            // stretched along x, the anamorphic flare of the title cards
            float3 bloom = 0, streak = 0;
            float wsum = 0;
            // each pixel turns and stretches its tap rings by its own random amount, so a bright line blurs into a soft
            // glow instead of leaving ghost copies of itself at the ring radii
            float jr = Hash(pos.xy * .731 + 4.7), ja = Hash(pos.yx * 1.13 + 9.2) * .7853982;
            [unroll] for (int i = 1; i <= 5; i++)
            {
                float ri = i - .3 + .6 * jr;
                float r = ri * ri * 1.6;
                float w = 1.0 / i;
                [unroll] for (int k = 0; k < 8; k++)
                {
                    float a = k * .7853982 + i * .4 + ja;
                    float2 o = float2(cos(a), sin(a)) * r;
                    float3 s = Last.SampleLevel(Linear, uv + o * inv, 0).rgb;
                    bloom += max(s - .55, 0) * w;
                }
                wsum += w;
            }
            // the streak: a dense, smoothly fading run of taps, so it reads as one flare and not as copies
            float ssum = 0;
            [unroll] for (int j = 1; j <= 14; j++)
            {
                float jj = j - .5 + jr;
                float off = jj * jj * .8 + jj * 1.5;     // jittered per pixel, so a vertical edge smears instead of echoing
                float ws = exp(-j * .22);
                float3 sl = Last.SampleLevel(Linear, uv + float2(off, 0) * inv, 0).rgb;
                float3 sr = Last.SampleLevel(Linear, uv - float2(off, 0) * inv, 0).rgb;
                streak += (max(sl - .8, 0) + max(sr - .8, 0)) * ws;
                ssum += ws;
            }
            h += bloom / (wsum * 8.0) * Film.x * 1.6;
            h += streak / (ssum * 2.0) * Film.y * 1.5 * float3(.75, .85, 1.15);
            h *= Fade;

            // the display grille: fine vertical lines with a slight RGB triad, and a fainter row mesh
            float gx = pos.x / max(Mat2.w, 2.0);
            float3 tri = .5 - .5 * cos(6.2831853 * (gx - float3(-.12, 0, .12)));
            float gy = .5 - .5 * cos(6.2831853 * pos.y / 4.0);
            float3 grille = (1 - Mat.y * tri) * (1 - Mat.y * Mat2.y * gy);
            h *= grille / ((1 - Mat.y * .5) * (1 - Mat.y * Mat2.y * .5));

            float3 t = pow(Aces(h * 1.15), 1.0 / 2.2);
            float grain = Hash(pos.xy + frac(Clock * 7.13) * 917.0) - .5;
            // light film grain: mostly on the light, a fine trace in the darks too, so the black is never dead flat
            float grain2 = Hash(pos.yx * 1.37 + frac(Clock * 5.31) * 613.0) - .5;
            t = saturate(t * (1 + grain * Film.w * 1.6) + (grain2 * .6 + grain * .4) * Film.w * .3);
            float top = smoothstep(0.0, .16, pos.y / Size.y);
            t *= top;
            float a = saturate(max(t.r, max(t.g, t.b)));
            return float4(t, a);
        }
        """;
}
