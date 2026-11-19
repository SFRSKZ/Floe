using Floe.App.Services;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// CP77 material studies rendered in linear HDR with feedback bloom and ACES filmic tone mapping:
/// <list type="bullet">
/// <item>Redline: an illuminated Arasaka cyberdeck circuit board with carbon-matte substrate, copper-crimson bus
/// highways, beveled vias, high-speed HDR data packets with trailing afterglow and overclock thermal surges.</item>
/// <item>Kiroshi: a Kiroshi Optics Mk.3 thermal-topographic field with filled elevation strata and a curved glass
/// scanner lens that sweeps outward, magnifying the contours with chromatic dispersion, Fresnel rim reflections
/// and golden target-lock brackets.</item>
/// </list>
/// In silence all motion freezes into a poised, luminous technical display.
/// </summary>
public sealed class MaterialGpuScene : FeedbackGpuScene
{
    public enum Material { Mercury, Engram, Redline, Kiroshi }

    private const int ConstantFloats = 60;
    private readonly Material _material;
    private readonly Color _a, _b, _hot, _deep;
    private float _phase, _kickAge = 9, _snareAge = 9, _seed, _flare, _lens;

    public MaterialGpuScene(Material material)
        : base(ConstantFloats, Shared + $"\n#define STYLE {(int)material}\n" + FeedbackPs, Shared + $"\n#define STYLE {(int)material}\n" + ShowPs)
    {
        _material = material;
        (_a, _b, _hot, _deep) = material switch
        {
            Material.Mercury => (Color.FromRgb(0xE8, 0xE4, 0xDC), Color.FromRgb(0xFF, 0x12, 0x30), Color.FromRgb(0xFF, 0xF6, 0xE8), Color.FromRgb(0x1A, 0x05, 0x08)),
            Material.Engram => (Color.FromRgb(0xFF, 0x0A, 0x40), Color.FromRgb(0xFC, 0xEE, 0x0A), Color.FromRgb(0xFF, 0xF0, 0xF0), Color.FromRgb(0x2A, 0x06, 0x0E)),
            Material.Redline => (Color.FromRgb(0xFF, 0x18, 0x28), Color.FromRgb(0xFF, 0x7A, 0x28), Color.FromRgb(0xFF, 0xF2, 0xE2), Color.FromRgb(0x26, 0x05, 0x0A)),
            _ => (Color.FromRgb(0x18, 0xE0, 0xD2), Color.FromRgb(0xFC, 0xD4, 0x24), Color.FromRgb(0xF6, 0xFF, 0xFC), Color.FromRgb(0x06, 0x1B, 0x24)),
        };
    }

    public static VisualizerStyle StyleOf(Material material) => material switch
    {
        Material.Mercury => VisualizerStyle.CyberMercury,
        Material.Engram => VisualizerStyle.CyberEngram,
        Material.Redline => VisualizerStyle.CyberRedline,
        _ => VisualizerStyle.CyberKiroshi,
    };

    protected override void Advance(GpuFrame frame)
    {
        float dt = frame.Dt;
        var audio = frame.Audio;
        _kickAge += dt;
        _snareAge += dt;
        if (Beat.Hit)
        {
            _seed = (_seed + 1) % 997;
            if (Beat.Snare) _snareAge = 0;
            else _kickAge = 0;
        }

        _flare = Math.Max(_flare * MathF.Exp(-dt * 4f), Math.Max(_kickAge < .02f ? 1f : 0f, audio.Bass * .55f));
        if (audio.Level > .015f)
        {
            _phase = (_phase + (dt * (.35f + (1.65f * audio.Level) + (.95f * _flare)))) % 10000;
            _lens += dt * (.055f + (.11f * audio.Level) + (.14f * _flare));
            if (_lens > 1.08f) _lens = -.08f;
        }
    }

