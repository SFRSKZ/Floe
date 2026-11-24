namespace Floe.App.Effects.Gpu.Desktop;

/// <summary>
/// The Relic biochip as Ben Andrews' concept draws it: a slab of resin seen at a slant, down its middle a double
/// band of living cells whose dendrites run out of the bands in long, nearly parallel red fibres over a fine mat of
/// green and violet capillaries, and between the bands Arasaka's wordmark and the chip's status line. Arasaka's
/// circuitry is hidden in the resin and draws itself trace by trace a little ahead of where the camera looks. The
/// song is laid along the chip as on the Blackwall, low notes to the left: each stretch of cells and fibres glows
/// with its own band, new fibres grow out to the rim where the song is loud and melt back into the dark where it falls quiet, and a
/// kick flashes the loud stretches at once.
/// A hat makes the pads glint. In silence the chip lies dim and still.
/// </summary>
public sealed class RelicDesktopScene : DesktopPointScene
{
    // keep in step with the shader's counts
    private const int Cells = 2 * 1500, FibreDots = 250, Traces = 6, TraceDots = 3600, Branches = 260, BranchDots = 80, Edges = 2 * 5000, Dust = 60000;
    private const int Veins = 2600, VeinDots = 100;
    private const int Waves = 8;

    private readonly float[] _waveAt = { -99, -99, -99, -99, -99, -99, -99, -99 };
    private readonly float[] _label = RelicLabel.Build();
    private int _next;
    private float _lastKick, _gap, _music, _calm, _glint;

    protected override int Instances => (Cells * (1 + FibreDots)) + (Traces * 2 * TraceDots) + (Branches * 2 * BranchDots) + (Branches * 4) + Edges + Dust + (Veins * VeinDots) + (_label.Length / 4);

    protected override float[]? Points => _label;

    private float _labelFrom => LabelSpan().From;
    private float _labelTo => LabelSpan().To;
    private (float From, float To)? _span;

    private (float From, float To) LabelSpan()
    {
        if (_span is { } known) return known;
        float from = float.MaxValue, to = float.MinValue;
        for (int i = 0; i + 3 < _label.Length; i += 4)
        {
            from = Math.Min(from, _label[i]);
            to = Math.Max(to, _label[i]);
        }

        _span = from <= to ? (from, to) : (0f, 0f);
        return _span.Value;
    }

    protected override void Advance(GpuFrame frame, float dt)
    {
        var a = frame.Audio;
        bool kick = a.Kick > .14f && _lastKick <= .14f;
        _lastKick = a.Kick;
        _gap += dt;
        if (kick && _gap > .22f)
        {
            _gap = 0;
            _waveAt[_next] = Clock;
            _next = (_next + 1) % Waves;
        }

        float present = a.Level > .02f ? 1f : 0f;
        _music += (present - _music) * (1f - MathF.Exp(-dt * (present > _music ? 2f : .5f)));
        _calm += dt * (.2f + (.6f * _music) + (.6f * a.Level));
        _glint = Math.Max(_glint * MathF.Exp(-dt * 9f), a.Hat);

        var d = Data;
        d[12] = _music;
        d[13] = _calm;
        d[14] = _glint;
        d[15] = 0;
        // how long ago each wave set off; they take turns starting from either end of the chip
        for (int i = 0; i < Waves; i++) d[56 + i] = Clock - _waveAt[i];

        d[64] = frame.Height / 1300f;
        // where the label lies along the chip, so the circuit passes under it
        d[68] = _labelFrom;
        d[69] = _labelTo;
        Camera(frame);
    }

    // looking down the slab at a slant from one fixed place; the music never moves it
    private void Camera(GpuFrame frame)
    {
        const float sway = .105f, lean = .283f;
        var eye = new[] { Mix(-2.6f, -.8f, sway), Mix(-1.9f, -1.5f, lean), Mix(2.6f, 3f, lean) };
        var aim = new[] { Mix(-.2f, 1.7f, sway), .15f, 0f };
        SetCamera(frame, eye, aim, 50);
        Data[65] = MathF.Sqrt(((eye[0] - aim[0]) * (eye[0] - aim[0])) + ((eye[1] - aim[1]) * (eye[1] - aim[1])) + ((eye[2] - aim[2]) * (eye[2] - aim[2])));
        // the circuit draws itself trace by trace along the chip, left to right, over the first minute
        Data[70] = -6f + (Clock * .25f);
    }

