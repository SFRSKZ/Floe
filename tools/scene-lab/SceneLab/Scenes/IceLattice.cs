using Floe.App.Effects.Gpu;
using Color = System.Windows.Media.Color;

namespace SceneLab.Scenes;

/// <summary>
/// ICE hex lattice. ONE IDEA: the strip is a Netrunner ICE wall — a hard honeycomb wire mesh; a kick drives a
/// fracture from an impact point that shatters the cells along its path, bass welds the cracks shut, and the top of
/// the spectrum pops new cells out of the lattice.
/// <para>
/// Rejected faults: not a static illustration (the fracture front travels, the cells along it break and are welded
/// again — the mesh is only the wall it happens on), not dock-blind (the mesh is continuous and the impacts land at
/// the far left and right ends, so the dock can eat the middle without breaking the reading), not unmeasured (kick =
/// fracture event, bass = weld rate, treble = cell birth, snare = a red lattice flash), no gratuitous decoration
/// (hard 2 px wire over a 1 px near-black edge, no bloom, no blur, no background gradient).
/// </para>
/// <para>
/// Port notes: same y-down SV_Position as the app, nothing flipped; the honeycomb is computed in strip fractions
/// except the cell size, so another strip width still tiles. A y-up port only needs no change at all — the mesh is
/// isotropic; the only directional thing is that impacts land at a random height. The feedback pass is empty: the
/// field is analytic and silence freezes the cracked mesh in place.
/// </para>
/// </summary>
[SceneDoc("ICE hex lattice: kick starts a travelling fracture across the honeycomb, bass welds it shut, treble births new cells")]
public sealed class IceLattice : FeedbackGpuScene
{
    private const int ConstantFloats = 92; // 16 scalars + 4 cracks + 4 crack states + 3 births + pad + 7 colours

    private static readonly Color Core = Color.FromRgb(0xFF, 0xFF, 0xFF);
    private static readonly Color Ice = Color.FromRgb(0x02, 0xD7, 0xF2);
    private static readonly Color Hot = Color.FromRgb(0xFC, 0xEE, 0x0A);
    private static readonly Color Warm = Color.FromRgb(0xFF, 0x4A, 0x18);
    private static readonly Color Alarm = Color.FromRgb(0xFF, 0x00, 0x3C);
    private static readonly Color Ink = Color.FromRgb(0x03, 0x05, 0x0A);
    private static readonly Color Dim = Color.FromRgb(0x0B, 0x4E, 0x5E);

    private readonly float[] _cx = new float[4];
    private readonly float[] _cy = new float[4];
    private readonly float[] _ca = new float[4];
    private readonly float[] _cl = new float[4];
    private readonly float[] _cs = new float[4];
    private readonly float[] _cage = new float[4];
    private readonly float[] _cseed = new float[4];

    private readonly float[] _bx = new float[3];
    private readonly float[] _by = new float[3];
    private readonly float[] _bage = new float[3];
    private readonly float[] _bsize = new float[3];

    private readonly Random _random = new(2077);
    private float _prevKick, _prevSnare, _prevHat, _prevTreble = .5f;
    private float _repair, _warn, _idle;
    private int _next;

    public IceLattice() : base(ConstantFloats, Shared + FeedbackPs, Shared + ShowPs)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        float dt = frame.Dt;
        var audio = frame.Audio;
        bool silent = audio.Level <= .004f;
        float sdt = silent ? 0 : dt;

        bool kickOn = audio.Kick > .45f && _prevKick <= .45f;
        bool snareOn = audio.Snare > .40f && _prevSnare <= .40f;
        bool hatOn = audio.Hat > .50f && _prevHat <= .50f;
        bool trebleOn = audio.Treble > .70f && _prevTreble <= .70f;
        _prevKick = audio.Kick;
        _prevSnare = audio.Snare;
        _prevHat = audio.Hat;
        _prevTreble = audio.Treble;

        if (kickOn) SpawnCrack();
        if (snareOn) _warn = 1;
        if (trebleOn || (hatOn && _random.NextDouble() < .5)) SpawnBirth();

        _warn *= MathF.Exp(-dt * 6f);
        if (_warn < .04f) _warn = 0;

        // bass welds the fractures: the more low end, the faster a crack heals and cools to cyan
        float weld = Math.Clamp((audio.Bass - .30f) * 1.8f, 0, 1);
        _repair += ((silent ? 0 : weld) - _repair) * (1 - MathF.Exp(-sdt * 7f));