    protected override void Fill(GpuFrame frame)
    {
        var d = Data;
        float step = frame.Dt * 60f;
        d[0] = frame.Width;
        d[1] = frame.Height;
        d[2] = frame.PixelsPerDip;
        d[3] = Math.Clamp(frame.Fade, 0, 1);
        d[4] = Math.Clamp(frame.DockLeft, .05f, .9f);
        d[5] = Math.Clamp(frame.DockRight, d[4] + .02f, .95f);
        d[6] = frame.Audio.Level;
        d[7] = _flare;
        d[8] = _phase;
        d[9] = _kickAge;
        d[10] = _snareAge;
        d[11] = _seed;
        d[12] = _lens;
        d[13] = MathF.Pow(.86f, step);
        d[14] = frame.Audio.Bass;
        d[15] = frame.Audio.Treble;
        PutBands(16, frame);
        var a = Lead(_a);
        Put(40, a);
        Put(44, Second(_b));
        Put(48, Theme is null ? _hot : Mix(a, Colors.White, .82f));
        Put(52, Theme is null ? _deep : Mix(Colors.Black, a, .26f));
        Put(56, Color.FromRgb(0x00, 0xE8, 0xFF));
    }

    private const string Shared = """
        cbuffer Material : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float DockLeft;
            float DockRight;
            float Level;
            float Flare;
            float Phase;
            float KickAge;
            float SnareAge;
            float Seed;
            float LensU;
            float Decay;
            float Bass;
            float Treble;
            float4 Bands[6];
            float4 ColA;
            float4 ColB;
            float4 HotCol;
            float4 DeepCol;
            float4 AccentCol;
        };

        """ + ShaderHelpers + """

        float3 AcesTone(float3 x)
        {
            return saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14));
        }

        float WingInfo(float u, out float wingU, out float wingEdge, out float side)
        {
            float leftW = max(DockLeft, 0.001);
            float rightW = max(1.0 - DockRight, 0.001);
            if (u < DockLeft)
            {
                side = -1.0;
                wingU = saturate((DockLeft - u) / leftW);
                wingEdge = smoothstep(0.0, 0.032, wingU) * smoothstep(1.0, 0.94, wingU);
                return 1.0;
            }
            if (u > DockRight)
            {
                side = 1.0;
                wingU = saturate((u - DockRight) / rightW);
                wingEdge = smoothstep(0.0, 0.032, wingU) * smoothstep(1.0, 0.94, wingU);
                return 1.0;
            }
            side = 0.0;
            wingU = 0.0;
            wingEdge = 0.0;
            return 0.0;
        }

        // Smooth topographic height field for Kiroshi optics
        float KiroshiField(float2 p, float band, float phase)
        {
            float h = 0.0;
            h += 0.52 * sin(p.x * 2.4 + cos(p.y * 2.8 + phase * 0.25));
            h += 0.30 * cos(p.x * 5.2 - p.y * 4.1 - phase * 0.38);
            h += 0.22 * Noise(p * 3.1 + float2(phase * 0.18, -phase * 0.12));
            h += 0.35 * band * exp(-pow((p.y - 0.5) * 2.2, 2.0));
            return h;
        }
        """;

