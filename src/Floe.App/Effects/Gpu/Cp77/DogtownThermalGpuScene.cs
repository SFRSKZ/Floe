using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace Floe.App.Effects.Gpu.Cp77;

/// <summary>
/// Dogtown FLIR (3D LiDAR Recon Point-Cloud Terrain + Minimal 1px Stabilized Reticle): 28,800 GPU-instanced
/// micro-particles sculpt an Ironbow thermal elevation ridge scan rising calmly with smoothed frequency plumes,
/// shifting organically through cold indigo cold-pockets, cyan anomalies, thermal violet and amber-white peaks,
/// overlaid in CompositePs with a razor-sharp gimbal-stabilized 1px MIL-SPEC targeting reticle.
/// </summary>
public sealed class DogtownThermalGpuScene : PointCloudGpuScene
{
    private const int ParticleCount = 28800; // 2 wings * 150 cols * 96 depth rows
    private const int ConstantFloats = 64;

    private static readonly Color ColdIndigo = Color.FromRgb(0x16, 0x12, 0x62);
    private static readonly Color FlirViolet = Color.FromRgb(0x88, 0x16, 0xAE);
    private static readonly Color FlirOrange = Color.FromRgb(0xFF, 0x5C, 0x0E);
    private static readonly Color ReconCyan = Color.FromRgb(0x0A, 0xB8, 0xE6);
    private static readonly Color HotOlive = Color.FromRgb(0xE8, 0xF2, 0x86);

    private int _lockedBand = 8;
    private float _phase, _targetU = .38f, _goalU = .38f, _lockHold, _kickAge = 9f, _snareAge = 9f, _seed = 1f;

    public DogtownThermalGpuScene() : base(ParticleCount, ConstantFloats, VertexShader, null, CompositeShader)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        float dt = Math.Clamp(frame.Dt, 0f, 0.1f);
        var audio = frame.Audio;
        Smoother.Update(frame, attack: 6.0f, release: 2.2f, peakDecay: 0.40f);
        _kickAge += dt;
        _snareAge += dt;
        _lockHold = Math.Max(0f, _lockHold - dt);

        if (audio.Level > .015f)
        {
            if (_lockHold <= 0f)
            {
                int bestIdx = _lockedBand;
                float bestVal = -1f;
                for (int i = 2; i <= 21; i++)
                {
                    if (Smoother.Bands[i] > bestVal)
                    {
                        bestVal = Smoother.Bands[i];
                        bestIdx = i;
                    }
                }

                if (Smoother.Bands[bestIdx] > (Smoother.Bands[_lockedBand] * 1.42f) + 0.05f)
                {
                    _lockedBand = bestIdx;
                    _goalU = Math.Clamp((bestIdx + .5f) / 24f, 0.16f, 0.84f);
                    _lockHold = 2.6f;
                }
            }

            float diff = _goalU - _targetU;
            float vel = Math.Clamp(diff * 1.25f, -0.08f, 0.08f);
            _targetU += vel * dt;
            _phase = (_phase + (dt * (.13f + (.30f * Smoother.Level) + (.16f * Smoother.Bass)))) % 10000f;
        }

