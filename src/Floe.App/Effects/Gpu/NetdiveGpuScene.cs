using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// Deep-net corridor dive: on each wing the camera plunges down a dark wireframe tunnel whose vanishing point sits at
/// the dock edge. Its ribs, wall rows and floor lanes are one pixel lines that fade out before they crowd into a moire;
/// a few wall panels glow with the song. A kick speeds the dive, lights the ribs where the song is loud and sends a
/// hard edged magenta ICE gate frame toward the viewer; a snare lights the floor lanes. In silence the dive stops.
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
        // a one pixel line on a repeating coordinate, faded out where the lines crowd closer than a few pixels so
        // the vanishing point goes dark instead of turning into a moire
        float GridLine(float c, float widthPx)
        {
            float fw = max(fwidth(c), 1e-5);
            float d = abs(frac(c - 0.5) - 0.5) / fw;
            return saturate(widthPx + 0.5 - d) * saturate(1.0 - fw * 3.5);
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 uv = position.xy / Size;
            float wingU, wingEdge, side;
            if (WingInfo(uv.x, wingU, wingEdge, side) < 0.5)
            {
                return float4(0, 0, 0, 0);
            }

            // Perspective corridor: the vanishing point sits at the dock edge (wingU = 0)
            float2 vp = float2(0.04, 0.52);
            float2 rel = float2(wingU - vp.x, (uv.y - vp.y) * 0.95);
            float radialX = max(rel.x, 0.018);
            float radialY = max(abs(rel.y), 0.02);
            float wallZ = 1.15 / radialX;
            float floorZ = 0.42 / radialY;
            float isWall = step(wallZ, floorZ * 1.45);
            float z = lerp(floorZ, wallZ, isWall);
            float worldZ = z + Travel;
            float fog = exp(-z * 0.16);
            float band = Band(saturate(wingU * 0.92 + 0.04));
            float kick = Surge * Surge;

            // every coordinate's derivative is taken here, outside any branch
            // the depth jumps where wall meets floor, so each surface's lines take their own smooth depth
            float rib = lerp(GridLine((floorZ + Travel) * 0.9, 0.55), GridLine((wallZ + Travel) * 0.9, 0.55), isWall);
            float seamC = log(wallZ / (floorZ * 1.45));
            float corner = saturate(1.0 - abs(seamC) / max(fwidth(seamC), 1e-5)) * saturate(1.0 - fwidth(seamC) * 2.0);
            float wallRow = GridLine(rel.y * wallZ * 2.2, 0.5);
            float floorLane = GridLine(rel.x / radialY * 12.0, 0.5);
            float cellZ = floor(worldZ * 0.9);

            // a dark tunnel; the wall's panels are flat faces a shade apart, some of them lit by the song
            float3 col = FaceColor.rgb * (0.18 + 0.30 * fog);
            float row = floor(rel.y * wallZ * 2.2);
            float h = Hash(float2(cellZ, row + side * 9.0));
            float panelBand = Band(saturate(frac(h * 3.7) * 0.85 + 0.05));
            float panelLit = isWall * step(0.62, h) * saturate((panelBand - 0.12) * 2.2);
            col += RainColor.rgb * panelLit * (0.03 + 0.10 * kick) * fog;

            // the frame of the tunnel: ribs rush toward you, rows on the walls and lanes on the floor run to the core
            float lines = max(rib, lerp(floorLane, wallRow, isWall) * 0.75);
            col *= 1.0 - saturate(lines * 2.0) * 0.6;
            float3 ink = lerp(RainColor.rgb, HotColor.rgb, saturate(kick * band * 1.5));
            col += ink * rib * (0.30 + 0.9 * band + 1.6 * kick * band) * fog;
            col += RainColor.rgb * corner * (0.35 + 0.6 * band) * saturate(fog * 2.0);
            col += RainColor.rgb * lerp(floorLane * (0.30 + 0.9 * SnareFlash), wallRow * 0.22, isWall) * saturate(fog * 1.6);

            // a kick sends a hard edged ICE gate frame down the tunnel toward you
            float gateArr[4] = { GateDist.x, GateDist.y, GateDist.z, GateDist.w };
            float fadeArr[4] = { GateFade.x, GateFade.y, GateFade.z, GateFade.w };
            float fz = max(lerp(fwidth(floorZ), fwidth(wallZ), isWall), 1e-4);
            [unroll]
            for (int g = 0; g < 4; g++)
            {
                float gd = gateArr[g];
                if (gd > 0.05)
                {
                    float frame = saturate(1.6 - abs(z - gd) / fz);
                    float inner = saturate(1.6 - abs(z - gd * 1.08) / fz) * 0.5;
                    col += GateColor.rgb * (frame + inner) * fadeArr[g] * 1.4 * saturate(fog * 2.5);
                }
            }

            // speed motes: short hard dashes streaking out of the core
            float moteRow = floor(uv.y * 16.0);
            float motePhase = frac(wingU * 7.0 - Travel * (0.55 + 0.35 * Hash(float2(moteRow, side))) + Hash(float2(moteRow, 3.1)));
            float rowPx = abs(frac(uv.y * 16.0) - 0.5) * Size.y / 16.0;
            float mote = step(0.965, motePhase) * saturate(0.6 * Dip + 0.5 - rowPx) * step(0.55, Hash(float2(moteRow, floor(wingU * 7.0 - Travel * (0.55 + 0.35 * Hash(float2(moteRow, side))) + Hash(float2(moteRow, 3.1))))));
            col += lerp(RainColor.rgb, HotColor.rgb, 0.4) * mote * (0.12 + 0.4 * Level + 0.5 * Surge);

            float topSoft = smoothstep(0.0, 0.14, uv.y);
            col *= wingEdge * topSoft;
            float alpha = saturate(0.88 * wingEdge * topSoft);
            return float4(col, alpha);
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

            float ca = 0;
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

            float grain = (Hash(floor(position.xy) + floor(Travel * 16.0)) - 0.5) * 0.014;
            float3 graded = AcesTone(pow(max(hdr, 0.0), 1.34) * 1.42) + grain;

            float topSoft = smoothstep(0.0, 0.16, uv.y);
            float luma = dot(graded, float3(0.299, 0.587, 0.114));
            float alpha = saturate(max(c0.a * 0.62, smoothstep(0.02, 0.28, luma)) * wingEdge * topSoft) * Fade;

            return float4(saturate(graded) * alpha, alpha);
        }
        """;
}
