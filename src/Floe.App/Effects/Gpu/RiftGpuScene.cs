using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// Blackwall dimensional rift: the horizon tears open along both wings into two undulating white-hot plasma shores,
/// framing a volumetric crimson-violet energy chasm strung with vertical harmonic filaments and bordered by electric
/// cyan chromatic ghosts. Bass and kick violently widen the tear and send a gravitational shockwave outward; snare
/// discharges sharp electric-cyan arc shafts across both shores. In silence the rift narrows to a poised glowing seam.
/// </summary>
public sealed class RiftGpuScene : FeedbackGpuScene
{
    private const int ConstantFloats = 60;
    private static readonly Color Core = Color.FromRgb(0xFF, 0xEE, 0xF5);
    private static readonly Color Body = Color.FromRgb(0xFF, 0x1A, 0x6C);
    private static readonly Color Deep = Color.FromRgb(0x28, 0x04, 0x18);
    private static readonly Color Ghost = Color.FromRgb(0x20, 0xEC, 0xFF);

    private readonly Random _random = new(23);
    private float _open, _spray, _shaft, _time, _kickAge = 9;
    private int _seed = 1;

    public RiftGpuScene() : base(ConstantFloats, Shared + FeedbackPs, Shared + ShowPs)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        float dt = frame.Dt;
        var audio = frame.Audio;
        _kickAge += dt;

        _open = Math.Max(_open * MathF.Exp(-dt * 3.2f), Math.Max(audio.Bass * .85f, Beat.Pulse));
        if (Beat.Hit && !Beat.Snare)
        {
            _kickAge = 0;
            _spray = 1f;
        }
        else
        {
            _spray *= MathF.Exp(-dt * 4.2f);
        }

        if (Beat.Snare || (Beat.Hit && _random.Next(2) == 0))
        {
            _seed = (_seed % 997) + 1 + _random.Next(30);
            _shaft = 1f;
        }

        _shaft *= MathF.Exp(-dt * 3.2f);

        if (audio.Level > .015f)
        {
            _time = (_time + (dt * (.75f + (2.0f * audio.Level) + (1.2f * _open)))) % 10000;
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
        d[7] = _open;
        d[8] = _spray;
        d[9] = _shaft;
        d[10] = _seed;
        d[11] = _time;
        d[12] = _kickAge;
        d[13] = MathF.Pow(.83f, step);
        d[14] = audio.Bass;
        d[15] = audio.Treble;
        PutBands(16, frame);

        var body = Lead(Body);
        Put(40, Theme is null ? Core : Mix(body, Colors.White, .85f));
        Put(44, body);
        Put(48, Theme is null ? Deep : Mix(Colors.Black, body, .28f));
        Put(52, Second(Ghost));
        Put(56, Color.FromRgb(0xFF, 0xA8, 0x30));
    }