    private const string FeedbackPs = """
        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 uv = position.xy / Size;
            float wingU, wingEdge, side;
            if (WingInfo(uv.x, wingU, wingEdge, side) < 0.5)
            {
                return float4(0, 0, 0, 0);
            }

            float active = step(0.015, Level);
            float4 prev = Last.Sample(Linear, uv);
            float4 prevSoft = 0.5 * prev
                + 0.25 * Last.Sample(Linear, saturate(uv + float2(2.0 / Size.x, 1.0 / Size.y)))
                + 0.25 * Last.Sample(Linear, saturate(uv - float2(2.0 / Size.x, 1.0 / Size.y)));
            float3 trail = lerp(prev.rgb, prevSoft.rgb, 0.4) * (active > 0.5 ? Decay : 0.94);

            float y = uv.y;
            float band = Band(saturate(wingU * 0.92 + 0.04));
            float topSoft = smoothstep(0.0, 0.18, y);
            float botSoft = smoothstep(1.0, 0.90, y);

            float shockPos = KickAge * 1.40;
            float shockEnv = exp(-KickAge * 3.8);
            float shockWave = exp(-pow((wingU - shockPos) * 14.0, 2.0)) * shockEnv;
            float snareEnv = exp(-SnareAge * 6.5);

            float3 col = 0;
            float alpha = 0;

        #if STYLE == 2
            // ==================== REDLINE: ARASAKA OVERCLOCK CYBERDECK PCB ====================
            // Illuminated carbon-matte PCB substrate with subtle copper sheen
            float weave = 0.5 + 0.5 * sin(position.x * 0.55 + position.y * 0.55) * sin(position.x * 0.55 - position.y * 0.55);
            float boardGrad = exp(-pow((y - 0.54) * 1.65, 2.0));
            float thermal = (0.35 + 0.85 * band + 0.9 * shockWave) * boardGrad;

            col = DeepCol.rgb * (0.75 + 0.45 * weave) * (0.75 + 0.55 * boardGrad);
            col += ColA.rgb * thermal * 0.34;
            // Subtle copper ground plane blocks between bus channels
            float blockMask = step(0.32, Hash(float2(floor(wingU * 18.0), floor(y * 5.0)))) * smoothstep(0.08, 0.14, abs(frac(y * 5.0) - 0.5));
            col += lerp(DeepCol.rgb, ColA.rgb, 0.22) * blockMask * (0.28 + 0.35 * band);

            // 5 sculpted chamfered bus traces with glowing packets & metallic vias
            float traceGlowTotal = 0;
            [unroll]
            for (int lane = 0; lane < 5; lane++)
            {
                float fl = (float)lane;
                float baseY = 0.16 + fl * 0.17;
                float seg = floor(wingU * 14.0 + fl * 0.37);
                float segF = frac(wingU * 14.0 + fl * 0.37);
                float hSeg = Hash(float2(seg, fl * 7.3 + side * 3.1));
                float hNext = Hash(float2(seg + 1.0, fl * 7.3 + side * 3.1));

                // 45-degree chamfered PCB jog between segments
                float jog0 = (hSeg - 0.5) * 0.075;
                float jog1 = (hNext - 0.5) * 0.075;
                float jog = lerp(jog0, jog1, smoothstep(0.76, 0.94, segF));
                float traceY = baseY + jog;

                float dy = abs(y - traceY);
                float core = exp(-dy * 95.0);
                float halo = exp(-dy * 20.0);

                float laneBand = Band(saturate((wingU * 0.8 + fl * 0.04) + 0.04));
                float speed = 1.6 + 0.45 * fl;
                float pktPhase = frac(wingU * (2.6 + 0.4 * fl) - Phase * speed * 0.32 + fl * 0.23);
                float head = exp(-pow((pktPhase - 0.88) * 26.0, 2.0));
                float tail = smoothstep(0.20, 0.88, pktPhase) * step(pktPhase, 0.90) * pow(pktPhase, 2.4);

                float energy = (0.35 + 0.85 * laneBand + 1.25 * shockWave);
                col += ColA.rgb * (halo * 0.42 * energy + core * 1.15 * energy);
                col += ColB.rgb * (halo * 0.55 + core * 1.9) * tail * (0.45 + 0.95 * Level);
                col += HotCol.rgb * (core * 2.8 + halo * 0.75) * head * (0.55 + 1.1 * Level + 0.8 * Flare);

                // Sculpted circular solder via pads at segment boundaries
                // a segment's own width in pixels, from the wing it is on: the taskbar's wings differ from the
                // dock's and from each other, and a fixed share turned the pads into ellipses there
                float wingW = side < 0.0 ? max(DockLeft, 0.001) : max(1.0 - DockRight, 0.001);
                float viaX = (segF - 0.5) * (wingW * Size.x / 14.0) / Size.y;
                float viaD = length(float2(viaX, y - traceY));
                float viaRing = exp(-abs(viaD - 0.024) * 140.0);
                float viaHole = smoothstep(0.018, 0.010, viaD);
                float viaSpark = step(0.72, Hash(float2(seg, fl + Seed))) * snareEnv;

                col = lerp(col, DeepCol.rgb * 0.3, viaHole * 0.75);
                col += lerp(ColB.rgb, HotCol.rgb, 0.45 + 0.55 * viaSpark) * viaRing * (0.65 + 0.9 * laneBand + 2.8 * viaSpark);
                col += HotCol.rgb * exp(-viaD * 32.0) * viaSpark * 2.4;

                traceGlowTotal = max(traceGlowTotal, max(core, halo * 0.65));
            }

            // Overclock kick surge wave
            col += lerp(ColA.rgb, HotCol.rgb, 0.55) * shockWave * (0.75 + 1.2 * boardGrad);

            alpha = saturate(max(boardGrad * 0.78, traceGlowTotal) * wingEdge * topSoft);
        #else
            // ==================== KIROSHI: OPTICS MK.3 THERMAL-TOPOGRAPHIC HUD ====================
            float2 fp = float2(wingU * 4.8, y * 1.6);

            // Curved glass scanner lens sweeping smoothly across each wing
            float lensCenterU = saturate(LensU);
            float lensAspect = (Size.x * max(DockLeft, 0.15)) / max(Size.y, 1.0);
            float2 lensDelta = float2((wingU - lensCenterU) * lensAspect * 0.48, (y - 0.50) * 1.0);
            float lensR = 0.40;
            float lensDist = length(lensDelta);
            float inLens = smoothstep(lensR + 0.015, lensR - 0.015, lensDist);

            // Optical barrel magnification inside the Kiroshi lens
            float2 sampleFp = fp;
            if (inLens > 0.001)
            {
                float radial = saturate(lensDist / lensR);
                float mag = 1.0 - 0.32 * (1.0 - radial * radial);
                float2 centerFp = float2(lensCenterU * 4.8, 0.50 * 1.6);
                sampleFp = centerFp + (fp - centerFp) * mag;
            }

            float h = KiroshiField(sampleFp, band, Phase);
            float hShift = KiroshiField(sampleFp + float2(0.035, 0.015), band, Phase);

            // Rich volumetric elevation strata fill (eliminates flat 1px wireframe look!)
            float elev = saturate(h * 0.48 + 0.52);
            float3 strataFill = lerp(DeepCol.rgb * 1.25, ColA.rgb * 0.52, pow(elev, 1.35));
            // Thermal warm amber peaks where spectrum energy is high
            float thermalHot = smoothstep(0.62, 0.96, elev + band * 0.24 + shockWave * 0.25);
            strataFill = lerp(strataFill, ColB.rgb * 0.85, thermalHot * 0.72);

            // Crisp analytical contour lines with soft inner glow
            float levels = 6.5;
            float fVal = h * levels;
            float fDist = abs(frac(fVal) - 0.5);
            float lineCore = exp(-fDist * 26.0);
            float lineGlow = exp(-fDist * 7.5);
            float majorLine = exp(-abs(frac(h * levels * 0.5) - 0.5) * 32.0);

            col = strataFill * (0.68 + 0.48 * band + 0.35 * Level);
            col += ColA.rgb * (lineGlow * 0.42 + lineCore * 1.35) * (0.55 + 0.75 * band);
            col += lerp(ColB.rgb, HotCol.rgb, 0.45) * majorLine * (0.65 + 1.15 * band + 0.9 * shockWave);

            // Subtle optical scanlines & lower rangefinder calibration ticks
            float scanline = 0.90 + 0.10 * sin(position.y * 1.6);
            col *= scanline;
            float tickZone = smoothstep(0.84, 0.95, y);
            float ticks = smoothstep(0.75, 0.96, sin(wingU * 140.0));
            col += ColA.rgb * tickZone * ticks * (0.35 + 0.65 * Treble + 0.8 * snareEnv);

            // Curved glass Kiroshi lens overlay: chromatic dispersion + Fresnel rim + tactical brackets
            if (inLens > 0.001)
            {
                float hR = KiroshiField(sampleFp + float2(0.045, 0.0), band, Phase);
                float hB = KiroshiField(sampleFp - float2(0.045, 0.0), band, Phase);
                float lR = exp(-abs(frac(hR * levels) - 0.5) * 24.0);
                float lB = exp(-abs(frac(hB * levels) - 0.5) * 24.0);
                float3 disp = float3(lR * 1.4, lineCore * 1.2, lB * 1.6);

                // Glass Fresnel edge + curved specular highlight
                float radial = saturate(lensDist / lensR);
                float glassFresnel = pow(radial, 3.2);
                float glassSpec = exp(-length(lensDelta + float2(0.14, 0.16)) * 9.0);

                float3 lensCol = col * 0.95 + disp * lerp(ColA.rgb, ColB.rgb, 0.45) * 0.65;
                lensCol += lerp(ColA.rgb, HotCol.rgb, 0.5) * glassFresnel * 0.60;
                lensCol += HotCol.rgb * glassSpec * 0.55;

                // Crisp reticle crosshairs inside lens
                float crossX = exp(-abs(lensDelta.x) * 90.0) * step(0.08, abs(lensDelta.y)) * step(abs(lensDelta.y), lensR * 0.78);
                float crossY = exp(-abs(lensDelta.y) * 90.0) * step(0.08, abs(lensDelta.x)) * step(abs(lensDelta.x), lensR * 0.78);
                lensCol += ColB.rgb * (crossX + crossY) * (0.9 + 1.2 * Flare);

                col = lerp(col, lensCol, inLens);
            }

            // Outer Kiroshi lens ring & golden corner target-lock brackets
            float ring = exp(-abs(lensDist - lensR) * 58.0);
            float ringGlow = exp(-abs(lensDist - lensR) * 14.0);
            col += ColB.rgb * (ring * 2.2 + ringGlow * 0.55) * (0.75 + 0.85 * Flare + 0.6 * snareEnv);
            col += HotCol.rgb * ring * (0.7 + 1.2 * shockEnv);

            float2 bAbs = abs(lensDelta);
            float bracketBox = lensR + 0.065;
            float cornerX = exp(-abs(bAbs.x - bracketBox) * 75.0) * step(bracketBox - 0.15, bAbs.y) * step(bAbs.y, bracketBox + 0.01);
            float cornerY = exp(-abs(bAbs.y - bracketBox) * 75.0) * step(bracketBox - 0.15, bAbs.x) * step(bAbs.x, bracketBox + 0.01);
            col += lerp(ColB.rgb, HotCol.rgb, 0.4) * (cornerX + cornerY) * (1.2 + 1.5 * Flare);

            // Kick scan sweep across the optics
            col += lerp(ColA.rgb, HotCol.rgb, 0.6) * shockWave * 1.15;

            alpha = saturate(max(0.76, max(lineCore, max(inLens * 0.94, ring))) * wingEdge * topSoft);
        #endif

            col *= wingEdge * topSoft * botSoft;
            float3 outCol = active > 0.5 ? max(col, trail * 0.82) : col;
            return float4(outCol, active > 0.5 ? max(alpha, prev.a * 0.74) : alpha);
        }
        """;

