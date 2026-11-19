using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// Deep-net corridor dive: on each wing the camera plunges down a volumetric cyber-tunnel whose vanishing core sits at
/// the dock edge. Sculpted, beveled obsidian-cyan data monoliths line the tunnel walls with illuminated circuit cores
/// and depth-fog shading, while luminous floor and ceiling data highways rush outward. A kick surges the dive and
/// launches blazing magenta holographic ICE portal gates toward the viewer; a snare ignites bright telemetry lanes.
/// In silence the dive freezes cleanly with its volumetric tunnel intact.
/// </summary>
public sealed class NetdiveGpuScene : FeedbackGpuScene
{
    private const int ConstantFloats = 64;
    private static readonly Color Face = Color.FromRgb(0x06, 0x16, 0x1E);
    private static readonly Color Rain = Color.FromRgb(0x1C, 0xE2, 0xCC);
    private static readonly Color Hot = Color.FromRgb(0xDE, 0xFF, 0xF8);
    private static readonly Color Gate = Color.FromRgb(0xFF, 0x20, 0x9C);
    private static readonly Color Far = Color.FromRgb(0xFC, 0xE2, 0x42);

    private readonly float[] _gates = [-1, -1, -1, -1];
    private readonly float[] _gateAge = [9, 9, 9, 9];
    private int _nextGate;
    private float _travel, _surge, _snare;

    public NetdiveGpuScene() : base(ConstantFloats, Shared + FeedbackPs, Shared + ShowPs)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        float dt = frame.Dt;
        var audio = frame.Audio;
        if (Beat.Hit)
        {
            if (Beat.Snare)
            {
                _snare = 1f;
            }
            else
            {
                _surge = 1f;
                _gates[_nextGate] = _travel + 12f;
                _gateAge[_nextGate] = 0;
                _nextGate = (_nextGate + 1) % _gates.Length;
            }
        }

        _surge *= MathF.Exp(-dt * 2.8f);
        _snare *= MathF.Exp(-dt * 4.5f);

        if (audio.Level > .015f)
        {
            _travel += dt * (1.2f + (3.2f * audio.Level) + (4.5f * _surge));
        }

        for (int i = 0; i < _gates.Length; i++)
        {
            _gateAge[i] += audio.Level > .015f ? dt : dt * 3f;
            if (_gates[i] >= 0 && (_gates[i] < _travel + .25f || _gateAge[i] > 4.5f)) _gates[i] = -1;
        }

        if (_travel > 5000f)
        {
            _travel -= 4000f;
            for (int i = 0; i < _gates.Length; i++)
            {
                if (_gates[i] >= 0) _gates[i] -= 4000f;
            }
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
        d[7] = _surge;
        d[8] = _travel;
        d[9] = _snare;
        d[10] = MathF.Pow(.82f, step);
        d[11] = frame.Audio.Bass;
        for (int i = 0; i < 4; i++) d[12 + i] = _gates[i] < 0 ? -1f : _gates[i] - _travel;
        PutBands(16, frame);
        var rain = Lead(Rain);
        Put(40, Theme is null ? Face : Mix(Colors.Black, rain, .14f));
        Put(44, rain);
        Put(48, Theme is null ? Hot : Mix(rain, Colors.White, .78f));
        Put(52, Second(Gate));
        Put(56, Far);
        for (int i = 0; i < 4; i++) d[60 + i] = Math.Clamp(1.4f - (_gateAge[i] / 2.8f), 0, 1);
    }

