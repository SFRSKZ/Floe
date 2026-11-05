using Floe.App.Effects.Gpu;
using Color = System.Windows.Media.Color;

namespace SceneLab.Scenes;

/// <summary>
/// Night City from across the bay, a cinematic wide: three parallax skylines sinking into coloured depth fog, a
/// horizon halo burning low behind them, volumetric shafts off the neon, the near city's windows split warm and
/// cool, and all of it carried in wet water — vertical smear, fresnel, ripple lines, sparkle. Everything is
/// authored in linear HDR (emissive above 1.0), rolled off through an ACES film curve, encoded to sRGB, graded
/// teal-shadow / warm-highlight, vignetted, chromatically split at the edges and grained.
/// <para>
/// The spectrum drives it: the low end raises the whole city as one mass, each district's windows burn with its own
/// band, the kick is a light wave that crosses the bay over about ten frames, the snare moves how many windows are
/// lit, the hats sparkle on the water. Every clock in the scene is fed by the music, so when the music stops the
/// water, the camera, the shafts and even the film grain all freeze: the skeleton of the city stays, nothing drifts.
/// </para>
/// <para>
/// The four traps it does not fall into: not a still illustration (mass, fog, wave and water all follow the low end,
/// and they hold perfectly still the moment the sound does); not blind to the dock gap (the four hero towers stand
/// at x ~.10 .23 .77 .90, nothing has to be read at x .5, and the bay runs under the dock); not unmeasured (mass =
/// the city's height, band = that district's windows, kick = the light wave, snare = window density, hat = sparkle);
/// not decorated (sky, three skylines, water, light: nothing else is in frame).
/// </para>
/// <para>
/// Port note: the shader's y runs down from the top row, so <c>main</c> converts twice — <c>g.y = 1 - uv.y</c> for the
/// design space and <c>px.y = Size.y - position.y</c> for the pixel grid. If the shipping target hands over a y-up
/// position, delete both flips; nothing else assumes a direction. The chromatic aberration lives in the show pass,
/// which resamples the field three times, so the field has to stay a texture in the port.
/// </para>
/// </summary>
[SceneDoc("Night City: three parallax skylines over a burning bay — HDR, ACES, fog, wet reflections")]
public sealed class Skyline : FeedbackGpuScene
{
    private const int ConstantFloats = 68; // a multiple of 4: the constant buffer is 16-byte aligned

    private float _mass;   // the low end, eased: how tall the whole city stands
    private float _wave;   // the kick, dying away over about ten frames: the light wave crossing the bay
    private float _spark;  // the hats, eased: how much the water sparkles
    private float _flow;   // the level, eased: the light's strength, and the camera's breath
    private float _clock;  // the camera and grain clock: only ever advanced by the music

    public Skyline() : base(ConstantFloats, Shared + FieldPs, Shared + ShowPs)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        var audio = frame.Audio;
        _mass += (Math.Clamp(Math.Max(audio.Bass, audio.Level * 0.6f), 0, 1) - _mass) * (1 - MathF.Exp(-frame.Dt * 10f));
        _spark += (Math.Clamp(audio.Hat, 0, 1) - _spark) * (1 - MathF.Exp(-frame.Dt * 12f));
        _flow += (Math.Clamp(audio.Level, 0, 1) - _flow) * (1 - MathF.Exp(-frame.Dt * 8f));

        // the kick is a wave with a tail, not one bright frame: it falls away over about ten frames at 30 fps
        _wave = Math.Max(_wave * MathF.Exp(-frame.Dt * 9f), audio.Kick);

        // the camera drifts and the grain is seeded from this clock, and only the music moves it: a held frame is
        // exactly still, film grain included
        _clock += frame.Dt * 2.0f * Math.Clamp(audio.Level, 0, 1);