        if (BeatHit)
        {
            _seed = (_seed + 1f) % 997f;
            if (BeatSnare) _snareAge = 0f;
            else _kickAge = 0f;
        }
    }

    protected override void Fill(GpuFrame frame)
    {
        var d = Data;
        bool crown = frame.DockLeft >= frame.DockRight;
        d[0] = frame.Width;
        d[1] = frame.Height;
        d[2] = frame.PixelsPerDip;
        d[3] = Math.Clamp(frame.Fade, 0f, 1f);
        d[4] = crown ? 1f : Math.Clamp(frame.DockLeft, .05f, .9f);
        d[5] = crown ? 0f : Math.Clamp(frame.DockRight, d[4] + .02f, .95f);
        d[6] = Smoother.Level;
        d[7] = crown ? 1f : 0f;
        d[8] = Smoother.Bass;
        d[9] = Smoother.Treble;
        d[10] = _phase;
        d[11] = BeatPulse;
        d[12] = _kickAge;
        d[13] = _snareAge;
        d[14] = _targetU;
        d[15] = _seed;
        d[16] = d[17] = d[18] = d[19] = 0f;
        Smoother.WriteBands(d, 20);

        Put(44, ColdIndigo);
        Put(48, FlirViolet);
        Put(52, Lead(FlirOrange));
        Put(56, ReconCyan);
        Put(60, Second(HotOlive));
    }

    private const string VertexShader = """
        cbuffer DogtownCloud : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float DockLeft;
            float DockRight;
            float Level;
            float CrownMode;
            float Bass;
            float Treble;
            float Phase;
            float BeatPulse;
            float KickAge;
            float SnareAge;
            float TargetU;
            float Seed;
            float4 Params;
            float4 Bands[6];
            float4 IndigoCol;
            float4 VioletCol;
            float4 OrangeCol;
            float4 CyanCol;
            float4 HudCol;
        };

        """ + SharedHeader + """

        float ThermalElevation(float u, float v, float band)
        {
            float n1 = Noise(float2(u * 7.5, v * 5.2 + Phase * 0.32));
            float n2 = Noise(float2(u * 14.0 - Phase * 0.22, v * 10.0));
            float plume = n1 * 0.62 + n2 * 0.38;
            return saturate((0.24 + 0.95 * band + 0.28 * Bass) * (0.48 + 0.72 * plume));
        }

        VSOut main(uint vid : SV_VertexID, uint iid : SV_InstanceID)
        {
            VSOut o;
            const uint COLS = 150u;
            const uint ROWS = 96u;
            uint perWing = COLS * ROWS;
            uint wingIdx = min(iid / perWing, 1u);
            uint rem = iid - wingIdx * perWing;
            uint cIdx = rem % COLS;
            uint rIdx = rem / COLS;

            float xMin, xMax, side, totalCols;
            if (CrownMode > 0.5)
            {
                xMin = 0.0;
                xMax = Size.x;
                side = 1.0;
                cIdx = wingIdx * COLS + cIdx;
                totalCols = (float)(COLS * 2u);
            }
            else if (wingIdx == 0u)
            {
                xMin = 0.0;
                xMax = DockLeft * Size.x;
                side = -1.0;
                totalCols = (float)COLS;
            }
            else
            {
                xMin = DockRight * Size.x;
                xMax = Size.x;
                side = 1.0;
                totalCols = (float)COLS;
            }

            float u = ((float)cIdx + 0.5) / totalCols;
            float v = ((float)rIdx + 0.5) / (float)ROWS;
            float acousticU = (CrownMode > 0.5) ? abs(u - 0.5) * 2.0 : (wingIdx == 0u ? (1.0 - u) : u);
            float band = Band(saturate(acousticU * 0.92 + 0.04));

            float h = ThermalElevation(u, v, band);
            float hBack = ThermalElevation(u, saturate(v + 0.03), band);
            float slope = saturate(0.5 + (h - hBack) * 5.0);

            float depthZ = lerp(0.95, 2.65, v);
            float persp = 1.0 / depthZ;
            float wingW = max(xMax - xMin, 10.0);
            float pxX = 0.5 * (xMin + xMax) + (u - 0.5) * wingW * lerp(1.0, 0.74, v);
            float groundY = lerp(Size.y * 0.90, Size.y * 0.34, pow(v, 0.85));
            float pxY = groundY - h * Size.y * 0.72 * persp;

            // Organic thermal color pockets + localized cold-cyan Recon anomalies
            float coldPocket = smoothstep(0.62, 0.86, Noise(float2(u * 4.0 - Phase * 0.12 + (float)wingIdx * 4.1, v * 3.8 + Phase * 0.10)));
            float3 col = lerp(IndigoCol.rgb, VioletCol.rgb, smoothstep(0.08, 0.34, h));
            col = lerp(col, OrangeCol.rgb * 1.25, smoothstep(0.28, 0.62, h));
            col = lerp(col, CyanCol.rgb, coldPocket * (0.42 + 0.35 * (1.0 - h)));
            col = lerp(col, float3(1.0, 0.94, 0.80), smoothstep(0.54, 0.88, h * slope) * 0.72);

            float energy = (0.12 + 0.32 * pow(slope, 1.25) + 0.22 * h + 0.14 * BeatPulse) * persp * lerp(1.20, 0.58, v);
            o.Color = col * energy * (0.48 + 0.78 * Level);

            float radiusPx = lerp(1.65, 1.02, v) * Dip;
            o.Pos = ProjectParticle(float2(pxX, pxY), radiusPx, vid, o.LocalUv);
            return o;
        }
        """;

    private static readonly string CompositeShader = """
        cbuffer DogtownCloud : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float DockLeft;
            float DockRight;
            float Level;
            float CrownMode;
            float Bass;
            float Treble;
            float Phase;
            float BeatPulse;
            float KickAge;
            float SnareAge;
            float TargetU;
            float Seed;
            float4 Params;
            float4 Bands[6];
            float4 IndigoCol;
            float4 VioletCol;
            float4 OrangeCol;
            float4 CyanCol;
            float4 HudCol;
        };

        Texture2D HdrTex : register(t0);
        SamplerState Linear : register(s0);

        """ + Cp77ShaderLib.SdfPrimitives + Cp77ShaderLib.CoreHelpers + """

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 sp = position.xy;
            float2 uv = sp / Size;
            float wingU, wingEdge, side;
            if (WingInfo(uv.x, wingU, wingEdge, side) < 0.5) return float4(0, 0, 0, 0);

            float4 c0 = HdrTex.Load(int3((int2)sp, 0));
            float3 hdr = c0.rgb;
            float peak = max(hdr.r, max(hdr.g, hdr.b));
            float3 graded = AcesToneClean(hdr * 1.18);
            graded = saturate(graded + smoothstep(1.15, 3.6, peak) * float3(0.28, 0.22, 0.16));

            // Whisper-thin 1px MIL-SPEC stabilized reticle & bottom azimuth ruler (no clutter!)
            float xMin, xMax;
            WingBoundsPx(uv.x, xMin, xMax);
            float padX = floor(10.0 * Dip);
            float x0 = xMin + padX;
            float x1 = xMax - padX;
            float targX = side < 0.0 ? lerp(xMax, xMin, TargetU) : lerp(xMin, xMax, TargetU);
            targX = clamp(targX, x0 + 26.0 * Dip, x1 - 26.0 * Dip);
            float2 targPos = float2(floor(targX), floor(Size.y * 0.50));
            float2 td = sp - targPos;

            float boxH = floor((18.0 - 1.5 * BeatPulse) * Dip);
            float brackets = CornerBracketsPx(sp, targPos - float2(boxH, boxH * 0.72), targPos + float2(boxH, boxH * 0.72), floor(6.0 * Dip), 1.0);
            float crossH = StrokePx(td.y, 1.0) * step(4.5 * Dip, abs(td.x)) * step(abs(td.x), 11.0 * Dip);
            float crossV = StrokePx(td.x, 1.0) * step(4.5 * Dip, abs(td.y)) * step(abs(td.y), 9.0 * Dip);

            float botY = floor(Size.y - 4.0 * Dip);
            float minorT, majorT;
            TickRulerPx(sp.x - x0, 6.0 * Dip, 5.0, minorT, majorT);
            float az = (minorT * step(botY - 3.0 * Dip, sp.y) + majorT * step(botY - 5.0 * Dip, sp.y)) * step(sp.y, botY) * step(x0, sp.x) * step(sp.x, x1);

            float hud = saturate(brackets * 0.72 + (crossH + crossV) * 0.65 + az * 0.45);
            graded = lerp(graded, lerp(HudCol.rgb, float3(1, 1, 1), 0.30 * BeatPulse), hud);

            float topSoft = smoothstep(0.0, 0.12, uv.y);
            float colPeak = max(graded.r, max(graded.g, graded.b));
            float alpha = saturate(max(smoothstep(0.004, 0.14, colPeak), max(smoothstep(0.01, 0.25, c0.a) * 0.72, hud * 0.75))) * wingEdge * topSoft * Fade;
            return float4(saturate(graded) * alpha, alpha);
        }
        """;
}
