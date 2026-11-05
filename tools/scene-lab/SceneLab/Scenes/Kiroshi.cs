using Floe.App.Effects.Gpu;
using Color = System.Windows.Media.Color;

namespace SceneLab.Scenes;

/// <summary>
/// Kiroshi optic HUD. ONE IDEA: the strip is the targeting overlay inside a Kiroshi eye implant — the top of the
/// spectrum is a list of candidate targets, corner brackets lock onto them, a kick throws a scan bar across the
/// strip, a snare rolls the telemetry digits and turns the HUD red, hats drop sensor crumbs. A fixed range ruler,
/// a dock-edge bracket pair and six glass target plates keep the scanning screen populated even with no music.
/// <para>
/// Rejected faults: not a static illustration (the scan bar, the bracket slam and the search cursor are events; the
/// parked HUD is only the rest pose), not dock-blind (every target, bracket, digit, ruler and the dock-edge pair
/// sit outside the middle 46% — the scan bar crosses the dock but is a passing event, never the reading), not
/// unmeasured (bass thickens the strokes, kick fires the sweep and slams the brackets, snare rolls digits, hats drop
/// crumbs), no gratuitous decoration (no bloom, no blur, no grain: a bright core over a 1 px near-black edge only).
/// </para>
/// <para>
/// Port notes: the app renderer uses the same y-down SV_Position, so nothing is flipped. Target geometry is kept as
/// fractions of Size so another strip width still composes; stroke widths are in DIPs. The range ladder sits under
/// the hero boxes and the edge rulers hug all four strip borders — a y-up port must mirror the y fractions
/// (v -> 1-v). The feedback pass is deliberately empty: the HUD is analytic, and the only thing still moving in
/// silence is a 7% standby cool-down of the locked brackets, which settles within a second.
/// </para>
/// </summary>
[SceneDoc("Kiroshi optic HUD: top bands become targets, kick sweeps a scan bar and slams the brackets, snare rolls telemetry digits")]
public sealed class Kiroshi : FeedbackGpuScene
{
    private const int ConstantFloats = 124; // 20 scalars + 2 glitch quads + 6 targets + 6 states + 24 bands + 6 colours

    private static readonly Color Core = Color.FromRgb(0xFF, 0xFF, 0xF4);
    private static readonly Color Hot = Color.FromRgb(0xFC, 0xEE, 0x0A);
    private static readonly Color Cold = Color.FromRgb(0x02, 0xD7, 0xF2);
    private static readonly Color Alarm = Color.FromRgb(0xFF, 0x00, 0x3C);
    private static readonly Color Dim = Color.FromRgb(0x11, 0x6C, 0x7E);
    private static readonly Color Ink = Color.FromRgb(0x03, 0x05, 0x0A);

    // x, y, half width, half height as fractions of the strip. The two hero boxes flank the dock, the mid pair
    // sits under them, the small pair pins the far ends; the middle 46% stays empty by design.
    private static readonly float[] Geom =
    {
        0.1224f, 0.300f, 0.0450f, 0.220f,
        0.8776f, 0.300f, 0.0450f, 0.220f,
        0.2292f, 0.760f, 0.0240f, 0.120f,
        0.7708f, 0.760f, 0.0240f, 0.120f,
        0.0391f, 0.790f, 0.0140f, 0.085f,
        0.9609f, 0.790f, 0.0140f, 0.085f,
    };

    // which analyser band feeds each target: the hero boxes read the very top of the spectrum
    private static readonly int[] BandIndex = { 20, 22, 16, 18, 12, 14 };

    private readonly float[] _conf = new float[6];
    private readonly float[] _locked = new float[6];
    private readonly int[] _id = { 7, 42, 83, 19, 6, 50 };

    private readonly Random _random = new(2077);
    private float _prevKick, _prevSnare, _prevHat;
    private float _scanAge = 9, _scanDir = 1, _scanHot, _flash, _warn, _glitchLife;
    private float _g0x, _g0y, _g0s, _g1x, _g1y, _g1s;
    private float _searchT = .18f, _idle;