        if (_mass < 0.001f) _mass = 0;
        if (_wave < 0.001f) _wave = 0;
        if (_spark < 0.001f) _spark = 0;
        if (_flow < 0.001f) _flow = 0;
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
        d[6] = _mass;
        d[7] = frame.Audio.Treble;
        d[8] = _wave;
        d[9] = frame.Audio.Snare;
        d[10] = _spark;
        d[11] = frame.Audio.Mid;
        PutBands(12, frame);                                  // 24 bands: 12..35
        Put(36, Lead(Color.FromRgb(0xFF, 0xD4, 0x00)));       // sodium yellow: the warm key
        Put(40, Color.FromRgb(0x00, 0xE5, 0xFF));             // cyan: cool windows, cool water
        Put(44, Color.FromRgb(0xFF, 0x2E, 0x88));             // magenta: the middle sky, signs
        Put(48, Color.FromRgb(0xFF, 0x2E, 0x40));             // red: warning beacons
        Put(52, Color.FromRgb(0x0A, 0x12, 0x1C));             // the near buildings' body, teal black
        Put(56, Color.FromRgb(0x14, 0x0A, 0x22));             // the top of the sky, deep violet
        Put(60, Color.FromRgb(0xFF, 0x8A, 0x36));             // the warm glow off the horizon
        d[64] = _clock;
        d[65] = _wave;
        d[66] = _flow;
        d[67] = 0;
    }

    private const string Shared = """
        cbuffer Sky : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float Time;
            float Level;
            float Mass;
            float Treble;
            float Kick;
            float Snare;
            float Spark;
            float Mid;
            float4 Bands[6];
            float4 Core;
            float4 Cyan;
            float4 Magic;
            float4 Alert;
            float4 Body;
            float4 Night;
            float4 Warm;
            float Clock;
            float Wave;
            float Flow;
            float Pad2;
        };

        """ + ShaderHelpers;

    private const string FieldPs = """

        // =============================================================================================
        // A skyline here is a height field, and every edge is resolved against the pixel footprint: no hard
        // 1 px steps, no flat colour blocks. Light is authored above 1.0 in linear space and only becomes a
        // picture at the very end, through an ACES curve and an sRGB encode.
        // =============================================================================================
        static const float GY = 0.24;   // the waterline, in strip heights above the bottom edge
        static const float DEEP = 79.0; // the far skyline
        static const float MIDB = 41.0; // the middle skyline
        static const float FAR = 24.0;  // the near districts: one per spectrum band
        static const float MS = 2.2;    // how far the reflection is squeezed into the water

        float Slab(float x, float a, float b)
        {
            return step(a, x) * step(x, b);
        }

        // an anti-aliased step: the edge is resolved over the pixel's own footprint
        float Soft(float d, float w)
        {
            return smoothstep(-w, w, d);
        }

        float3 DistrictHash(float fi)
        {
            return float3(Hash(float2(fi + 1.37, (fi * 0.71) + 3.10)),
                          Hash(float2((fi * 1.70) + 3.10, fi + 5.30)),
                          Hash(float2((fi * 2.30) + 7.70, (fi * 0.37) + 1.90)));
        }

        // the bay's edge is not a ruled line: a slow swell and a break in the middle distance
        float Horizon(float u)
        {
            return GY + (0.013 * sin(u * 7.3)) + (0.008 * sin((u * 2.4) + 1.7)) + (0.005 * sin((u * 21.0) + 0.4));
        }

        // one of the two background skylines: a dense mass, no windows, no branches, softened by its own fog
        float BackRoof(float u, float count, float seed, float base, float amp)
        {
            float fi = floor((u * count) + seed);
            float fx = frac((u * count) + seed);
            float band = Band(fi / (count - 1.0));
            float3 r = DistrictHash(fi);
            float h = base + (amp * r.z) + (0.05 * band) + (0.05 * Mass);
            h *= Slab(fx, 0.06 + (0.05 * r.x), 0.94 - (0.05 * r.y));
            h = max(h, (h + 0.03) * Slab(fx, 0.48, 0.50) * step(0.6, r.x));
            return h;
        }

        // the hero layer: 24 districts, one per band, with crowns, masts, plant rooms and hero towers
        float4 NearRoof(float u)
        {
            float fi = floor(u * FAR);
            float fx = frac(u * FAR);
            float band = Band(fi / 23.0);
            float3 r = DistrictHash(fi);
            float r0 = r.x;
            float r1 = r.y;
            float r2 = r.z;

            float tall = (0.22 + (0.22 * r2)) + (0.10 * Mass) + (0.24 * band) + (0.14 * Level);
            float hero = min(step(abs(fi - 2.0), 0.5) + step(abs(fi - 5.0), 0.5)
                             + step(abs(fi - 18.0), 0.5) + step(abs(fi - 21.0), 0.5), 1.0);
            tall = min(tall * (1.0 + (0.16 * hero)), 0.70);

            float lowL = (0.08 + (0.10 * r1)) + (0.09 * band) + (0.05 * Level);
            float lowR = (0.06 + (0.11 * r0)) + (0.10 * band) + (0.05 * Level);

            float built = step(0.12, r0);
            float cx = 0.5 + ((r0 - 0.5) * 0.30);
            float hw = 0.14 + (0.18 * r1);

            float h = tall * Slab(fx, cx - hw, cx + hw) * built;
            h = max(h, lowL * Slab(fx, 0.02, cx - hw + 0.02));
            h = max(h, lowR * Slab(fx, cx + hw - 0.02, 0.98));

            float kind = floor(r0 * 3.0);
            float tip = 0;
            if (kind < 0.5)
            {
                h = max(h, tall * 0.99 * Slab(fx, cx - (hw * 0.46), cx + (hw * 0.46)) * built);
            }
            else if (kind < 1.5)
            {
                tip = min(h + 0.045 + (0.07 * r2), 0.70);
                h = max(h, tip * Slab(fx, cx - 0.006, cx + 0.006) * built);
            }
            else
            {
                float bx = cx + ((r2 - 0.5) * hw);
                h = max(h, (h + 0.026) * Slab(fx, bx, bx + 0.07));
                tip = min(h + 0.032, 0.70);
                h = max(h, tip * Slab(fx, bx + 0.030, bx + 0.038));
            }

            h *= Slab(fx, 0.006 + (0.028 * r1), 0.994 - (0.028 * r1));
            return float4(h, kind, tip, cx);
        }

        // the sky, in linear light: a violet zenith over a warm, polluted horizon
        float3 SkyAt(float above)
        {
            float t = saturate(above / 0.92);
            float3 low = lerp(Warm.rgb * 0.30, Magic.rgb * 0.10, smoothstep(0.0, 0.28, t));
            return lerp(low, Night.rgb * 0.16, smoothstep(0.14, 0.95, t));
        }

        // the film curve: ACES, then the sRGB encode the strip expects, then the grade, the vignette, the grain
        float3 Aces(float3 x)
        {
            x = max(x, 0);
            return saturate((x * ((2.51 * x) + 0.03)) / ((x * ((2.43 * x) + 0.59)) + 0.14));
        }

        float3 Film(float3 light, float2 g)
        {
            float3 c = pow(Aces(light), 1.0 / 2.2);
            float l = dot(c, float3(0.2126, 0.7152, 0.0722));
            // teal shadows, warm highlights: the grade that holds a Night City frame together
            c = lerp(c * float3(0.78, 0.98, 1.14), c * float3(1.09, 1.01, 0.92), smoothstep(0.10, 0.62, l));
            float2 d = (g - 0.5) * float2(0.85, 2.0);
            c *= 1.0 - (0.38 * dot(d, d));
            // film grain, seeded by the music's clock: a held frame stays exactly still
            float grain = Hash((g * Size) + (Clock * 61.3)) - 0.5;
            c += grain * 0.026 * (1.0 - (0.5 * l));
            return saturate(c);
        }

        // a soft volumetric shaft falling from a source above the strip
        float Shaft(float2 g, float x0, float top, float spread)
        {
            float dy = top - g.y;
            if (dy < 0) return 0;
            float yy = saturate(dy / max(top, 0.02));
            float half = 0.0035 + (spread * yy);
            float across = 1.0 - saturate(abs(g.x - x0) / half);
            return across * across * (1.0 - yy) * (1.0 - yy);
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 uv = position.xy / Size;                        // y runs down from the top row
            float2 g = float2(uv.x, 1.0 - uv.y);                   // the design space: y up from the bottom
            float2 px = float2(position.x, Size.y - position.y);   // pixels, y up
            float fx = 1.0 / Size.x;
            float fy = 1.0 / Size.y;
            float aa = 0.9 * fy;                                    // half a pixel of edge softness

            // the camera drifts a little and the layers move against each other: parallax without a lens
            float2 camG = float2(0.0030 * sin(Clock * 0.31), 0.0016 * sin((Clock * 0.23) + 1.3)) * (0.7 + (0.5 * Mass));

            float hz = Horizon(g.x);
            float over = g.y - hz;
            float above = max(over, 0.0);
            float fi = floor(g.x * FAR);
            float band = Band(fi / 23.0);

            // the three banks keep to their own colour family: yellow west, magenta middle, cyan east
            float zone = floor(g.x * 3.0);
            float3 zoneCol = lerp(lerp(Core.rgb, Magic.rgb, step(0.5, zone)), Cyan.rgb, step(1.5, zone));

            // --- sky: a coloured gradient with a soft halo over the horizon, authored above 1.0
            float3 light = SkyAt(above);
            float halo = exp(-above * 11.0);
            light += Warm.rgb * ((0.62 * halo) + (0.55 * Wave * halo));
            light += Magic.rgb * (0.10 * halo * halo);

            // --- layer 3, the far skyline: small, desaturated, sunk in the air
            float farRoof = BackRoof(g.x + (camG.x * 0.4), DEEP, 0.15, 0.05, 0.10);
            float farCov = Soft(farRoof - (over + (camG.y * 0.4)), aa * 3.0);
            float3 farCol = lerp(SkyAt(0.20), float3(0.020, 0.017, 0.030), 0.72);
            light = lerp(light, farCol, farCov * 0.92);

            // --- layer 2, the middle skyline: closer, a few lights of its own, less air
            float midRoof = BackRoof(g.x + (camG.x * 0.9), MIDB, 0.37, 0.10, 0.16);
            float midCov = Soft(midRoof - (over + (camG.y * 0.9)), aa * 2.2);
            float3 midCol = lerp(SkyAt(0.12), float3(0.012, 0.011, 0.020), 0.66);
            float midLights = step(0.90, Hash(floor(px / float2(9.0, 11.0)) + 31.0)) * 0.30;
            midCol += zoneCol * midLights * saturate(midRoof - over);
            light = lerp(light, midCol, midCov * 0.95);

            // --- layer 1, the near city: the hero. Windows warm and cool, interior light up every face
            float4 here = NearRoof(g.x + camG.x);
            float roof = here.x;
            float nearCov = Soft(roof - (over + (camG.y * 1.4)), aa);

            // soft window glows, one per cell, warm and cool lighting different interiors
            float2 cell = float2(8.0, 10.0);
            float2 wcell = frac(px / cell) - 0.5;
            float glow = 1.0 - saturate(length(wcell * cell) / 2.8);
            glow *= glow;
            float2 id = floor(px / cell);
            float litChance = 0.24 + (0.34 * Snare) + (0.10 * Treble);
            float lit = step(Hash(id + (here.w * 17.0)), litChance) * glow * nearCov;
            float cool = step(0.78, Hash(id + 4.4));
            float3 winCol = lerp(float3(1.0, 0.78, 0.48) * 2.5, Cyan.rgb * 2.0, cool);
            winCol *= 0.30 + (1.10 * saturate((band * 1.5) + (0.45 * Wave)));

            // the interior light: a gradient up the face of every tower, from teal black to a warm wash
            float face = saturate(over / max(roof, 0.001));
            float3 city = lerp(Body.rgb * 0.55, Body.rgb * 1.7, face * face);
            city += zoneCol * 0.09 * face * (0.4 + (0.6 * band));
            city = lerp(city, winCol, lit);

            // the roof edge: a soft 2 px neon with a wider halo, no hard step anywhere
            float roofBand = 1.0 - saturate(abs(over - roof) / (2.2 * fy));
            float roofHalo = 1.0 - saturate(abs(over - roof) / (9.0 * fy));
            float3 roofCol = zoneCol * (1.6 + (1.3 * band));
            roofCol = lerp(roofCol, float3(1.0, 1.0, 1.0) * 1.6, 0.45 * Wave);
            city += roofCol * ((roofBand * 0.85) + (roofHalo * roofHalo * 0.22)) * nearCov;

            // mast, warning beacon, neon spine and the short roof sign
            float spineX = abs(g.x - ((fi + here.w) / FAR));
            float mast = Slab(spineX, 0.0, 0.0002) * step(0.001, here.z)
                       * Soft(here.z - over, aa) * Soft(over - roof, aa);
            city += roofCol * mast * 0.55;
            float beacon = step(abs(over - here.z), 0.9 * fy) * step(spineX, 1.6 * fx) * step(0.001, here.z);
            city += Alert.rgb * (1.5 + (2.6 * Wave)) * beacon * nearCov;

            float3 rh = DistrictHash(fi);
            float spine = step(spineX, 1.1 * fx) * step(0.70, rh.z) * nearCov;
            city += zoneCol * spine * (0.35 + (0.55 * Mid));
            float signOn = step(0.55, here.w) * (1.0 - saturate(abs(over - (roof - 0.040)) / (1.6 * fy)));
            signOn *= Slab(frac(g.x * FAR), here.w - 0.07, here.w + 0.07) * nearCov;
            city += zoneCol * signOn * (1.3 + (1.3 * Wave));

            // a soft contact shadow outside the silhouette, not a 1 px line
            float rim = Soft((over - roof), aa * 4.0) * 0.45;
            light = lerp(light, city, nearCov);
            light *= 1.0 - rim;

            // --- volumetric shafts off the neon, dipping into the haze
            float shafts = Shaft(g, 0.130, 0.99, 0.115) + Shaft(g, 0.232, 0.95, 0.080)
                         + Shaft(g, 0.905, 0.99, 0.120) + Shaft(g, 0.778, 0.96, 0.085);
            light += zoneCol * shafts * (0.10 + (0.12 * Flow));

            // --- the bay: the city mirrored with a vertical smear, fresnel, ripples and sparkle
            float depth = hz - g.y;
            if (depth > 0.0)
            {
                float amp = 2.0 + (14.0 * Mass) + (9.0 * Wave);
                float wob = (Noise(float2(px.x * 0.020, px.y * 0.085)) - 0.5) * amp * fx;
                float mirRoof = NearRoof(g.x + wob).x;
                float mirrorY = depth * MS;
                float inMir = Soft(mirRoof - mirrorY, 4.2 * fy);                 // a soft vertical smear
                float fres = 0.45 + (0.55 * saturate(1.0 - (depth * 2.0)));

                float3 water = SkyAt(depth * 1.7) * (0.55 * fres);
                water += Warm.rgb * (0.22 * fres * (1.0 - saturate(depth * 3.0)));
                water = lerp(water, float3(0.004, 0.008, 0.016), 0.55);
                water = lerp(water, float3(0.003, 0.005, 0.010) * (0.6 + inMir), inMir * 0.85);

                // the mirrored roofline, smeared into a soft vertical streak
                float mirLine = 1.0 - saturate(abs(mirRoof - mirrorY) / (5.5 * fy));
                water += zoneCol * (0.55 + (0.45 * band)) * mirLine * mirLine * 0.40 * fres;

                // ripple lines on the surface: broad, soft bands of light, not a comb of hard lines
                float rip = smoothstep(0.42, 0.95, Noise(float2((g.x * 16.0) + (Mass * 0.6), (depth * 22.0) + (Wave * 0.8))));
                water += SkyAt(0.05) * 0.30 * rip * (0.30 + (0.9 * Mass));
                float sparkle = step(0.993 - (0.40 * Spark), Hash(floor(px / 2.0) + 41.0)) * Spark;
                water += (Cyan.rgb + float3(0.3, 0.3, 0.3)) * sparkle * 0.25 * mirLine * (0.3 + (0.7 * fres));

                light = lerp(light, water, 0.94);
            }

            // --- resolve the film and hand it over premultiplied
            float3 col = Film(light, g);
            float a = (0.94 - (0.30 * saturate(above * 1.5))) * Fade;
            float edge = saturate(g.y / 0.045) * saturate((1.0 - g.y) / 0.045);
            a *= 0.30 + (0.70 * edge);
            return float4(col * a, a);
        }
        """;

    private const string ShowPs = """

        float4 main(float4 position : SV_Position) : SV_Target
        {
            // the picture is the field; here it only gets its lens: a little chromatic aberration towards the
            // edges, growing with the distance from the middle of the strip
            float2 uv = position.xy / Size;
            float2 bend = (uv - 0.5) * float2(0.0009, 0.0032);
            float r = Last.SampleLevel(Linear, uv + bend, 0).r;
            float4 m = Last.SampleLevel(Linear, uv, 0);
            float b = Last.SampleLevel(Linear, uv - bend, 0).b;
            return float4(r, m.g, b, m.a);
        }
        """;
}
