namespace Floe.App.Effects.Gpu.Desktop;

/// <summary>
/// The Blackwall as Phantom Liberty shows it: a towering curtain of red and magenta light lines, and in front of
/// it a floor of red points in long rows running up to its foot. The lines move like the strand curtain's: each
/// is a bundle of three that sways where it is lit and comes apart in places. A kick makes the wall flare where
/// the song is loud and sends the light up its lines. The floor ignores the music: it only breathes, with a
/// point flaring here and there. In silence the wall glows low and its lines hang still.
/// </summary>
public sealed class BlackwallDesktopScene : DesktopPointScene
{
    // the wall: lines and dots per line; the floor: columns and points per column
    private const int WallLines = 900, Bundle = 3, WallDots = 480, FloorCols = 300, FloorRows = 120;
    private const int Wall = WallLines * Bundle * WallDots, Floor = FloorCols * FloorRows;
    private const int Waves = 4;

    // when each kick hit (scene seconds) and how hard
    private readonly float[] _waveAt = { -99, -99, -99, -99 };
    private readonly float[] _waveAmp = new float[Waves];
    private int _next;
    private float _lastKick, _gap, _music, _calm;

    // each wall dot carries its violet ghost: one shader run lights both, the geometry shader draws both quads
    protected override int Instances => Wall + Floor;

    protected override bool ExpandPoints => true;

    protected override string ExpandShader => """
        struct PairOut
        {
            float4 Pos : SV_Position;
            float2 Half : TEXCOORD2;
            float3 Color : COLOR0;
            float Sharp : TEXCOORD1;
            float4 GPos : TEXCOORD3;
            float2 GHalf : TEXCOORD4;
            float3 GColor : COLOR1;
            float GSharp : TEXCOORD5;
        };

        static const float2 Quad[4] = { float2(-1, -1), float2(1, -1), float2(-1, 1), float2(1, 1) };

        void Emit(float4 pos, float2 half, float3 color, float sharp, inout TriangleStream<DotOut> quad)
        {
            if (pos.z < 0. || max(color.r, max(color.g, color.b)) < 1e-5) return;
            [unroll] for (int k = 0; k < 4; k++)
            {
                DotOut o;
                o.Pos = float4(pos.xy + Quad[k] * half, pos.zw);
                o.Uv = Quad[k];
                o.Color = color;
                o.Sharp = sharp;
                o.Half = half;
                quad.Append(o);
            }
            quad.RestartStrip();
        }

        [maxvertexcount(8)]
        void main(point PairOut v[1], inout TriangleStream<DotOut> quad)
        {
            Emit(v[0].Pos, v[0].Half, v[0].Color, v[0].Sharp, quad);
            Emit(v[0].GPos, v[0].GHalf, v[0].GColor, v[0].GSharp, quad);
        }
        """;

    protected override void Advance(GpuFrame frame, float dt)
    {
        var a = frame.Audio;
        bool kick = a.Kick > .14f && _lastKick <= .14f;
        _lastKick = a.Kick;
        _gap += dt;
        if (kick && _gap > .2f)
        {
            _gap = 0;
            _waveAt[_next] = Clock;
            _waveAmp[_next] = Math.Clamp(.45f + a.Bass, .4f, 1.3f);
            _next = (_next + 1) % Waves;
        }

        float present = a.Level > .02f ? 1f : 0f;
        _music += (present - _music) * (1f - MathF.Exp(-dt * (present > _music ? 2f : .5f)));
        // the drift of the wall's light runs on its own clock, slower in silence
        _calm += dt * (.25f + (.75f * _music) + a.Level);

        var d = Data;
        d[12] = _music;
        d[13] = _calm;
        d[14] = 0;
        d[15] = 0;
        for (int i = 0; i < Waves; i++)
        {
            d[56 + i] = Clock - _waveAt[i];
            d[60 + i] = _waveAmp[i];
        }

        d[64] = frame.Height / 1300f;
        Camera(frame);
    }

    // a slow look along the wall from the floor, drifting back and forth; the music never moves it
    private void Camera(GpuFrame frame)
    {
        float sway = .5f - (.5f * MathF.Cos(Clock * 2 * MathF.PI / 70f));
        float rise = .5f - (.5f * MathF.Cos(Clock * 2 * MathF.PI / 43f));
        var eye = new[] { Mix(-2.4f, -.6f, sway), Mix(.7f, 1.05f, rise), Mix(3.4f, 2.9f, sway) };
        // turned toward the wall enough that its far end runs off the screen's right edge
        var aim = new[] { Mix(2.2f, 3.6f, sway), Mix(1.2f, 1.05f, rise), -1.4f };
        SetCamera(frame, eye, aim, 58);
        Data[65] = MathF.Sqrt(((eye[0] - aim[0]) * (eye[0] - aim[0])) + ((eye[1] - aim[1]) * (eye[1] - aim[1])) + ((eye[2] - aim[2]) * (eye[2] - aim[2])));
    }