    protected override string VertexShader => """
        StructuredBuffer<float4> Points : register(t0);

        static const uint NCell = 2 * 1500, NF = 250, NT = 6, NTD = 3600, NB = 260, NBD = 80, NE = 2 * 5000, ND = 60000;
        static const uint NV = 2600, NVD = 100;
        static const uint ACell = NCell * (1 + NF);
        static const uint ATrace = ACell + NT * 2 * NTD;
        static const uint ABranch = ATrace + NB * 2 * NBD;
        static const uint APad = ABranch + NB * 4;
        static const uint AEdge = APad + NE;
        static const uint ADust = AEdge + ND;
        static const uint AVein = ADust + NV * NVD;
        static const float Half = 9.;           // the chip runs -Half..Half along x
        static const float Width = 1.25;        // and -Width..Width across
        static const float Speed = 11.;         // how fast a wave runs along the chip

        float Music() { return Own0.x; }

        float SinceStart(int w) { return w < 4 ? Own1[w & 3] : Own2[w & 3]; }

        // the song laid along the chip as on the Blackwall: low notes at the left of the view, high at the right
        float Energy(float x) { return Band(saturate((x + 5.) / 11.)); }

        // how much of a kick reaches x: the loud parts of the song take it, the quiet ones barely stir
        float Reach(float x) { return saturate(.15 + Energy(x) * 1.5); }

        // a kick flashes the loud parts of the chip at once, and they fade out quickly
        float Pulse(float x, float delay, float sharp)
        {
            float lit = 0.;
            [unroll] for (int w = 0; w < 8; w++)
            {
                float since = SinceStart(w) - delay;
                if (since < 0. || since > 1.5) continue;
                lit += exp(-since * 5.);
            }
            return lit * Reach(x);
        }

        // the same kick, fading slowly: the warmth it leaves on the circuit
        float Reveal(float x)
        {
            float lit = 0.;
            [unroll] for (int w = 0; w < 8; w++)
            {
                float since = SinceStart(w);
                if (since < 0. || since > 3.) continue;
                lit += exp(-since * 1.4) * (.55 + .45 * exp(-since * 12.));
            }
            return lit * Reach(x);
        }

        // how far the circuit's drawing front, lagging by lag, has passed x: formed behind it, a bright head on it
        float Built(float x, float lag, out float head)
        {
            float d = Own4.z - lag - x;
            head = exp(-pow(d / .06, 2.));
            return smoothstep(-.01, .02, d);
        }

        // a cell's own glow: the band of the song under it, breathing slowly
        float CellGlow(float x, float seed, float t)
        {
            float e = Energy(x);
            float n = Snoise(float3(x * .7, seed * 3., t * .15));
            return saturate(.15 + .45 * n + Music() * (e - .2) * 1.4);
        }

        // the cell s: where it sits, which band, its seed
        float3 CellAt(uint s, out float seed, out float side)
        {
            uint band = s & 1u;
            uint k = s >> 1;
            seed = Hash(float2(k, band * 7. + 1.));
            float x = ((k + .5 + (seed - .5) * .8) / (NCell / 2)) * 2. * Half - Half;
            float y = (band == 0 ? -.2 : .26) + (Hash(float2(k, band + 4.)) - .5) * .12 + .05 * Snoise(float3(x * .4, band, 2.));
            side = band == 0 ? -1. : 1.;
            // the bands part around the label
            y += side * .12 * smoothstep(-.4, .2, x) * (1. - smoothstep(2.4, 3., x));
            return float3(x, y, .02 + .04 * Hash(float2(k, band + 13.)));
        }

        // the hidden circuit runs in the slit between the cell bands: a bundle of NT parallel traces that steps up
        // and down in 45 degree jogs all together, and branches leaving it diagonally across the bands
        static const float SlitY = .03, Lane = .021, Jog = .045, Cell = .38;
        float BundleY(float x)
        {
            float c = floor(x / Cell);
            float l0 = floor(Hash(float2(c - 1., 3.1)) * 3.) - 1.;
            float l1 = floor(Hash(float2(c, 3.1)) * 3.) - 1.;
            float f = saturate((x - c * Cell) / (abs(l1 - l0) * Jog + 1e-4));
            return SlitY + lerp(l0, l1, f) * Jog;
        }

        // the label sits over the circuit: no trace shows through it
        float Clear(float x) { return 1. - smoothstep(Own4.x - .12, Own4.x, x) * (1. - smoothstep(Own4.y, Own4.y + .12, x)); }

        // branch b: where it leaves the bundle, which way it runs, how far
        void Branch(uint b, out float2 from, out float2 dir, out float l1, out float l2)
        {
            float xb = (Hash(float2(b, 3.3)) * 2. - 1.) * (Half - .4);
            float side = Hash(float2(b, 5.5)) < .5 ? -1. : 1.;
            float run = Hash(float2(b, 7.7)) < .5 ? -1. : 1.;
            from = float2(xb, BundleY(xb) + side * (NT - 1) * .5 * Lane);
            dir = float2(run, side);
            l1 = .08 + .22 * Hash(float2(b, 9.9));
            l2 = .03 + .14 * Hash(float2(b, 11.1));
        }

        // a point s along branch b: a 45 degree run out of the bundle, then straight along the chip
        float2 BranchAt(float2 from, float2 dir, float l1, float s)
        {
            float d = s / 1.4142;
            return d < l1 ? from + dir * d : from + dir * l1 + float2(dir.x * (s - l1 * 1.4142), 0.);
        }

        // how far a fibre has grown (0..1): the song's band at its cell feeds it, so where the music is loud new
        // fibres shoot out and grow to the rim, and where it falls quiet they shrink back into the dark; in silence
        // a slow drift keeps some of them out
        float Grow(float x, float seed)
        {
            float level = .32 + .28 * Snoise(float3(x * .6, seed * 5., Own0.y * .06)) + Music() * Energy(x) * 1.4;
            return saturate((level - Hash(float2(seed * 7.7, 31.)) * .9) / .3);
        }

        // fibre which (0, 1 the two main ones, 2 a branch off the first) of the cell at soma, at u (0..1) along it;
        // the main fibres run from the cell to the slab's edge and end on it
        float3 DendriteAt(float3 soma, float seed, float side, uint which, float u, float t)
        {
            float s2 = which == 1u ? frac(seed * 3.17 + .41) : seed;
            float reach = Width - side * soma.y;
            float x0 = soma.x + (which == 1u ? (Hash(float2(seed * 13., 9.)) - .5) * .03 : 0.);
            float tilt = (Hash(float2(s2 * 37., 5.)) - .5) * .12 + .08 * Snoise(float3(soma.x * .35, side, 4.));
            float bend = (Hash(float2(s2 * 53., 8.)) - .5) * .08;
            float wig = .008 * Snoise(float3(u * 3., s2 * 20., t * .1)) * u;
            float2 at;
            if (which != 2u)
            {
                at = float2(x0 + reach * (tilt * u + bend * u * u) + wig, soma.y + side * reach * u);
            }
            else
            {
                float u0 = .08 + .3 * Hash(float2(seed * 71., 3.));
                float2 from = float2(soma.x + reach * (tilt * u0 + bend * u0 * u0), soma.y + side * reach * u0);
                float rest = reach * (1. - u0);
                float turn = Hash(float2(seed * 19., 6.)) < .5 ? -.16 : .16;
                at = float2(from.x + rest * (tilt + turn) * u + wig, from.y + side * rest * u);
            }

            // from the cell's depth to the rim's
            return float3(at, lerp(soma.z, .03, saturate(abs(at.y - soma.y) / reach)));
        }

        // root v (a green or violet capillary) grows out of one of the cells to the rim, at u along it
        float3 RootAt(uint v, float u, float t, out float x0, out float seed)
        {
            float side;
            float3 soma = CellAt((v * 7919u + 13u) % NCell, seed, side);
            seed = Hash(float2(v, 91.7));
            x0 = soma.x;
            float reach = Width - side * soma.y;
            float tilt = (Hash(float2(v, 47.1)) - .5) * .16 + .08 * Snoise(float3(x0 * .35, side, 4.));
            float bend = (Hash(float2(v, 61.2)) - .5) * .1;
            float wig = .008 * Snoise(float3(u * 3.4, v * .37, t * .1)) * u;
            float2 at = float2(x0 + reach * (tilt * u + bend * u * u) + wig, soma.y + side * reach * u);
            return float3(at, lerp(soma.z, .03, u));
        }

        DotOut main(uint vid : SV_VertexID, uint iid : SV_InstanceID)
        {
            float2 corner = Corners[min(vid, 5u)];
            float t = Own0.y;
            float music = Music();
            float bright = .6 + .4 * music;
            float3 p;
            float3 col;
            float size;
            float kind = 0.;    // 0 a plain dot, 1 a cell body, 2 a pad
            // the fibres and traces are thin lines from p to p2 instead of dots, lw their half width
            bool isLine = false;
            float3 p2 = 0.;
            float lw = 0.;

            if (iid < ACell)
            {
                uint s = iid / (1 + NF);
                uint j = iid % (1 + NF);
                float seed, side;
                float3 soma = CellAt(s, seed, side);
                float glow = CellGlow(soma.x, seed, t);
                // a cell fires as the wave passes it
                float fire = Pulse(soma.x, 0., .35);
                // the cells' colours run from red through magenta to violet, a few orange
                float3 hue = seed < .45 ? float3(1., .05, .35) : seed < .8 ? float3(.75, .1, 1.) : seed < .93 ? float3(1., .02, .08) : float3(1., .45, .12);
                if (j == 0)
                {
                    p = soma;
                    col = hue * (.12 + glow * .35 + fire * 1.6) * bright;
                    // a white heart when it fires hard
                    col += float3(1., .85, .9) * saturate(fire - .6) * .8 * bright;
                    size = 26. + 10. * seed;
                    kind = 1.;
                }
                else
                {
                    // the dendrites: two long fibres straight out of the band to the rim, nearly parallel to their
                    // neighbours, and a branch splitting off low down; each grows and shrinks with the song
                    uint f = j - 1;
                    uint which = f < 100u ? 0u : f < 200u ? 1u : 2u;
                    float n = which == 2u ? 50. : 100.;
                    float k = (float)(f - (which == 2u ? 200u : which * 100u));
                    float u0 = k / n, u1 = (k + 1.) / n;
                    float grow = Grow(soma.x, seed + which * .37);
                    p = DendriteAt(soma, seed, side, which, u0, t);
                    p2 = DendriteAt(soma, seed, side, which, u1, t);
                    // the grown part is drawn, its tip melting into the dark
                    float um = (u0 + u1) * .5;
                    float tip = 1. - smoothstep(grow - .3, grow, um);
                    float e = Energy(soma.x);
                    // a kick lights the fibres of the loud cells from the cell outward, all at once
                    float kick = Pulse(soma.x, 0., .35) * (1. - um);
                    col = float3(1., .04, .2) * (.05 + music * e * .3 + kick * .7) * (1. - .45 * um) * tip * bright;
                    if (um > grow) col = 0.;
                    isLine = true;
                    lw = 2.6;
                }
            }
            else if (iid < ATrace)
            {
                // the bundle: each trace drawn as two thin bright edges, as the art's traces are
                uint id = iid - ACell;
                uint k = id / (2 * NTD);
                uint e = (id / NTD) % 2;
                float x = (float)(id % NTD) / NTD * 2. * Half - Half;
                float x2 = x + 2. * Half / NTD;
                float off = ((float)k - (NT - 1) * .5) * Lane + (e == 0 ? -.0055 : .0055);
                p = float3(x, BundleY(x) + off, .045);
                p2 = float3(x2, BundleY(x2) + off, .045);
                // the traces form one after another as the drawing front passes; a kick only warms what is drawn
                float head;
                float built = Built(x, (float)k * .35, head);
                float lit = (built * (.16 + .3 * Reveal(x - .012 * k)) + head * 1.1) * Clear(x);
                col = float3(1., .12, .16) * lit * bright * step(.001, built + head);
                isLine = true;
                lw = 1.8;
            }
            else if (iid < ABranch)
            {
                uint id = iid - ATrace;
                uint b = id / (2 * NBD);
                uint e = (id / NBD) % 2;
                float2 from, dir;
                float l1, l2;
                Branch(b, from, dir, l1, l2);
                float len = l1 * 1.4142 + l2;
                float s0 = (float)(id % NBD) / NBD * len;
                float s1 = s0 + len / NBD;
                // the edge offset follows the segment it is on
                float2 n0 = s0 / 1.4142 < l1 ? normalize(float2(-dir.y, dir.x)) : float2(0., 1.);
                float2 n1 = s1 / 1.4142 < l1 ? normalize(float2(-dir.y, dir.x)) : float2(0., 1.);
                float eo = e == 0 ? -.0045 : .0045;
                float2 at = BranchAt(from, dir, l1, s0) + n0 * eo;
                float2 at2 = BranchAt(from, dir, l1, s1) + n1 * eo;
                p = float3(at, .045);
                p2 = float3(at2, .045);
                // the branch grows out of the bundle after the front has passed its root
                float grown = (Own4.z - (.6 + .9 * Hash(float2(b, 13.3))) - from.x) * 1.2;
                float built = smoothstep(s0 - .005, s0 + .005, grown);
                float head = exp(-pow((grown - s0) / .02, 2.)) * step(0., grown);
                float lit = (built * (.15 + .3 * Reveal(from.x)) + head * 1.1) * Clear(at.x);
                col = float3(1., .12, .16) * lit * bright * step(.001, built + head);
                isLine = true;
                lw = 1.6;
            }
            else if (iid < APad)
            {
                // where a branch ends, a white capsule pad laid along it; where it leaves the bundle, a small ring
                uint id = iid - ABranch;
                uint b = id / 4;
                uint n = id % 4;
                float2 from, dir;
                float l1, l2;
                Branch(b, from, dir, l1, l2);
                float len = l1 * 1.4142 + l2;
                float2 end = BranchAt(from, dir, l1, len);
                float grown = (Own4.z - (.6 + .9 * Hash(float2(b, 13.3))) - from.x) * 1.2;
                // the pads light as the branch reaches them, the ring as it sets off
                float atEnd = smoothstep(len - .01, len + .02, grown) * (.5 + .3 * Reveal(from.x)) + exp(-pow((grown - len) / .04, 2.)) * step(len - .01, grown);
                float atStart = smoothstep(-.01, .02, grown) * (.5 + .3 * Reveal(from.x)) + exp(-pow(grown / .04, 2.));
                float lit = n < 3 ? atEnd : atStart;
                float glint = Own0.z * step(.7, Hash(float2(b, 40.)));
                if (n < 3)
                {
                    p = float3(end + float2(dir.x * (.006 + n * .011), 0.), .05);
                    col = float3(1., .82, .84) * (lit * .9 + glint * lit) * Clear(end.x) * bright;
                    size = 15.;
                }
                else
                {
                    p = float3(from, .05);
                    col = float3(1., .55, .4) * lit * 1.1 * Clear(from.x) * bright;
                    size = 22.;
                    kind = 2.;
                }
            }
            else if (iid < AEdge)
            {
                // the slab's two edges, a thin bright rim of resin
                uint id = iid - APad;
                float sideE = id < NE / 2 ? -1. : 1.;
                float x = (((id % (NE / 2)) + .5) / (NE / 2)) * 2. * Half - Half;
                p = float3(x, sideE * Width, .03);
                col = float3(1., .08, .3) * (.05 + Pulse(x, 0., .6) * .3) * bright;
                size = 9.;
            }
            else if (iid < ADust)
            {
                // the matted fibre of the resin around everything, faint
                uint id = iid - AEdge;
                float a = Hash(float2(id, 1.3)), b = Hash(float2(id, 7.9)), c = Hash(float2(id, 4.4));
                float x = (a * 2. - 1.) * Half;
                float y = (b * 2. - 1.) * Width;
                p = float3(x, y, -c * .15);
                col = lerp(float3(.8, .1, .9), float3(1., .05, .15), c) * (.006 + Pulse(x, .15, .8) * .03) * bright;
                size = 6.;
            }
            else if (iid < AVein)
            {
                // the capillaries: thin green and violet roots growing out of the cell bands among the red
                // dendrites, long and nearly parallel like them, growing and shrinking with the song as they do
                uint id = iid - ADust;
                uint v = id / NVD;
                uint j = id % NVD;
                float x0, rs, x0b, rsb;
                p = RootAt(v, (float)j / NVD, t, x0, rs);
                p2 = RootAt(v, (float)(j + 1) / NVD, t, x0b, rsb);
                float u = (j + .5) / NVD;
                float3 hue = Hash(float2(v, 77.7)) < .55 ? float3(.2, 1., .5) : float3(.6, .2, 1.);
                float grow = Grow(x0, rs);
                float tip = 1. - smoothstep(grow - .3, grow, u);
                float e = Energy(x0);
                float kick = Pulse(x0, 0., .35) * (1. - u);
                col = hue * (.05 + music * e * .25 + kick * .5) * (1. - .45 * u) * tip * bright;
                if (u > grow) col = 0.;
                isLine = true;
                lw = 2.2;
            }
            else
            {
                // Arasaka's wordmark and the chip's status line, laid out on the CPU: x, y, weight, size
                float4 q = Points[iid - AVein];
                p = float3(q.xy, .05);
                float lit = Pulse(q.x, .05, .45) + .5 * Reveal(q.x);
                col = float3(1., .12, .2) * q.z * (.35 + lit * .8) * bright;
                size = q.w;
            }

            DotOut o;
            if (isLine)
            {
                // a thin line quad between the two ends, a pixel wider than the line for its soft edge
                float4 ca = Project(p), cB = Project(p2);
                bool atB = corner.x > 0.;
                float4 c = atB ? cB : ca;
                float2 halfSize = Size * .5;
                float2 sa = ca.xy / max(ca.w, .01), sb = cB.xy / max(cB.w, .01);
                float2 dpx = (sb - sa) * halfSize;
                float2 along = dpx / max(length(dpx), 1e-4);
                float2 across = float2(-along.y, along.x);
                float hw = lw * Own3.x / max(c.w, .01);
                // a soft profile never narrower than about a pixel and a half; a thinner line keeps its light by
                // dimming rather than breaking into stair steps
                float w = max(hw, 1.5);
                float thin = min(hw / w * 1.5, 1.);
                float ext = w * 2. + .5;
                float2 s2 = (atB ? sb : sa) + across * corner.y * ext / halfSize;
                o.Pos = float4(s2 * c.w, .5 * c.w, c.w);
                if (ca.w < .05 || cB.w < .05) o.Pos = float4(0., 0., -1., 1.);
                o.Uv = float2(corner.y * ext, w);
                o.Color = col * thin;
                o.Sharp = 8.;
                return o;
            }

            float4 cl = Project(p);
            float d = cl.w;
            float px = Own3.x;
            float dot0 = size * px / max(d, .01);
            float coc = abs(d - Own3.y) / max(d, .01) * 10. * px;
            float sz = clamp(dot0 + coc, 1., 60.);
            o.Color = col * dot0 * dot0 / (sz * sz);
            o.Sharp = kind > 0. ? kind + saturate(1. - coc / dot0) * .5 : saturate(1. - coc / (dot0 * 2.));
            cl.xy += corner * sz / Size * cl.w;
            cl.z = .5 * cl.w;
            if (d < .05) cl = float4(0., 0., -1., 1.);
            o.Pos = cl;
            o.Uv = corner;
            return o;
        }
        """;