    private const string Shared = """
        cbuffer Rift : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float DockLeft;
            float DockRight;
            float Level;
            float Open;
            float Spray;
            float Shaft;
            float Seed;
            float Time;
            float KickAge;
            float Decay;
            float Bass;
            float Treble;
            float4 Bands[6];
            float4 CoreCol;
            float4 BodyCol;
            float4 DeepCol;
            float4 GhostCol;
            float4 WarmCol;
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
            float3 trail = prev.rgb * (active > 0.5 ? Decay : 0.94);

            float y = uv.y;
            float band = Band(saturate(wingU * 0.92 + 0.04));
            float topSoft = smoothstep(0.0, 0.16, y);

            // Travelling gravitational shockwave from dock edge on kick
            float shockPos = KickAge * 1.40;
            float shockEnv = exp(-KickAge * 3.6);
            float shock = exp(-pow((wingU - shockPos) * 14.0, 2.0)) * shockEnv;

            // Undulating rift center & spectrum-sculpted opening envelope (widest near dock, tapering outward)
            float taper = pow(saturate(1.0 - wingU * 0.72), 0.75);
            float centerY = 0.52
                + 0.09 * sin(wingU * 9.0 - Time * 1.6 + side * 0.5) * (0.35 + 0.65 * band)
                + 0.04 * cos(wingU * 19.0 + Time * 2.3) * (0.25 + 0.75 * Treble);

            float halfGap = (0.045 + 0.18 * band + 0.11 * Open + 0.09 * shock) * (0.45 + 0.55 * taper);
            float topShore = centerY - halfGap;
            float botShore = centerY + halfGap;

            float dTop = abs(y - topShore);
            float dBot = abs(y - botShore);
            float dShore = min(dTop, dBot);

            float insideRift = smoothstep(topShore - 0.015, topShore + 0.025, y)
                * smoothstep(botShore + 0.015, botShore - 0.025, y);

            // Outer volumetric plasma aurora surrounding the rift
            float outerAura = exp(-dShore * (5.2 - 1.5 * Open));
            float3 col = DeepCol.rgb * (0.45 + 0.75 * outerAura);
            col += BodyCol.rgb * outerAura * (0.22 + 0.42 * band + 0.35 * Open);

            // Inside the dimensional tear: deep dark event-horizon abyss + fine harmonic plasma threads
            if (insideRift > 0.001)
            {
                float normY = saturate((y - topShore) / max(botShore - topShore, 0.01));
                float filaments = pow(0.5 + 0.5 * sin(wingU * 95.0 - Time * 2.4 + sin(normY * 6.283) * 1.2), 9.0);
                float coreRibbon = exp(-abs(normY - 0.5) * 11.0);

                float3 voidCol = DeepCol.rgb * 0.18;
                voidCol += BodyCol.rgb * (coreRibbon * 0.24) * (0.45 + 0.85 * band);
                voidCol += GhostCol.rgb * filaments * (0.38 + 0.95 * band + 0.75 * shock);
                voidCol += CoreCol.rgb * coreRibbon * shock * 0.95;

                col = lerp(col, voidCol, insideRift);
            }

            // Blazing white-hot shore curves + offset electric-cyan chromatic ghost fringes
            float shoreCore = exp(-dShore * 58.0);
            float shoreHalo = exp(-dShore * 13.0);
            float ghostTop = exp(-abs(y - (topShore - 0.028 - 0.015 * Open)) * 42.0);
            float ghostBot = exp(-abs(y - (botShore + 0.028 + 0.015 * Open)) * 42.0);

            col += BodyCol.rgb * (shoreHalo * 0.65 + shoreCore * 1.45) * (0.65 + 0.75 * band + 0.85 * shock);
            col += CoreCol.rgb * shoreCore * (0.75 + 1.15 * Open + 1.25 * shock);
            col += GhostCol.rgb * (ghostTop + ghostBot) * (0.55 + 0.85 * band + 0.95 * Spray);

            // Snare vertical electric plasma arc shafts leaping off the shores
            float shaftGrid = wingU * 18.0;
            float shaftId = floor(shaftGrid);
            float shaftLocal = abs(frac(shaftGrid) - 0.5);
            float shaftPick = step(0.76, Hash(float2(shaftId, Seed + side * 5.0)));
            float shaftBeam = exp(-shaftLocal * 36.0) * shaftPick * Shaft * exp(-dShore * 5.5);
            col += lerp(GhostCol.rgb, CoreCol.rgb, 0.45) * shaftBeam * 2.2;

            col *= wingEdge * topSoft;
            float3 outCol = active > 0.5 ? max(col, trail * 0.82) : col;
            float alpha = saturate(max(outerAura * 0.74, max(insideRift * 0.90, max(shoreHalo, shaftBeam))) * wingEdge * topSoft);
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

            float ca = (0.45 + 3.2 * Spray + 2.2 * Shaft) * Dip / Size.x;
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
            hdr += max(bloom - 0.46, 0.0) * 0.54;

            float grain = (Hash(floor(position.xy) + floor(Time * 20.0)) - 0.5) * 0.014;
            float3 graded = AcesTone(pow(max(hdr, 0.0), 1.46) * 1.45) + grain;

            float topSoft = smoothstep(0.0, 0.22, uv.y);
            float luma = dot(graded, float3(0.299, 0.587, 0.114));
            float alpha = saturate(max(c0.a * 0.45, smoothstep(0.02, 0.30, luma)) * wingEdge * topSoft) * Fade;

            return float4(saturate(graded) * alpha, alpha);
        }
        """;
}