    protected override string VertexShader => """
        static const uint NL = 900, NK = 3, NDW = 480, NC = 300, NR = 120;
        static const float Far = 24.;          // the wall and floor run from x = -3 out past the screen's edge

        // where along the wall a line or floor column u (0..1) sits: tighter near, wider out where it is small
        float AlongX(float u) { return -3. + (Far + 3.) * (.55 * u + .45 * u * u); }
        static const uint Wall = NL * NK * NDW;
        static const float Near = 6.;           // how far the floor runs out from the wall

        float Music() { return Own0.x; }

        // the wall's own light at (x, y): tall streaks that drift sideways and breathe, before the music
        float WallNoise(float x, float y, float t)
        {
            float3 q = float3(x * .9, y * .16 - t * .03, t * .06);
            return Snoise(q) * .6 + Snoise(q * float3(3.1, 1.4, 1.) + 4.2) * .3 + Snoise(float3(x * 9., y * .5, t * .2)) * .1;
        }

        // the dark of the distance: the wall and floor sink into it far down the line
        float Haze(float x) { return exp(-max(x - 4., 0.) * .085); }

        struct PairOut
        {
            float4 Pos : SV_Position;
            float2 Half : TEXCOORD2;
            float3 Color : COLOR0;
            float Sharp : TEXCOORD1;
            float4 GPos : TEXCOORD3;
            float2 GHalf : TEXCOORD4;
            float3 GColor : COLOR1;
            float GSharp : TEXCOORD5;
        };

        // a dot at p, size world units across: where it lands, how big, and how its light spreads off focus
        void Place(float3 p, float3 col, float size, out float4 pos, out float2 half, out float3 color, out float sharp)
        {
            float4 c = Project(p);
            float d = c.w;
            float px = Own3.x;
            float dot0 = size * px / max(d, .01);
            float coc = abs(d - Own3.y) / max(d, .01) * 9. * px;
            float sz = clamp(dot0 + coc, 1., 40.);
            color = col * dot0 * dot0 / (sz * sz);
            sharp = saturate(1. - coc / (dot0 * 2.));
            c.z = .5 * c.w;
            if (d < .05) c = float4(0., 0., -1., 1.);
            pos = c;
            half = sz / Size * c.w;
        }

        PairOut main(uint iid : SV_InstanceID)
        {
            float t = Own0.y;
            float music = Music();
            float3 p;
            float3 col;
            float size;
            // the ghost: none for the floor
            float3 gp = 0.;
            float3 gcol = 0.;
            float gsize = 1.;

            if (iid < Wall)
            {
                uint id = iid;
                // each line of the wall is a bundle of three strands, as in the strand curtain
                uint j = id % NDW;
                uint k = (id / NDW) % NK;
                uint i = id / (NDW * NK);
                float kz = (float)k - 1.;
                float jit = (Hash(float2(i, 1.7)) - .5) * .5;
                float wu = (i + .5 + jit) / NL;
                float x = AlongX(wu);
                float y = ((j + .5 + (k % 2) * .5) / NDW) * 3.6;

                // the wall's own light: tall streaks that drift sideways and breathe, pushed by the band under them
                float n = WallNoise(x, y, t);
                float e = Band(saturate(wu * 1.6));
                n += .12 + music * ((e - .3) * 1.1);

                // a kick: the wall flares where the song is loud and the light climbs the lines
                float flare = 0., climb = 0.;
                [unroll] for (int w = 0; w < 4; w++)
                {
                    float since = Own1[w];
                    if (since < 0. || since > 1.) continue;
                    float amp = Own2[w];
                    float spreadW = saturate(.25 + e * 1.4);
                    flare += amp * exp(-since * 4.) * (.4 + .6 * spreadW);
                    climb += amp * exp(-pow((y - since * 3.6) / .25, 2.)) * exp(-since * 1.6) * spreadW;
                }

                float I = smoothstep(.05, .75, n + flare * .25);
                float I2 = I * I;

                // the strand curtain's motion: where it is lit the bundle sways and ripples, and some of its
                // strands come apart, each on its own wave; a kick pulls more of them apart
                float rip = sin(y * 42. - t * 2.4 + x * 11.) * .4 + Snoise(float3(x * 9., y * 16., t * .6)) * .6;
                float sway = Snoise(float3(x * 2.4, y * 4.5, t * .25 + 3.));
                float ks = Hash(float2(i * 1.37 + k * 7.1, 3.));
                float own = Snoise(float3(x * 9. + ks * 40., y * (7. + 7. * ks), t * (.45 + .5 * ks)));
                float ownRip = sin(y * (30. + 25. * ks) - t * (1.3 + 1.6 * ks) + ks * 6.283);
                float scatter = saturate(flare * .8);
                float pick = smoothstep(.2 - .35 * scatter, .5 - .2 * scatter, Snoise(float3(x * 3.7, t * .11, 5.)));
                float spread = I * (.5 + .5 * I) * pick * (1. + .8 * scatter);
                float amp = .45 + flare * .5 + music * .15;
                float dx = kz * .0008 + I * (.0075 * rip + .016 * sway) * amp + spread * (.0075 * own + .004 * ownRip);
                // the camera looks along the wall, so a sway out of it reads large: kept small
                float dz = I * .018 * sway + spread * .008 * own;
                p = float3(x + dx, y, dz);

                float bright = .6 + .4 * music;
                float3 hot = lerp(float3(1., 0., .07), float3(1., .05, .42), smoothstep(.35, .9, I));
                col = (float3(.9, 0., .06) * .07 + hot * (I2 * 1.3 + I * .1 + climb * .7)) * bright * .55;
                // the lines melt into the dark at the top and at the foot, over a long ragged stretch, never cut off
                // along a straight edge; the shadow at the foot and the dark of the distance
                float rag = .35 * Snoise(float3(x * 1.7, kz * .3, t * .05)) + .15 * Hash(float2(i, 12.9));
                float top = smoothstep(3.6, 1.7, y + rag);
                float foot = smoothstep(.0, .85, y - .05 + rag * .5);
                float edge = top * top * foot * Haze(x);
                col *= edge * 1.5;
                size = 4.5;
                // a violet shadow just off each line, on the edge of the lit parts
                gp = p + float3(.012 + .02 * I, 0., .01);
                float rim = smoothstep(.04, .3, I) * (1. - smoothstep(.4, .75, I));
                gcol = float3(.12, .03, 1.) * (rim * .1 + I2 * .02 + .0006) * bright * .55 * edge;
                gsize = 7.;
            }
            else
            {
                // the floor: rows of red points running up to the wall's foot; the music leaves it alone, it only
                // breathes a little and here and there a point flares and goes out
                uint id = iid - Wall;
                uint r = id % NR;
                uint cI = id / NR;
                // scattered like grit on rough ground, not laid on a grid: each point jitters in both directions
                float jit = (Hash(float2(cI, r * 1.7 + 9.1)) - .5) * 1.6;
                float x = AlongX((cI + .5 + jit) / NC);
                float z = ((r + .5 + (Hash(float2(cI * 2.9, r + 4.4)) - .5) * 1.4) / NR) * Near;
                // the ground: low mounds and hollows, the light gathering in some patches and thinning in others
                float ground = Snoise(float3(x * .9, z * .7, 11.)) * .6 + Snoise(float3(x * 3.1, z * 2.6, 17.)) * .4;
                float patch = smoothstep(-.55, .6, ground);
                float h = .05 * ground + .02 * Snoise(float3(x * .6, z * .35, Time * .05));
                p = float3(x, h, z);
                float near = exp(-z * .45);
                float seed = Hash(float2(cI * 1.3, r + .7));
                float blink = 0.;
                if (seed > .985)
                {
                    float ph = Hash(float2(r, cI + 2.3)) * 6.283;
                    float rate = .35 + .5 * Hash(float2(cI, r + 5.1));
                    blink = pow(max(sin(Time * rate + ph), 0.), 24.);
                }
                // the wall's light falls on the floor in front of the bright streaks and fades away from it; only
                // the wall's slow drift moves it, never the beat
                float above = smoothstep(.15, .8, WallNoise(x, .6, t) + .12);
                float3 spill = lerp(float3(1., 0., .07), float3(1., .05, .4), above) * above * (.5 + above) * exp(-z * .45) * 2.2;
                // a dark gap between the wall's foot and the floor: the floor fades in from black away from the wall
                float gap = smoothstep(.05, 1.1, z);
                gap *= gap;
                col = (float3(1., .02, .09) * (.06 + near * .22) * .8 * gap * (.25 + .9 * patch) + float3(1., .02, .09) * blink * .9 * .8 + spill * gap * (.4 + .6 * patch)) * Haze(x);
                size = (9. + 6. * Hash(float2(r * 3.1, cI + .3))) + blink * 3.;
            }

            PairOut o;
            Place(p, col, size, o.Pos, o.Half, o.Color, o.Sharp);
            Place(gp, gcol, gsize, o.GPos, o.GHalf, o.GColor, o.GSharp);
            return o;
        }
        """;