    private const string ShowPs = """
        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 uv = position.xy / Size;
            float wingU, wingEdge, side;
            if (WingInfo(uv.x, wingU, wingEdge, side) < 0.5)
            {
                return float4(0, 0, 0, 0);
            }

            float ca = (0.45 + 2.8 * exp(-KickAge * 5.0) + 2.0 * exp(-SnareAge * 7.0)) * Dip / Size.x;
            float4 c0 = Last.Sample(Linear, uv);
            float r = Last.Sample(Linear, saturate(uv + float2(ca, 0))).r;
            float b = Last.Sample(Linear, saturate(uv - float2(ca, 0))).b;
            float3 hdr = float3(r, c0.g, b);

            float2 px = float2(Dip / Size.x, Dip / Size.y);
            float3 bloom = 0;
            bloom += Last.Sample(Linear, saturate(uv + float2(px.x * 4.0, 0))).rgb * 0.25;
            bloom += Last.Sample(Linear, saturate(uv - float2(px.x * 4.0, 0))).rgb * 0.25;
            bloom += Last.Sample(Linear, saturate(uv + float2(0, px.y * 2.5))).rgb * 0.25;
            bloom += Last.Sample(Linear, saturate(uv - float2(0, px.y * 2.5))).rgb * 0.25;
            hdr += max(bloom - 0.48, 0.0) * 0.52;

            float grain = (Hash(floor(position.xy) + floor(Phase * 22.0)) - 0.5) * 0.015;
            float3 graded = AcesTone(pow(max(hdr, 0.0), 1.48) * 1.48) + grain;

            float topSoft = smoothstep(0.0, 0.20, uv.y);
            float luma = dot(graded, float3(0.299, 0.587, 0.114));
            float alpha = saturate(max(c0.a * 0.52, smoothstep(0.02, 0.30, luma)) * wingEdge * topSoft) * Fade;

            return float4(saturate(graded) * alpha, alpha);
        }
        """;
}