    private const string Shared = """
        cbuffer Netdive : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float DockLeft;
            float DockRight;
            float Level;
            float Surge;
            float Travel;
            float SnareFlash;
            float Decay;
            float Bass;
            float4 GateDist;
            float4 Bands[6];
            float4 FaceColor;
            float4 RainColor;
            float4 HotColor;
            float4 GateColor;
            float4 FarColor;
            float4 GateFade;
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

            // Perspective corridor coordinates: vanishing point sits near the dock edge (wingU = 0)
            float2 vp = float2(0.04, 0.52);
            float2 rel = float2(wingU - vp.x, (uv.y - vp.y) * 0.95);
            float radialX = max(rel.x, 0.018);
            float radialY = abs(rel.y);

            // Distance along tunnel (depth z: large near dock vanishing point, small at outer screen edge)
            float wallZ = 1.15 / radialX;
            float floorZ = 0.42 / max(radialY, 0.02);
            bool isWall = wallZ < floorZ * 1.45;
            float z = isWall ? wallZ : floorZ;

            float worldZ = z + Travel;
            float depthFog = exp(-z * 0.14);
            float farCore = exp(-length(float2(rel.x * 3.8, rel.y * 2.4)) * 3.2);
            float band = Band(saturate(wingU * 0.92 + 0.04));

            float3 col = FaceColor.rgb * (0.45 + 0.65 * depthFog);
            // Warm golden-cyan vanishing core at the dock edge
            col += lerp(RainColor.rgb, FarColor.rgb, 0.55) * farCore * (0.55 + 0.75 * Bass + 0.9 * Surge);

            if (isWall)
            {
                // Sculpted beveled data monolith panels along the rushing tunnel wall
                float zCell = floor(worldZ * 1.25);
                float zFrac = frac(worldZ * 1.25);
                float yScaled = (uv.y - 0.08) * 3.4;
                float yCell = floor(yScaled);
                float yFrac = frac(yScaled);

                float hPanel = Hash(float2(zCell, yCell + side * 9.0));
                float panelBand = Band(saturate(frac(hPanel * 3.7) * 0.85 + 0.05));

                // Beveled panel inset & glowing circuit seam
                float edgeZ = min(zFrac, 1.0 - zFrac);
                float edgeY = min(yFrac, 1.0 - yFrac);
                float bevel = smoothstep(0.02, 0.14, min(edgeZ, edgeY));
                float seamGlow = exp(-min(edgeZ, edgeY) * 28.0);

                // Illuminated internal data bars on active panels
                float dataBar = smoothstep(0.18, 0.35, yFrac) * smoothstep(0.82, 0.65, yFrac)
                    * step(0.35, hPanel) * smoothstep(0.15, 0.85, sin(zFrac * 18.0 + hPanel * 6.0) * 0.5 + 0.5);

                float3 panelFace = FaceColor.rgb * (0.65 + 0.85 * bevel * (0.6 + 0.4 * hPanel));
                panelFace += RainColor.rgb * (seamGlow * (0.48 + 0.85 * panelBand) + dataBar * (0.28 + 0.75 * panelBand));
                panelFace += HotColor.rgb * seamGlow * dataBar * (0.4 + 0.8 * Surge);
                panelFace += GateColor.rgb * step(0.82, hPanel) * bevel * (0.25 + 0.75 * band);

                col = lerp(col, panelFace * (0.42 + 0.78 * depthFog), 0.92);
            }
            else
            {
                // Perspective floor & ceiling data highways
                float laneU = rel.x / max(radialY, 0.03) * 0.45;
                float laneLine = exp(-abs(frac(laneU * 2.2) - 0.5) * 28.0);
                float crossLine = exp(-abs(frac(worldZ * 0.9) - 0.5) * 24.0);
                float highway = max(laneLine * 0.85, crossLine * 0.65);

                col += RainColor.rgb * highway * depthFog * (0.42 + 0.85 * band + 0.95 * SnareFlash);
                col += HotColor.rgb * laneLine * crossLine * depthFog * (0.55 + 1.1 * SnareFlash);
            }

            // Blazing magenta holographic ICE portal gates rushing past on every kick
            float gateArr[4] = { GateDist.x, GateDist.y, GateDist.z, GateDist.w };
            float fadeArr[4] = { GateFade.x, GateFade.y, GateFade.z, GateFade.w };
            [unroll]
            for (int g = 0; g < 4; g++)
            {
                float gd = gateArr[g];
                if (gd > 0.05)
                {
                    float gPlane = exp(-abs(z - gd) * 3.8) * fadeArr[g];
                    float frameEdge = smoothstep(0.12, 0.44, abs(uv.y - 0.52)) + exp(-abs(z - gd) * 14.0);
                    col += GateColor.rgb * gPlane * (0.55 + 1.35 * frameEdge);
                    col += HotColor.rgb * exp(-abs(z - gd) * 18.0) * fadeArr[g] * 1.25;
                }
            }

            // Floating volumetric speed motes
            float moteRow = floor(uv.y * 16.0);
            float motePhase = frac(wingU * 7.0 - Travel * (0.55 + 0.35 * Hash(float2(moteRow, side))) + Hash(float2(moteRow, 3.1)));
            float mote = exp(-pow((motePhase - 0.5) * 34.0, 2.0)) * exp(-abs(frac(uv.y * 16.0) - 0.5) * 18.0);
            col += lerp(RainColor.rgb, HotColor.rgb, 0.6) * mote * (0.35 + 0.85 * Level + 0.9 * Surge);

            float topSoft = smoothstep(0.0, 0.14, uv.y);
            col *= wingEdge * topSoft;

            float3 outCol = active > 0.5 ? max(col, trail * 0.80) : col;
            float alpha = saturate((0.76 + 0.18 * depthFog) * wingEdge * topSoft);
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

            float ca = (0.45 + 3.2 * Surge + 2.0 * SnareFlash) * Dip / Size.x;
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
            hdr += max(bloom - 0.45, 0.0) * 0.55;

            float grain = (Hash(floor(position.xy) + floor(Travel * 16.0)) - 0.5) * 0.014;
            float3 graded = AcesTone(pow(max(hdr, 0.0), 1.34) * 1.42) + grain;

            float topSoft = smoothstep(0.0, 0.16, uv.y);
            float luma = dot(graded, float3(0.299, 0.587, 0.114));
            float alpha = saturate(max(c0.a * 0.62, smoothstep(0.02, 0.28, luma)) * wingEdge * topSoft) * Fade;

            return float4(saturate(graded) * alpha, alpha);
        }
        """;
}