        for (int i = 0; i < 4; i++)
        {
            _cage[i] += dt;
            if (sdt > 0 && _cs[i] > 0)
            {
                _cs[i] *= MathF.Exp(-sdt * (.9f + (2.8f * audio.Bass)));
                if (_cs[i] < .05f) _cs[i] = 0;
            }
        }

        for (int i = 0; i < 3; i++) _bage[i] += dt;

        // standby: the mesh loses a couple of levels while the track is dead, then nothing moves at all
        if (silent) _idle = MathF.Min(1, _idle + (dt / 1.1f));
        else _idle = MathF.Max(0, _idle - (dt * 6));
    }

    private void SpawnCrack()
    {
        int i = _next;
        _next = (_next + 1) % 4;
        bool left = _random.Next(2) == 0;
        float w = 3840, h = 126;
        _cx[i] = left ? w * .075f : w * .925f;
        _cy[i] = 14 + ((float)_random.NextDouble() * (h - 28));
        float aim = ((float)_random.NextDouble() - .5f) * 1.05f;
        _ca[i] = left ? aim : MathF.PI + aim;
        _cl[i] = w * (.30f + ((float)_random.NextDouble() * .22f));
        _cs[i] = 1;
        _cage[i] = 0;
        _cseed[i] = 1 + ((float)_random.NextDouble() * 900);
    }

    private void SpawnBirth()
    {
        int i = _random.Next(3);
        bool left = _random.Next(2) == 0;
        _bx[i] = left
            ? 70 + ((float)_random.NextDouble() * 900)
            : 2870 + ((float)_random.NextDouble() * 900);
        _by[i] = 14 + ((float)_random.NextDouble() * 98);
        _bage[i] = 0;
        _bsize[i] = 5 + ((float)_random.NextDouble() * 4);
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
        d[12] = .84f - (.02f * _idle); // mesh brightness, settles in standby
        d[13] = _repair;
        d[14] = _warn;
        d[15] = 0;

        for (int i = 0; i < 4; i++)
        {
            d[16 + (i * 4)] = _cx[i];
            d[17 + (i * 4)] = _cy[i];
            d[18 + (i * 4)] = _ca[i];
            d[19 + (i * 4)] = _cl[i];
            d[32 + (i * 4)] = _cs[i];
            d[33 + (i * 4)] = _cage[i];
            d[34 + (i * 4)] = _cseed[i];
            d[35 + (i * 4)] = 0;
        }

        for (int i = 0; i < 3; i++)
        {
            d[48 + (i * 4)] = _bx[i];
            d[49 + (i * 4)] = _by[i];
            d[50 + (i * 4)] = _bage[i];
            d[51 + (i * 4)] = _bsize[i];
        }

        d[60] = 0; d[61] = 0; d[62] = 0; d[63] = 0;

        Put(64, Core);
        Put(68, Ice);
        Put(72, Hot);
        Put(76, Warm);
        Put(80, Alarm);
        Put(84, Ink);
        Put(88, Dim);
    }

    private const string Shared = """
        cbuffer Ice : register(b0)
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
            float MetaX;
            float MetaY;
            float MetaZ;
            float MetaW;
            float4 Crack[4];
            float4 CrackState[4];
            float4 Birth[3];
            float4 Pad;
            float4 Core;
            float4 Ice;
            float4 Hot;
            float4 Warm;
            float4 Alarm;
            float4 Ink;
            float4 Dim;
        };

        """ + ShaderHelpers;

    private const string FeedbackPs = """

        // The mesh is analytic: nothing is carried between frames, so silence holds the cracked wall dead still.
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

        float HexDist(float2 q)
        {
            q = abs(q);
            return max(dot(q, float2(.5, .8660254)), q.x);
        }

        float2 HexGv(float2 uv)
        {
            float2 r = float2(1.0, 1.7320508);
            float2 h = r * .5;
            float2 a = frac(uv / r) * r - h;
            float2 b = frac((uv - h) / r) * r - h;
            return dot(a, a) < dot(b, b) ? a : b;
        }

        // 0 at a hex edge, .5 at a cell centre (uv units)
        float HexEdge(float2 uv)
        {
            return .5 - HexDist(HexGv(uv));
        }

        // the travelling fracture: a jagged polyline of five segments, only the head reached so far is drawn
        float CrackDist(float2 p, float4 c, float4 s)
        {
            float front = min(c.w, s.y * 2200);
            if (front < 4) return 1e5;
            float2 q = c.xy;
            float ang = c.z;
            float seg = front / 5;
            float d = 1e5;
            for (int j = 0; j < 5; j++)
            {
                float2 q2 = q + float2(cos(ang), sin(ang)) * seg;
                d = min(d, SdSeg(p, q, q2));
                q = q2;
                ang += (Hash(float2(s.z, j * 1.7)) - .5) * 1.5;
            }

            return d;
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 p = position.xy;
            float3 col = 0;
            float a = 0;
            float cell = Size.y * .27;
            float2 uv = p / cell;
            float2 id = uv - HexGv(uv);
            float2 centre = id * cell;

            // 1) how deep each cell sits inside the fractures, and how hot the fracture line itself is
            float dmg = 0;
            float line = 1e5;
            float heat = 0;
            for (int i = 0; i < 4; i++)
            {
                float4 c = Crack[i];
                float4 s = CrackState[i];
                if (s.x < .03) continue;
                float dc = CrackDist(centre, c, s);
                dmg = max(dmg, saturate(1 - (dc / (18 * Dip))) * s.x);
                line = min(line, CrackDist(p, c, s));
                heat = max(heat, saturate(1 - (s.y * 2.2)) * s.x);
            }

            // 2) the mesh: cells near a fracture shift and lose edges, so the wall reads as broken, not as a texture
            float shatter = dmg * (.55 + (.75 * Hash(id + 1.3)));
            float2 off = float2(Hash(id + 3.1) - .5, Hash(id + 7.7) - .5) * shatter * 12 * Dip;
            float edge = HexEdge((p + off) / cell);
            float thick = 1.5 * Dip / cell;
            float hole = step(.42, Hash(id + 9.9)) * step(.45, dmg);
            float3 mesh = lerp(Dim.rgb, Ice.rgb, .55);
            mesh = lerp(mesh, Warm.rgb, dmg * .85);
            mesh = lerp(mesh, Hot.rgb, saturate((dmg - .5) * 2.4));
            mesh = lerp(mesh, Core.rgb, heat * dmg * .9);
            mesh += Ice.rgb * MetaY * dmg * .45; // welding glows cyan along a healing crack
            mesh = lerp(mesh, Alarm.rgb, MetaZ * .8);
            mesh *= MetaX;
            if (hole < .5)
            {
                float dpx = (edge - thick) * cell;
                float ink = step(dpx, 1.05 * Dip);
                col = lerp(col, Ink.rgb, ink);
                a = max(a, ink);
                col = lerp(col, mesh, step(dpx, 0));
                a = max(a, step(dpx, 0) * .95);
            }

            // 3) the fracture itself: a white-hot head that cools to orange and then to a cyan weld
            for (int i = 0; i < 4; i++)
            {
                float4 s = CrackState[i];
                if (s.x < .03) continue;
                float4 c = Crack[i];
                float d = CrackDist(p, c, s);
                float width = (1.0 + (1.8 * s.x)) * Dip;
                float hot = saturate(1 - (s.y * 2.0));
                float3 cc = lerp(Warm.rgb, Hot.rgb, hot);
                cc = lerp(cc, Core.rgb, saturate(1 - (s.y * 9)));
                cc = lerp(Ice.rgb, cc, saturate(s.x * 1.5));
                cc *= 1 - (MetaY * .55 * (1 - s.x)); // a welding fracture cools toward the ice colour
                if (s.y < .14)
                {
                    // the impact: a short burst of cells around the origin
                    float burst = saturate(1 - (s.y / .14));
                    float2 q = (p - c.xy) / (18 * Dip);
                    float ring = abs(.5 - HexDist(q * 2)) ;
                    if (ring < .12) cc = lerp(cc, Core.rgb, burst);
                }

                Stroke(col, a, d - width, cc);
            }

            // 4) treble births: a new cell pops out of the wall, expands once and is gone
            for (int i = 0; i < 3; i++)
            {
                float4 b = Birth[i];
                if (b.w < 1 || b.z > .55) continue;
                float radius = (b.w + (b.z * 90)) * Dip;
                float2 q = (p - b.xy) / radius;
                float ring = abs(.5 - HexDist(q * 1.15));
                float born = saturate(1 - (b.z / .55));
                float3 bc = lerp(Ice.rgb, Core.rgb, born * .6);
                Stroke(col, a, (ring - .10) * radius, bc * (.35 + (.65 * born)));
            }

            // 5) a thin field boundary keeps the strip from floating: hard 1 px line, cut by the dock without loss
            {
                float m = min(min(p.y, Size.y - p.y), min(p.x, Size.x - p.x));
                float border = abs(m - (2.5 * Dip)) - (.5 * Dip);
                Stroke(col, a, border, lerp(Dim.rgb, Ice.rgb, .35) * MetaX);
            }

            return float4(col * a, a) * Fade;
        }
        """;
}
