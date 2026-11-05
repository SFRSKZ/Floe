using Color = System.Windows.Media.Color;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// Biometric Wave / Braindance Audio Layer. The one design that survived review: Trauma Team's ECG (concept 04)
/// rebuilt from researched CP2077 interface elements and driven by the real analyser, not a synthetic clock.
/// <para>
/// Three stacked Braindance layers: a 24-band spectrum (visual), the oscilloscope / ECG trace (audio) and a
/// thermal bed (heat rings, LEDs, barcode ornaments). Breach Protocol's hex-matrix look supplies the buffer
/// readouts; the HUD colour semantics are cyan = information, yellow = warning/buffer, red = DEFIB / THREAT,
/// grey = secondary, and the ornaments are deliberately unreadable blocks.
/// </para>
/// <para>
/// Audio mapping (MuseGen / MilkDrop): bass -&gt; amplitude and heat, mids -&gt; deformation, treble -&gt; the
/// second trace and sparks, kick -&gt; a QRS spike plus a red DEFIB flash, snare -&gt; the warning latch,
/// level -&gt; overall brightness. Silence is an all-zero frame: the trace flattens, the bars die and Fade
/// (owned by the host) hides the strip.
/// </para>
/// </summary>
public sealed class BiometricWaveGpuScene : FeedbackGpuScene
{
    private const int ConstantFloats = 64;

    private static readonly Color Core = Color.FromRgb(0xE8, 0xEA, 0xF0);
    private static readonly Color Ice = Color.FromRgb(0x00, 0xE5, 0xFF);
    private static readonly Color Hot = Color.FromRgb(0xFC, 0xEE, 0x0A);
    private static readonly Color Alarm = Color.FromRgb(0xFF, 0x00, 0x3C);
    private static readonly Color Bio = Color.FromRgb(0x5A, 0xFF, 0xAA);
    private static readonly Color Dim = Color.FromRgb(0x48, 0x56, 0x6A);

    private float _surge, _tag;

