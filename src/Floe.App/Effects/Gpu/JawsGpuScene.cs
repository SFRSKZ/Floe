using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// The chrome cyberware jaws: two interlocking rows of sculpted, beveled brushed-titanium blades emerge from the top
/// and bottom edges of each wing and clamp to the music. Analytical bevel normals sample a high-contrast studio
/// chrome reflection with Fresnel edge glints and a glowing crimson plasma cavity between the mandibles. Each blade
/// pair extends with its frequency band; a kick slams the jaws inward with a white-hot plasma arc down the bite line;
/// a snare sprays incandescent sparks from the blade tips. In silence the blades hold poised with a soft crimson glow.
/// </summary>
public sealed class JawsGpuScene : FeedbackGpuScene
{
    private const int ConstantFloats = 64;
    private static readonly Color Chrome = Color.FromRgb(0xEC, 0xEA, 0xE6);
    private static readonly Color Dark = Color.FromRgb(0x4A, 0x4E, 0x58);
    private static readonly Color Vein = Color.FromRgb(0xFF, 0x12, 0x34);
    private static readonly Color Hot = Color.FromRgb(0xFF, 0xF5, 0xE8);
    private static readonly Color Deep = Color.FromRgb(0x1C, 0x04, 0x0A);

    private float _phase, _kickAge = 9, _lock, _spark, _seed = 1;

    public JawsGpuScene() : base(ConstantFloats, Shared + FeedbackPs, Shared + ShowPs)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        float dt = frame.Dt;
        var audio = frame.Audio;
        _kickAge += dt;
        if (Beat.Hit)
        {
            _seed = (_seed + 1) % 997;
            if (Beat.Snare) _spark = 1f;
            else _kickAge = 0;
        }

        _lock = MathF.Exp(-_kickAge * 6.2f);
        _spark *= MathF.Exp(-dt * 4.5f);