    public Kiroshi() : base(ConstantFloats, Shared + FeedbackPs, Shared + ShowPs)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        float dt = frame.Dt;
        var audio = frame.Audio;
        bool silent = audio.Level <= 0.004f;
        float sdt = silent ? 0 : dt;

        bool kickOn = audio.Kick > .45f && _prevKick <= .45f;
        bool snareOn = audio.Snare > .40f && _prevSnare <= .40f;
        bool hatOn = audio.Hat > .50f && _prevHat <= .50f;
        _prevKick = audio.Kick;
        _prevSnare = audio.Snare;
        _prevHat = audio.Hat;

        // a kick fires the scan bar; alternate the direction so it sweeps back and forth
        if (kickOn)
        {
            _scanAge = 0;
            _scanDir = -_scanDir;
            _flash = 1;
        }

        _scanAge += dt;
        _scanHot = MathF.Max(_scanHot * MathF.Exp(-dt * 3.4f), kickOn ? 1 : 0);
        if (_scanHot < .02f) _scanHot = 0;

        _flash *= MathF.Exp(-dt * 8f);
        if (_flash < .04f) _flash = 0;

        // a snare re-rolls the telemetry and flashes the whole HUD red
        if (snareOn)
        {
            _warn = 1;
            for (int i = 0; i < _id.Length; i++) _id[i] = (_id[i] + 3 + _random.Next(97)) % 100;
        }

        _warn *= MathF.Exp(-dt * 4.5f);
        if (_warn < .04f) _warn = 0;

        // hats drop little square sensor crumbs on the open ends of the strip
        if (hatOn)
        {
            SpawnCrumb(0);
            SpawnCrumb(1);
            _glitchLife = 1;
        }

        _glitchLife *= MathF.Exp(-dt * 15f);
        if (_glitchLife < .03f) _glitchLife = 0;

        // the search cursor only crawls while the music has content, and stops dead in silence
        if (!silent) _searchT += dt * (.16f + (.9f * audio.Mid));

        // standby: when the track dies the glass plates dim and the locked brackets cool, then everything rests
        if (silent) _idle = MathF.Min(1, _idle + (dt / 1.1f));
        else _idle = MathF.Max(0, _idle - (dt * 6));