    public BiometricWaveGpuScene() : base(ConstantFloats, Shared + FeedbackPs, Shared + ShowPs)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        _surge = Beat.Hit ? 1f : Math.Max(0, _surge - (frame.Dt * 5f));
        _tag = Beat.Snare ? 1f : Math.Max(0, _tag - (frame.Dt * 3f));
    }

    protected override void Fill(GpuFrame frame)
    {
        var d = Data;
        d[0] = frame.Width;
        d[1] = frame.Height;
        d[2] = frame.PixelsPerDip;
        d[3] = Math.Clamp(frame.Fade, 0, 1);
        d[4] = frame.Time;
        d[5] = frame.Audio.Level;
        d[6] = frame.Audio.Bass;
        d[7] = frame.Audio.Mid;
        d[8] = frame.Audio.Treble;
        d[9] = frame.Audio.Kick;
        d[10] = frame.Audio.Snare;
        d[11] = frame.Audio.Hat;
        d[12] = Math.Max(_surge, frame.Audio.Kick);
        d[13] = Math.Max(_tag, frame.Audio.Snare);
        d[14] = frame.Audio.Level < .01f ? 1f : 0f; // Flat: the analyser's all-zero frame
        d[15] = 0;
        PutBands(16, frame);
        Put(40, Core);
        Put(44, Ice);
        Put(48, Hot);
        Put(52, Alarm);
        Put(56, Bio);
        Put(60, Dim);
    }

    private const string Shared = """
        cbuffer BioWave : register(b0)
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
            float Surge;
            float Tag;
            float Flat;
            float Pad0;
            float4 Bands[6];
            float4 Core;
            float4 Ice;
            float4 Hot;
            float4 Alarm;
            float4 Bio;
            float4 Dim;
        };

        """ + ShaderHelpers + """

        // 5x7 bitmap glyphs for the diegetic readouts: 0..9, 10 = B, 11 = D.
        int2 BioBits(float d)
        {
            if (d < .5) return int2(575022, 14897);
            if (d < 1.5) return int2(135556, 14468);
            if (d < 2.5) return int2(67118, 32004);
            if (d < 3.5) return int2(69727, 14881);
            if (d < 4.5) return int2(600258, 2143);
            if (d < 5.5) return int2(64031, 14881);
            if (d < 6.5) return int2(999686, 14897);
            if (d < 7.5) return int2(133183, 8456);
            if (d < 8.5) return int2(476718, 14897);
            if (d < 9.5) return int2(509486, 12353);
            if (d < 10.5) return int2(1001022, 31281);
            return int2(575038, 31281);
        }

        float BioGlyph(float2 q, float d)
        {
            if (q.x < 0 || q.x > 5 || q.y < 0 || q.y > 7) return 0;
            int c = (int)floor(q.x);
            int r = (int)floor(q.y);
            int2 bits = BioBits(d);
            int column = 4 - c;
            int v = r < 4 ? (bits.x >> ((r * 5) + column)) : (bits.y >> (((r - 4) * 5) + column));
            return (float)(v & 1);
        }

        float BioBarcode(float2 p, float w, float dip)
        {
            float bx = floor((p.x - 18 * dip) / (3 * dip));
            float on = Hash(float2(bx, 7));
            return step(0, bx) * step(bx, 46) * step(.38, on)
                 * step(p.y, Size.y * .18) * step(Size.y * .10, p.y);
        }

        """;

    private const string FeedbackPs = """

        float4 main(float4 position : SV_Position) : SV_Target
        {
            int2 q = (int2)position.xy;
            float decay = .80 + (.10 * Bass);
            float4 prev = Last.Load(int3(q, 0)) * decay;

            float2 p = position.xy;
            float u = p.x / Size.x, v = p.y / Size.y;

            // a thin scan bar walks over the audio layer, leaving a phosphor trail on the field
            float head = frac(Time * .13);
            float scan = exp(-abs(u - head) * 260) * Level * step(.30, v) * step(v, .72);

            // hats seed sparse thermal sparks
            float2 cell = floor(p / 3) + float2(floor(Time * 18), floor(Time * 18));
            float spark = step(.994 - (Hat * .02), Hash(cell)) * Hat * .55;

            return prev + float4((scan * .22) + spark, 0, 0, 0);
        }
        """;

    private const string ShowPs = """

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 p = position.xy;
            float W = Size.x, H = Size.y;
            float u = p.x / W, v = p.y / H;
            float dip = max(1, Dip);
            float live = 1 - Flat;

            // ---- ground: gradient, eight volumetric shafts, distant haze, technical grid, grain ----
            float3 col = lerp(float3(.008, .012, .020), float3(.035, .050, .075), 1 - v);
            for (int s = 0; s < 8; s++)
            {
                float sx = frac(((float)s * .137) + (Time * .012));
                float shaft = exp(-abs(u - sx) * 48) * (.55 + (.45 * sin((Time * .7) + s)));
                float3 shade = (s % 3 == 0) ? Hot.rgb : ((s % 3 == 1) ? Ice.rgb : Alarm.rgb);
                col += shade * shaft * (.020 + (.025 * Level)) * live * (1 - (v * .5));
            }
            for (int k = 0; k < 5; k++)
            {
                float hy = H * (.40 + ((float)k * .045));
                col += Dim.rgb * step(abs(p.y - hy), 1.2 * dip) * .05 * live;
            }
            float grid = step(.94, frac(p.x / (11 * dip))) + step(.94, frac(p.y / (9 * dip)));
            col += Dim.rgb * grid * .018;
            col += (Hash(p + float2(Time * 60, Time * 37)) - .5) * .012;

            // ---- diegetic HUD: framed readouts, corner brackets, ruler ticks ----
            float leftPanel = step(p.x, 420 * dip) * step(4 * dip, p.x);
            float rightPanel = step(W - (420 * dip), p.x) * step(p.x, W - (4 * dip));
            float panel = max(leftPanel, rightPanel);
            float edgeX = min(abs(p.x - (4 * dip)), abs(p.x - (W - (4 * dip))));
            float edgeY = min(abs(p.y - (4 * dip)), abs(p.y - (H - (4 * dip))));
            float frameLine = step(min(edgeX, edgeY), 1.1 * dip) * panel;
            float cx = min(p.x, W - p.x), cy = min(p.y, H - p.y);
            float bracket = (step(cx, 22 * dip) * step(cy, 2 * dip))
                          + (step(cx, 2 * dip) * step(cy, 22 * dip));
            col += Ice.rgb * ((frameLine * .10) + (bracket * .30)) * live;
            col += Dim.rgb * step(.90, frac(p.x / (16 * dip))) * step(p.y, 6 * dip) * .25 * live;

            // ---- LAYER 1 (visual): 24 log-spaced spectrum bars + unreadable hex blocks ----
            float colId = floor(u * 24);
            float cellU = frac(u * 24);
            float bv = Band((colId + .5) / 24);
            float3 barCol = colId < 7 ? Ice.rgb : (colId < 17 ? Hot.rgb : Alarm.rgb);
            float barTop = H * .34;
            float barH = (3 + (bv * 26)) * dip;
            float barEdge = step(.06, cellU) * step(cellU, .94);
            float inBar = step(p.y, barTop) * step(barTop - barH, p.y);
            float barGlow = exp(-abs(p.y - (barTop - barH)) * .28) * barEdge * bv;
            col += barCol * (inBar * barEdge * (.35 + (.65 * bv)) + (barGlow * .35)) * live;
            float hexRow = floor((p.y - barTop) / (5 * dip));
            if (p.y > barTop && hexRow >= 0 && hexRow < 3)
            {
                float blk = Hash(float2(colId, hexRow) + floor(Time * 3));
                col += Dim.rgb * step(.55, blk) * step(.12, cellU) * step(cellU, .88) * .22 * live;
            }

            // ---- LAYER 2 (audio): the oscilloscope / ECG trace, driven by the real wave and onsets ----
            float yc = H * .55;
            float phase = (u * 46) + (Time * 3.2);
            float wave = (sin(phase) * .5) + (sin((phase * 2.27) + (Time * 1.7)) * .26)
                       + (sin((phase * .73) - (Time * 1.1)) * .3);
            wave *= (.22 + (.78 * Level)) * (.55 + (.75 * Bass));
            wave += (Band(u) - .5) * .34 * Mid;

            // kick = the QRS complex
            float beat = frac(Time * 2);
            float spike = exp(-pow((u - beat) * 100, 2)) * Surge;
            float amp = (10 + (12 * Level)) * dip;
            float yWave = yc + (wave * amp) - (spike * 26 * dip);

            float dist = abs(p.y - yWave);
            float core = exp(-dist * dist * .55);
            float glow = exp(-dist * .32);
            float halo = exp(-dist * .10);
            col += Bio.rgb * ((core * 1.15) + (glow * .30) + (halo * .12)) * live;
            col += Core.rgb * exp(-dist * dist * 2.2) * .35 * live; // white-hot centre of the trace

            // a ghost of the trace, carried by the highs
            float yGhost = yc - (wave * amp * .55) - (spike * 10 * dip);
            col += Ice.rgb * exp(-abs(p.y - yGhost) * .22) * (.06 + (.16 * Treble)) * live;

            // chromatic aberration: the trace's red and cyan fringes
            float dR = abs(p.y - (yWave + (2 * dip)));
            float dC = abs(p.y - (yWave - (2 * dip)));
            col += Alarm.rgb * exp(-dR * dR * .5) * .10 * live;
            col += Ice.rgb * exp(-dC * dC * .5) * .10 * live;

            // thermal second trace: mids deform it, treble sharpens it
            float y2 = yc + (8 * dip) + (sin((phase * 1.7) + (Time * 2.3)) * (3 + (9 * Treble)) * dip);
            col += Alarm.rgb * exp(-abs(p.y - y2) * .55) * (.12 + (.35 * Mid)) * live;
            col += Dim.rgb * exp(-abs(p.y - yc) * 1.2) * .05;

            // ---- LAYER 3 (thermal): bass heat rings, 24 LEDs, HUD ornaments ----
            for (int i = 0; i < 8; i++)
            {
                float cx = ((float)i / 8) + (.04 * sin((Time * .5) + i));
                float2 c = float2(cx * W, H * .86);
                float d = length(p - c) / (28 * dip);
                float heat = exp(-d * d);
                col += Alarm.rgb * heat * (.10 + (.35 * Bass)) * live;
                col += Hot.rgb * exp(-d * d * 3) * .06 * live;
            }
            float ledY = H * .90;
            float ledV = Band((23 - colId + .5) / 24);
            float led = step(abs(p.y - ledY), 2.2 * dip) * step(.10, cellU) * step(cellU, .90);
            float3 ledCol = colId < 8 ? Alarm.rgb : (colId < 18 ? Hot.rgb : Ice.rgb);
            col += ledCol * led * (.2 + (.8 * ledV)) * live;
            col += ledCol * exp(-abs(p.y - ledY) * .35) * step(.10, cellU) * step(cellU, .90)
                 * .10 * ledV * live;

            // diegetic HUD: corner frame, barcode, live timecode and buffer digits
            float frame = step(min(p.x, W - p.x), 2 * dip) * step(p.y, H * .92);
            col += Ice.rgb * frame * .18;
            col += Hot.rgb * BioBarcode(p, W, dip) * .22 * live;

            float2 gq = (p - float2(18 * dip, 8 * dip)) / (2 * dip);
            float clock = fmod(Time, 60);
            float t1 = floor(clock / 10);
            float t2 = fmod(floor(clock), 10);
            float t3 = floor(fmod(Time * 10, 10));
            float gd = max(max(BioGlyph(float2(gq.x, gq.y), t1),
                              BioGlyph(float2(gq.x - 7, gq.y), t2)),
                           BioGlyph(float2(gq.x - 16, gq.y), t3));
            col += Ice.rgb * gd * .55 * live;

            float2 rq = (p - float2(W - 120 * dip, 8 * dip)) / (2 * dip);
            float b1 = 0, b2 = 6, b3 = 0, b4 = 8;
            float bd = max(max(BioGlyph(float2(rq.x, rq.y), b1), BioGlyph(float2(rq.x - 7, rq.y), b2)),
                           max(BioGlyph(float2(rq.x - 16, rq.y), b3), BioGlyph(float2(rq.x - 23, rq.y), b4)));
            col += Hot.rgb * bd * .55 * live;

            // snare = warning latch, kick = the DEFIB wash
            col += Hot.rgb * Tag * step(H * .06, p.y) * step(p.y, H * .12) * .10;
            col += Alarm.rgb * Surge * exp(-abs(u - beat) * 5) * .06;

            // ---- finish: phosphor trail, scanlines, vignette, silence gate ----
            float trail = Last.SampleLevel(Linear, p / Size, 0).r;
            col += Bio.rgb * trail * .25 * live;
            col *= 1 - (.05 * step(.5, frac(p.y / (3 * dip))));
            float vig = smoothstep(0, .10, u) * smoothstep(0, .10, 1 - u);
            col *= .45 + (.55 * vig);

            col *= 1 - (.75 * Flat);
            col += Alarm.rgb * exp(-abs(p.y - yc) * 1.5) * Flat * .5; // flatline

            return float4(col, 1) * Fade;
        }
        """;
}