        if (audio.Level > .015f)
        {
            _phase = (_phase + (dt * (.85f + (2.6f * audio.Level) + (1.8f * audio.Bass)))) % 10000;
        }
    }

    protected override void Fill(GpuFrame frame)
    {
        var d = Data;
        var audio = frame.Audio;
        float step = frame.Dt * 60f;
        d[0] = frame.Width;
        d[1] = frame.Height;
        d[2] = frame.PixelsPerDip;
        d[3] = Math.Clamp(frame.Fade, 0, 1);
        d[4] = Math.Clamp(frame.DockLeft, .05f, .9f);
        d[5] = Math.Clamp(frame.DockRight, d[4] + .02f, .95f);
        d[6] = audio.Level;
        d[7] = audio.Bass;
        d[8] = audio.Treble;
        d[9] = _phase;
        d[10] = _kickAge;
        d[11] = _lock;
        d[12] = _spark;
        d[13] = _seed;
        d[14] = MathF.Pow(.84f, step);
        d[15] = 0;
        PutBands(20, frame);

        var chrome = Lead(Chrome);
        Put(44, chrome);
        Put(48, Theme is null ? Dark : Mix(chrome, Colors.Black, .58f));
        Put(52, Second(Vein));
        Put(56, Theme is null ? Hot : Mix(chrome, Colors.White, .85f));
        Put(60, Theme is null ? Deep : Mix(Colors.Black, chrome, .30f));
    }

    private const string Shared = """
        cbuffer Jaws : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float DockLeft;
            float DockRight;
            float Level;
            float Bass;
            float Treble;
            float Phase;
            float KickAge;
            float Lock;
            float Spark;
            float Seed;
            float Decay;
            float Pad0;
            float4 Spare;
            float4 Bands[6];
            float4 ChromeCol;
            float4 ShadeCol;
            float4 VeinCol;
            float4 HotCol;
            float4 DeepCol;
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
                wingEdge = smoothstep(0.0, 0.03, wingU) * smoothstep(1.0, 0.94, wingU);
                return 1.0;
            }
            if (u > DockRight)
            {
                side = 1.0;
                wingU = saturate((u - DockRight) / rightW);
                wingEdge = smoothstep(0.0, 0.03, wingU) * smoothstep(1.0, 0.94, wingU);
                return 1.0;
            }
            side = 0.0;
            wingU = 0.0;
            wingEdge = 0.0;
            return 0.0;
        }

        // Sculpted beveled cyberware fang profile: returns signed distance (< 0 inside blade),
        // plus local bevel normal x-sign and ridge distance for metallic shading.
        float FangProfile(float xLocal, float yFromGum, float reach, float lean, out float ridge, out float bevelSide)
        {
            float t = saturate(yFromGum / max(reach, 0.05));
            float center = lean * t;
            float halfW = lerp(0.42, 0.04, pow(t, 0.85));
            float dx = xLocal - center;
            bevelSide = smoothstep(-0.08, 0.08, dx) * 2.0 - 1.0;
            ridge = abs(dx) / max(halfW, 0.02);
            float dSide = abs(dx) - halfW;
            float dTip = yFromGum - reach;
            return max(dSide, dTip * 1.25);
        }

        // Studio liquid-chrome reflection sampled from a 3D normal
        float3 ChromeEnv(float3 n, float brush, float energy)
        {
            float3 v = float3(0, 0, 1);
            float3 r = reflect(-v, n);
            float fresnel = pow(1.0 - saturate(dot(n, v)), 3.0);

            // Overhead softbox key strip
            float keyBand = smoothstep(0.24, 0.58, r.y) * smoothstep(0.92, 0.48, r.y);
            // Sharp horizon dark cut (classic liquid-chrome high contrast)
            float horizon = exp(-pow((r.y + 0.04) * 5.5, 2.0));
            // Crimson thermal under-reflection from the plasma jaw cavity
            float underGlow = smoothstep(-0.08, -0.68, r.y) * (0.28 + 0.65 * energy);

            float3 metal = lerp(ShadeCol.rgb * 0.16, ChromeCol.rgb * 0.85, keyBand);
            metal *= (1.0 - 0.78 * horizon);
            metal += HotCol.rgb * pow(saturate(r.y * 0.85 + 0.22 + r.x * 0.25), 11.0) * 1.15;
            metal += VeinCol.rgb * underGlow * 0.85;
            metal *= (0.88 + 0.18 * brush);
            metal += lerp(ChromeCol.rgb, HotCol.rgb, 0.5) * fresnel * 0.55;
            return metal;
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
            float4 prevGlow = 0.5 * prev
                + 0.25 * Last.Sample(Linear, saturate(uv + float2(2.0 / Size.x, 1.5 / Size.y)))
                + 0.25 * Last.Sample(Linear, saturate(uv - float2(2.0 / Size.x, 1.5 / Size.y)));
            float3 trail = prevGlow.rgb * (active > 0.5 ? Decay : 0.92);

            float y = uv.y;
            float teeth = 9.0;
            float uScaled = wingU * teeth;

            // Upper row (anchored at y=0) and lower row (interlocking half-tooth offset, anchored at y=1)
            float cellTop = floor(uScaled);
            float localTop = frac(uScaled) - 0.5;
            float bandTop = Band(saturate((cellTop + 0.5) / teeth * 0.9 + 0.05));

            float uBot = uScaled + 0.5;
            float cellBot = floor(uBot);
            float localBot = frac(uBot) - 0.5;
            float bandBot = Band(saturate((cellBot + 0.5) / teeth * 0.9 + 0.05));

            // Jaw reach: poised in silence, clamping deeply on bass & kick
            float reachTop = 0.22 + 0.24 * bandTop + 0.10 * Bass + 0.16 * Lock;
            float reachBot = 0.22 + 0.24 * bandBot + 0.10 * Bass + 0.16 * Lock;

            float ridgeT, sideT;
            float dTop = FangProfile(localTop, y, reachTop, 0.10 * side, ridgeT, sideT);
            float ridgeB, sideB;
            float dBot = FangProfile(localBot, 1.0 - y, reachBot, -0.10 * side, ridgeB, sideB);

            float topMask = smoothstep(0.028, -0.015, dTop);
            float botMask = smoothstep(0.028, -0.015, dBot);

            // Glowing crimson-obsidian throat cavity between upper and lower jaws
            float throatY = abs(y - 0.5);
            float cavityGlow = exp(-throatY * 5.2);
            float energyWave = 0.5 + 0.5 * sin(wingU * 22.0 - Phase * 3.8);
            float energyCore = exp(-throatY * 18.0) * (0.35 + 0.65 * energyWave) * (0.25 + 0.75 * Level + 1.1 * Lock);

            // Travelling crimson plasma vein inside the throat
            float veinY = 0.5 + 0.07 * sin(wingU * 16.0 - Phase * 3.2) * (0.3 + 0.7 * Band(wingU));
            float veinLine = exp(-abs(y - veinY) * 62.0);

            float3 cavity = DeepCol.rgb * (0.32 + 0.55 * cavityGlow);
            cavity += VeinCol.rgb * (cavityGlow * (0.14 + 0.30 * Bass) + energyCore * 0.85 + veinLine * (0.75 + 1.15 * Level));
            cavity += HotCol.rgb * (veinLine * (0.35 + 0.95 * Lock) + exp(-throatY * 36.0) * Lock * 1.65);

            // Fine anisotropic brushed-metal grain
            float brush = Noise(float2(wingU * 140.0, y * 18.0));

            float3 col = cavity;
            float bladeAlpha = 0;

            if (topMask > 0.001)
            {
                float3 n = normalize(float3(sideT * 0.68, -0.42 + (1.0 - ridgeT) * 0.35, 0.68));
                float3 chrome = ChromeEnv(n, brush, 0.35 + 0.65 * bandTop + Lock);
                // Crisp glowing crimson inner bevel edge
                float rim = exp(-abs(dTop) * 52.0);
                chrome += VeinCol.rgb * rim * (0.55 + 0.95 * bandTop + 1.25 * Lock);
                chrome += HotCol.rgb * exp(-abs(ridgeT) * 18.0) * (0.25 + 0.45 * bandTop);
                col = lerp(col, chrome, topMask);
                bladeAlpha = max(bladeAlpha, topMask);
            }

            if (botMask > 0.001)
            {
                float3 n = normalize(float3(sideB * 0.68, 0.42 - (1.0 - ridgeB) * 0.35, 0.68));
                float3 chrome = ChromeEnv(n, brush, 0.35 + 0.65 * bandBot + Lock);
                float rim = exp(-abs(dBot) * 52.0);
                chrome += VeinCol.rgb * rim * (0.55 + 0.95 * bandBot + 1.25 * Lock);
                chrome += HotCol.rgb * exp(-abs(ridgeB) * 18.0) * (0.25 + 0.45 * bandBot);
                col = lerp(col, chrome, botMask);
                bladeAlpha = max(bladeAlpha, botMask);
            }

            // Gum cyber-rail along top and bottom borders with flowing packets
            float railTop = smoothstep(0.06, 0.01, y);
            float railBot = smoothstep(0.94, 0.99, y);
            float packet = smoothstep(0.72, 0.98, sin(wingU * 40.0 - Phase * 5.2));
            col += lerp(VeinCol.rgb, HotCol.rgb, packet) * (railTop + railBot) * (0.16 + 0.55 * packet * (0.3 + Level));

            // Fine pinpoint snare sparks flying off blade tips
            float2 spGrid = float2(wingU * 95.0, y * 12.0);
            float2 spCell = floor(spGrid);
            float2 spLocal = frac(spGrid) - 0.5;
            float spHash = Hash(spCell + Seed);
            float spDot = exp(-dot(spLocal, spLocal) * 48.0) * step(0.94, spHash) * Spark * cavityGlow;
            col += HotCol.rgb * spDot * 2.8;

            float topSoft = smoothstep(0.0, 0.14, y);
            col *= wingEdge * topSoft;

            // Strictly bounded HDR trail (no additive blowup; frozen clean in silence)
            float3 outCol = active > 0.5 ? lerp(max(col, trail * 0.80), col, bladeAlpha * 0.85) : col;
            float alpha = saturate(max(bladeAlpha * 0.96, max(cavityGlow * 0.72, veinLine)) * wingEdge * topSoft);
            return float4(outCol, active > 0.5 ? max(alpha, prev.a * 0.76) : alpha);
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

            float ca = (0.5 + 3.5 * Lock + 2.2 * Spark) * Dip / Size.x;
            float4 c0 = Last.Sample(Linear, uv);
            float r = Last.Sample(Linear, saturate(uv + float2(ca, 0))).r;
            float b = Last.Sample(Linear, saturate(uv - float2(ca, 0))).b;
            float3 hdr = float3(r, c0.g, b);

            float2 px = float2(Dip / Size.x, Dip / Size.y);
            float3 bloom = 0;
            bloom += Last.Sample(Linear, saturate(uv + float2(px.x * 3.5, 0))).rgb * 0.25;
            bloom += Last.Sample(Linear, saturate(uv - float2(px.x * 3.5, 0))).rgb * 0.25;
            bloom += Last.Sample(Linear, saturate(uv + float2(0, px.y * 2.5))).rgb * 0.25;
            bloom += Last.Sample(Linear, saturate(uv - float2(0, px.y * 2.5))).rgb * 0.25;
            hdr += max(bloom - 0.48, 0.0) * 0.52;

            float grain = (Hash(floor(position.xy) + floor(Phase * 20.0)) - 0.5) * 0.015;
            float3 graded = AcesTone(pow(max(hdr, 0.0), 1.42) * 1.50) + grain;

            float topSoft = smoothstep(0.0, 0.16, uv.y);
            float luma = dot(graded, float3(0.299, 0.587, 0.114));
            float alpha = saturate(max(c0.a * 0.62, smoothstep(0.02, 0.30, luma)) * wingEdge * topSoft) * Fade;

            return float4(saturate(graded) * alpha, alpha);
        }
        """;
}