    protected override string PixelShader => """
        float4 main(DotOut i) : SV_Target
        {
            if (i.Sharp > 5.)
            {
                // a line: full inside its half width, falling off over one pixel at its edges
                float q = i.Uv.x / i.Uv.y;
                float cover = exp(-q * q * 1.6);
                if (cover < .004) discard;
                return float4(i.Color * cover, 0.);
            }

            float r2 = dot(i.Uv, i.Uv);
            if (r2 > 1.) discard;
            if (i.Sharp >= 2.)
            {
                // a pad: a crisp ring and a hot heart
                float r = sqrt(r2);
                float ring = 1. - smoothstep(.06, .12, abs(r - .72));
                float heart = 1. - smoothstep(.22, .32, r);
                return float4(i.Color * (ring * .6 + heart * 2.2), 0.);
            }
            if (i.Sharp >= 1.)
            {
                // a cell body: a soft round with a brighter middle
                float body = 1. - smoothstep(.55, .95, r2);
                return float4(i.Color * body * (.6 + .8 * exp(-r2 * 6.)), 0.);
            }
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
            float3 c = Hdr.Load(int3((int2)pos.xy, 0)).rgb + Quarter.Sample(Linear, uv).rgb * .3 + Sixteenth.Sample(Linear, uv).rgb * .25;
            c *= Expo;
            float3 m = 1. - exp(-c);
            float over = max(max(c.r, c.b) - 1.6, 0.);
            m = lerp(m, float3(1., .86, .92), 1. - exp(-over * .3));
            return float4(pow(max(m, 0.), .95) * Fade, 1.);
        }
        """;
}