    protected override string PixelShader => """
        float4 main(DotOut i) : SV_Target
        {
            float r2 = dot(i.Uv, i.Uv);
            if (r2 > 1.) discard;
            float soft = exp(-r2 * 3.);
            float hard = 1. - smoothstep(.5, .85, r2);
            return float4(i.Color * lerp(soft, hard, i.Sharp), 0.);
        }
        """;

    protected override string FinalShader => """
        Texture2D Hdr : register(t0);
        Texture2D Quarter : register(t1);
        Texture2D Sixteenth : register(t2);
        SamplerState Linear : register(s0);

        float4 main(float4 pos : SV_Position) : SV_Target
        {
            float2 uv = pos.xy / Size;
            float3 c = Hdr.Load(int3((int2)pos.xy, 0)).rgb + Quarter.Sample(Linear, uv).rgb * .5 + Sixteenth.Sample(Linear, uv).rgb * .4;
            c *= Expo;
            float3 m = 1. - exp(-c);
            // far past full the red bleeds toward pale pink, as the wall's hot spots do in the game
            float over = max(c.r - 1.4, 0.);
            m = lerp(m, float3(1., .82, .96), 1. - exp(-over * .35));
            return float4(pow(max(m, 0.), .95) * Fade, 1.);
        }
        """;
}