        // confidence is an instant attack with a slow fall: the mock's top bands only spike on a hat, so an
        // average would never lock. Frozen in silence so the locked skeleton stays exactly where it was.
        for (int i = 0; i < 6; i++)
        {
            float band = audio.Bands[BandIndex[i]];
            if (sdt > 0) _conf[i] = MathF.Max(band, _conf[i] * MathF.Exp(-sdt * 2.2f));
            // a lock latches: once acquired it holds through the breakdown, so silence keeps the locked silhouette
            if (_conf[i] > .20f) _locked[i] = 1;
            else if (_conf[i] < .05f) _locked[i] = 0;
        }
    }

    private void SpawnCrumb(int slot)
    {
        bool left = _random.Next(2) == 0;
        float x = left
            ? 40 + ((float)_random.NextDouble() * 940)
            : 2900 + ((float)_random.NextDouble() * 900);
        float y = 12 + ((float)_random.NextDouble() * 100);
        float s = 5 + ((float)_random.NextDouble() * 6);
        if (slot == 0) { _g0x = x; _g0y = y; _g0s = s; }
        else { _g1x = x; _g1y = y; _g1s = s; }
    }

    protected override void Fill(GpuFrame frame)
    {
        var audio = frame.Audio;
        var d = Data;
        float w = frame.Width, h = frame.Height;
        d[0] = w;
        d[1] = h;
        d[2] = frame.PixelsPerDip;
        d[3] = Math.Clamp(frame.Fade, 0, 1);
        d[4] = frame.Time;
        d[5] = audio.Level;
        d[6] = audio.Bass;
        d[7] = audio.Mid;
        d[8] = audio.Treble;
        d[9] = audio.Kick;
        d[10] = audio.Snare;
        d[11] = audio.Hat;
        d[12] = _scanAge;
        d[13] = _scanDir;
        d[14] = _scanHot;
        d[15] = _flash;
        d[16] = _warn;
        d[17] = _glitchLife;
        d[18] = .88f - (.03f * _idle);                  // glass plate opacity, settles in standby
        d[19] = (_searchT - MathF.Floor(_searchT)) * w;  // search cursor x, frozen in silence

        d[20] = _g0x; d[21] = _g0y; d[22] = _g0s; d[23] = 0;
        d[24] = _g1x; d[25] = _g1y; d[26] = _g1s; d[27] = 0;

        for (int i = 0; i < 6; i++)
        {
            d[28 + (i * 4)] = Geom[i * 4] * w;
            d[29 + (i * 4)] = Geom[(i * 4) + 1] * h;
            d[30 + (i * 4)] = Geom[(i * 4) + 2] * w;
            d[31 + (i * 4)] = Geom[(i * 4) + 3] * h;
            d[52 + (i * 4)] = _conf[i];
            d[53 + (i * 4)] = _locked[i];
            d[54 + (i * 4)] = _id[i];
            d[55 + (i * 4)] = 0;
        }

        PutBands(76, frame);
        Put(100, Core);
        Put(104, Hot);
        Put(108, Cold);
        Put(112, Alarm);
        Put(116, Dim);
        Put(120, Ink);
    }

    private const string Shared = """
        cbuffer Kiroshi : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float Time;
            float Level;
            float Bass;
            float Mid;
            float Treble;
            float Kick;
            float Snare;
            float Hat;
            float ScanAge;
            float ScanDir;
            float ScanHot;
            float Flash;
            float Warn;
            float GlitchLife;
            float Panel;
            float SearchX;
            float4 Glitch[2];
            float4 Tgt[6];
            float4 State[6];
            float4 Bands[6];
            float4 Core;
            float4 Hot;
            float4 Cold;
            float4 Alarm;
            float4 Dim;
            float4 Ink;
        };

        """ + ShaderHelpers;

    private const string FeedbackPs = """

        // The HUD is analytic: nothing is carried between frames. That is what keeps the silence frame still.
        float4 main(float4 position : SV_Position) : SV_Target
        {
            return float4(0, 0, 0, 0);
        }
        """;

    private const string ShowPs = """

        float SdSeg(float2 p, float2 a, float2 b)
        {
            float2 pa = p - a;
            float2 ba = b - a;
            float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-4));
            return length(pa - (ba * h));
        }

        float SdBox(float2 p, float2 c, float2 half)
        {
            float2 q = abs(p - c) - half;
            return length(max(q, 0)) + min(max(q.x, q.y), 0);
        }

        // a bright core over a dark 1 px edge: the only way this HUD stays readable over a bright wallpaper
        void Stroke(inout float3 col, inout float a, float d, float3 colour)
        {
            float core = step(d, 0);
            float ink = step(d, 1.1 * Dip);
            col = lerp(col, Ink.rgb, ink * (1 - core));
            a = max(a, ink * (1 - core));
            col = lerp(col, colour, core);
            a = max(a, core);
        }

        void Paint(inout float3 col, inout float a, float mask, float3 colour)
        {
            col = lerp(col, colour, mask);
            a = max(a, mask);
        }

        int DigitMask(int v)
        {
            if (v <= 0) return 63;   // 0
            if (v == 1) return 6;
            if (v == 2) return 91;
            if (v == 3) return 79;
            if (v == 4) return 102;
            if (v == 5) return 109;
            if (v == 6) return 125;
            if (v == 7) return 7;
            if (v == 8) return 127;
            return 111;              // 9
        }

        // one seven-segment digit, p relative to its top-left corner, h tall
        float DigitSd(float2 p, float h, int value)
        {
            float w = .62 * h;
            float t = max(1.5, h * .14);
            int mask = DigitMask(value);
            float d = 1e5;
            if ((mask & 1) != 0) d = min(d, SdSeg(p, float2(.16 * w, t * .5), float2(.84 * w, t * .5)) - (t * .5));
            if ((mask & 2) != 0) d = min(d, SdSeg(p, float2(w - (t * .5), .18 * h), float2(w - (t * .5), .46 * h)) - (t * .5));
            if ((mask & 4) != 0) d = min(d, SdSeg(p, float2(w - (t * .5), .54 * h), float2(w - (t * .5), .82 * h)) - (t * .5));
            if ((mask & 8) != 0) d = min(d, SdSeg(p, float2(.16 * w, h - (t * .5)), float2(.84 * w, h - (t * .5))) - (t * .5));
            if ((mask & 16) != 0) d = min(d, SdSeg(p, float2(t * .5, .54 * h), float2(t * .5, .82 * h)) - (t * .5));
            if ((mask & 32) != 0) d = min(d, SdSeg(p, float2(t * .5, .18 * h), float2(t * .5, .46 * h)) - (t * .5));
            if ((mask & 64) != 0) d = min(d, SdSeg(p, float2(.16 * w, .5 * h), float2(.84 * w, .5 * h)) - (t * .5));
            return d;
        }

        float Digits2Sd(float2 p, float2 origin, float h, int value)
        {
            float w = .62 * h;
            int tens = value / 10;
            int ones = value - (tens * 10);
            float a = DigitSd(p - origin, h, tens);
            float b = DigitSd(p - origin - float2(w + (.34 * h), 0), h, ones);
            return min(a, b);
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 p = position.xy;
            float3 col = 0;
            float a = 0;
            float3 accent = lerp(Dim.rgb, Cold.rgb, .45);

            // 0) the optic frame: brackets pinning all four strip corners
            {
                float inset = 8 * Dip;
                float arm = 22 * Dip;
                float d = 1e5;
                d = min(d, SdSeg(p, float2(inset, inset), float2(inset + arm, inset)));
                d = min(d, SdSeg(p, float2(inset, inset), float2(inset, inset + arm)));
                d = min(d, SdSeg(p, float2(Size.x - inset, inset), float2(Size.x - inset - arm, inset)));
                d = min(d, SdSeg(p, float2(Size.x - inset, inset), float2(Size.x - inset, inset + arm)));
                d = min(d, SdSeg(p, float2(inset, Size.y - inset), float2(inset + arm, Size.y - inset)));
                d = min(d, SdSeg(p, float2(inset, Size.y - inset), float2(inset, Size.y - inset - arm)));
                d = min(d, SdSeg(p, float2(Size.x - inset, Size.y - inset), float2(Size.x - inset - arm, Size.y - inset)));
                d = min(d, SdSeg(p, float2(Size.x - inset, Size.y - inset), float2(Size.x - inset, Size.y - inset - arm)));
                Stroke(col, a, d - (.8 * Dip), accent);
            }

            // 1) border rulers: a tick every 16 px, a long one every 64 px, so the black is a measuring screen
            {
                float per = 16 * Dip;
                float k = floor((p.x / per) + .5);
                float dx = abs(p.x - (k * per));
                float major = 1 - step(.5, fmod(abs(k), 4));
                float tall = lerp(2.6, 6.5, major) * Dip;
                float halfTick = lerp(.5, .85, major) * Dip;
                float top = step(dx, halfTick) * step(p.y, tall);
                float bottom = step(dx, halfTick) * step(Size.y - tall, p.y);
                Paint(col, a, max(top, bottom), lerp(accent * .75, Cold.rgb, major));

                float kv = floor((p.y / per) + .5);
                float dy = abs(p.y - (kv * per));
                float majorV = 1 - step(.5, fmod(abs(kv), 4));
                float len = lerp(2.6, 6.5, majorV) * Dip;
                float halfV = lerp(.5, .85, majorV) * Dip;
                float left = step(dy, halfV) * step(p.x, len);
                float right = step(dy, halfV) * step(Size.x - len, p.x);
                Paint(col, a, max(left, right), lerp(accent * .75, Cold.rgb, majorV));
            }

            // 2) the range ladder: a fixed spine with ticks under the hero boxes
            {
                float y0 = Size.y * .56;
                float spine = abs(p.y - y0) - (.5 * Dip);
                Stroke(col, a, spine, lerp(Dim.rgb, Cold.rgb, .4));
                float per = 40 * Dip;
                float k = floor((p.x / per) + .5);
                float dx = abs(p.x - (k * per));
                float major = 1 - step(.5, fmod(abs(k), 5));
                float tall = lerp(4, 8, major) * Dip;
                float halfTick = lerp(.55, 1.0, major) * Dip;
                float up = step(dx, halfTick) * step(y0 - tall, p.y) * step(p.y, y0);
                float dn = step(dx, halfTick) * step(y0, p.y) * step(p.y, y0 + (tall * .6));
                Paint(col, a, max(up, dn), lerp(Dim.rgb, Cold.rgb * .85, major));
            }

            // 3) the dock edges get their own brackets, so the hidden middle still reads as the subject
            {
                float y0 = Size.y * .28, y1 = Size.y * .84;
                float xl = Size.x * .262, xr = Size.x * .738;
                float d = 1e5;
                d = min(d, SdSeg(p, float2(xl, y0), float2(xl, y1)));
                d = min(d, SdSeg(p, float2(xl, y0), float2(xl - (7 * Dip), y0)));
                d = min(d, SdSeg(p, float2(xl, y1), float2(xl - (7 * Dip), y1)));
                d = min(d, SdSeg(p, float2(xr, y0), float2(xr, y1)));
                d = min(d, SdSeg(p, float2(xr, y0), float2(xr + (7 * Dip), y0)));
                d = min(d, SdSeg(p, float2(xr, y1), float2(xr + (7 * Dip), y1)));
                Stroke(col, a, d - (.5 * Dip), accent * .9);
            }

            // 4) the targets: faint glass plates that ignite into bracketed locks
            for (int i = 0; i < 6; i++)
            {
                float4 g = Tgt[i];
                float4 s = State[i];
                float conf = saturate(s.x);
                float locked = s.y;
                float grow = lerp(.94, 1.10, conf) + (.05 * Flash);
                float2 half = g.zw * grow;
                float2 c = g.xy;
                float th = (1.5 * Dip) + (1.1 * Bass) + (.6 * Flash);
                float arm = min(half.x * .45, half.y * .95) + (4 * Dip);

                // glass: dark in both states, so the HUD has weight and the digits read over a wallpaper
                float plane = saturate(Panel * lerp(.40, 1, locked));
                float inside = step(SdBox(p, c, half), 0);
                col = lerp(col, Ink.rgb, inside * plane);
                a = max(a, inside * plane);

                // candidate: a thin rim; locked: the rim steps back and the brackets take over
                float rim = abs(SdBox(p, c, half)) - (.55 * Dip);
                if (locked < .5) Stroke(col, a, rim, accent * 1.15);
                else Stroke(col, a, rim, Cold.rgb * .35);

                float d = 1e5;
                for (int k = 0; k < 4; k++)
                {
                    float2 sg = float2(((k & 1) == 0) ? 1 : -1, ((k & 2) == 0) ? 1 : -1);
                    float2 corner = c + (half * sg);
                    d = min(d, SdSeg(p, corner, corner - float2(0, sg.y * arm)));
                    d = min(d, SdSeg(p, corner, corner - float2(sg.x * arm, 0)));
                }

                float3 stroke = lerp(accent, Hot.rgb, locked);
                stroke = lerp(stroke, Core.rgb, saturate(Flash * 1.4) * .85);
                stroke = lerp(stroke, Alarm.rgb, Warn * .9);
                stroke = lerp(stroke, Cold.rgb, (.88 - Panel) * 2.4 * locked); // standby: hot brackets cool down
                Stroke(col, a, d - (th * .5), stroke * (.6 + (.4 * locked)));

                // the lock-on ring: a second set of brackets slammed outward by the kick
                if (Flash > .02)
                {
                    float2 oh = half + ((4 * Dip) + (6 * Dip * Flash));
                    float d2 = 1e5;
                    for (int k = 0; k < 4; k++)
                    {
                        float2 sg = float2(((k & 1) == 0) ? 1 : -1, ((k & 2) == 0) ? 1 : -1);
                        float2 corner = c + (oh * sg);
                        d2 = min(d2, SdSeg(p, corner, corner - float2(0, sg.y * arm)));
                        d2 = min(d2, SdSeg(p, corner, corner - float2(sg.x * arm, 0)));
                    }

                    Stroke(col, a, d2 - (.55 * Dip), Core.rgb * Flash * .8);
                }

                // targeting centreline with a gap for the reticle
                if (i < 4)
                {
                    float inset = 8 * Dip;
                    float gap = (i < 2 ? 7 : 5) * Dip;
                    float dl = min(SdSeg(p, float2(c.x - half.x + inset, c.y), float2(c.x - gap, c.y)),
                                   SdSeg(p, float2(c.x + gap, c.y), float2(c.x + half.x - inset, c.y)));
                    Stroke(col, a, dl - (.4 * Dip), lerp(accent * .9, Cold.rgb, locked));
                }

                // confidence gauge, one bar per target
                {
                    float barHalf = half.x - (10 * Dip);
                    float barY = c.y + half.y - (6 * Dip);
                    float fill = step(c.x - barHalf, p.x) * step(p.x, c.x - barHalf + (barHalf * 2 * conf));
                    float bar = fill * step(abs(p.y - barY), .8 * Dip);
                    Paint(col, a, bar, lerp(Cold.rgb, Hot.rgb, locked));
                }

                // telemetry: id digits on every plate, confidence digits on the heroes, ticks on the small ones
                if (i < 2)
                {
                    float dh = Size.y * .115;
                    float dd = Digits2Sd(p, float2(c.x - half.x + (7 * Dip), c.y - half.y + (5 * Dip)), dh, (int)(s.z + .5));
                    Stroke(col, a, dd, lerp(accent * 1.1, Hot.rgb, locked));

                    float dw = .62 * dh;
                    float2 org2 = float2(c.x + half.x - (7 * Dip) - ((dw * 2) + (.34 * dh)), c.y - half.y + (5 * Dip));
                    float cd = Digits2Sd(p, org2, dh, (int)((conf * 99) + .5));
                    Stroke(col, a, cd, Cold.rgb * (.7 + (.3 * locked)));

                    // three mini data bars stacked in the bottom-left corner
                    for (int b = 0; b < 3; b++)
                    {
                        float bx = c.x - half.x + (9 * Dip);
                        float by = c.y + half.y - ((18 - (b * 5)) * Dip);
                        float bw = (6 + (b * 4)) * Dip * (.55 + (.45 * conf));
                        float m = step(bx, p.x) * step(p.x, bx + bw) * step(abs(p.y - by), .55 * Dip);
                        Paint(col, a, m, lerp(accent, Cold.rgb, .35 + (.3 * b)));
                    }

                    if (locked > .5)
                    {
                        float r = 5 * Dip;
                        float gap = 1.4 * Dip;
                        float dc = 1e5;
                        dc = min(dc, SdSeg(p, c - float2(r, 0), c - float2(gap, 0)));
                        dc = min(dc, SdSeg(p, c + float2(gap, 0), c + float2(r, 0)));
                        dc = min(dc, SdSeg(p, c - float2(0, r), c - float2(0, gap)));
                        dc = min(dc, SdSeg(p, c + float2(0, gap), c + float2(0, r)));
                        Stroke(col, a, dc - (.6 * Dip), Core.rgb);
                    }
                }
                else if (i < 4)
                {
                    float dh = Size.y * .085;
                    float dd = Digits2Sd(p, float2(c.x - half.x + (5 * Dip), c.y - half.y + (4 * Dip)), dh, (int)(s.z + .5));
                    Stroke(col, a, dd, lerp(accent * 1.1, Hot.rgb, locked));
                    for (int b = 0; b < 2; b++)
                    {
                        float bx = c.x + half.x - (10 * Dip) - ((8 + (b * 4)) * Dip);
                        float by = c.y + half.y - ((7 + (b * 4)) * Dip);
                        float bw = (8 + (b * 4)) * Dip * (.55 + (.45 * conf));
                        float m = step(bx, p.x) * step(p.x, bx + bw) * step(abs(p.y - by), .5 * Dip);
                        Paint(col, a, m, lerp(accent, Cold.rgb, .4 + (.3 * b)));
                    }
                }
                else
                {
                    float bx = c.x - half.x + (4 * Dip);
                    float by = c.y + half.y - (4 * Dip);
                    float bw = (half.x * 2 - (8 * Dip)) * (.35 + (.65 * conf));
                    float m = step(bx, p.x) * step(p.x, bx + bw) * step(abs(p.y - by), .5 * Dip);
                    Paint(col, a, m, lerp(accent, Hot.rgb, locked));
                }
            }

            // 5) the search cursor: a low bracket that crawls while the music has content
            {
                float y0 = Size.y * .56;
                float d = 1e5;
                d = min(d, SdSeg(p, float2(SearchX, y0 + (3 * Dip)), float2(SearchX, y0 + (18 * Dip))));
                d = min(d, SdSeg(p, float2(SearchX, y0 + (18 * Dip)), float2(SearchX - (4 * Dip), y0 + (14 * Dip))));
                d = min(d, SdSeg(p, float2(SearchX, y0 + (18 * Dip)), float2(SearchX + (4 * Dip), y0 + (14 * Dip))));
                d = min(d, SdSeg(p, float2(SearchX - (9 * Dip), y0 - (6 * Dip)), float2(SearchX + (9 * Dip), y0 - (6 * Dip))));
                Stroke(col, a, d - (.6 * Dip), Cold.rgb * .95);
            }

            // 6) the scan bar: a kick throws it across the strip, a hard line with a dark edge and two escorts
            if (ScanAge < .34 && ScanHot > .02)
            {
                float k = ScanAge / .34;
                float x = ScanDir > 0 ? lerp(0, Size.x, k) : lerp(Size.x, 0, k);
                float body = step(2 * Dip, p.y) * step(p.y, Size.y - (2 * Dip));
                float edge = abs(p.x - x) - (3.0 * Dip);
                float core = abs(p.x - x) - (1.4 * Dip);
                Paint(col, a, body * step(edge, 0), Ink.rgb);
                Paint(col, a, body * step(core, 0), lerp(Cold.rgb, Core.rgb, ScanHot * .85));
                float e1 = abs(p.x - (x - (ScanDir * 5 * Dip))) - (.8 * Dip);
                float e2 = abs(p.x - (x - (ScanDir * 11 * Dip))) - (.6 * Dip);
                Paint(col, a, body * step(e1, 0), Cold.rgb * .55 * ScanHot);
                Paint(col, a, body * step(e2, 0), Cold.rgb * .3 * ScanHot);
                float2 hp = p - float2(x, Size.y * .56);
                float hd = abs(hp.x) + (abs(hp.y) * 1.8) - (5 * Dip);
                Stroke(col, a, hd, Core.rgb * ScanHot);
            }

            // 7) hat crumbs: two small squares snapping on and off at the open ends
            if (GlitchLife > .02)
            {
                for (int i = 0; i < 2; i++)
                {
                    float4 g = Glitch[i];
                    if (g.z > 1)
                    {
                        float d = SdBox(p, g.xy, float2(g.z * .5, g.z * .5));
                        Stroke(col, a, d - (.5 * Dip), lerp(Core.rgb, Cold.rgb, GlitchLife));
                    }
                }
            }

            return float4(col * a, a) * Fade;
        }
        """;
}
