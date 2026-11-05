using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// A heart monitor writing a signal. One trace runs out from each edge of the dock; every kick sends a heartbeat
/// along it with a hot pen at its peak. Where the pen has just passed, three layers of thin streaks stand up from the
/// trace and hang below it, each streak with its own tone and burn time, and burn down behind the beat.
/// <para>
/// The Data Rain language fills the wake behind the pen: hard single pixels settle where the heartbeat passed,
/// sink and fade with their own age tones, a kick throws whole columns of them upward, and thin column streaks
/// with bright heads fall through the strip. Nothing blurs - the points are read straight from the feedback
/// texture. Every clock that moves a pixel rides the music level, so silence leaves a held frame.
/// </para>
/// </summary>
public sealed class PulseGpuScene : FeedbackGpuScene
{
    private const int ConstantFloats = 84;
    private const int MaxPulses = 8;
    // the clip's own pinks: a hot core, the body of the line, a dark blood for the curtain's far ends
    private static readonly Color Core = Color.FromRgb(0xFF, 0x5A, 0x9A);
    private static readonly Color Body = Color.FromRgb(0xD8, 0x12, 0x5C);
    private static readonly Color Deep = Color.FromRgb(0x5E, 0x04, 0x22);
    // a side of the strip in a second; a beat crosses it in a little under one
    private const float Speed = .46f;

    private readonly float[] _x = new float[MaxPulses], _amp = new float[MaxPulses], _dir = new float[MaxPulses], _start = new float[MaxPulses];
    private readonly bool[] _live = new bool[MaxPulses];
    private int _next;
    private float _sinceBeat, _tickHold, _flash, _time, _rain;
    private int _tick;
    private float _dockLeft = .37f, _dockRight = .63f;

    public PulseGpuScene() : base(ConstantFloats, Shared + FeedbackPs, Shared + ShowPs)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        float dt = frame.Dt;
        var audio = frame.Audio;
        float music = Math.Clamp(audio.Level * 3f, 0, 1);
        _dockLeft = Math.Clamp(frame.DockLeft, .05f, .5f);
        _dockRight = Math.Clamp(frame.DockRight, .5f, .95f);
        _sinceBeat += dt;

        // a kick is a heartbeat; a song with no kick still gets a slow one while it plays
        bool beat = Beat.Hit || (_sinceBeat > 1.3f && audio.Level > .12f);
        if (beat)
        {
            float amp = Math.Clamp(.3f + (.7f * Math.Max(audio.Bass, Beat.Pulse)), 0, 1);
            Spawn(_dockLeft, -1, amp);
            Spawn(_dockRight, 1, amp);
            _sinceBeat = 0;
            _flash = 1;
        }

        for (int i = 0; i < MaxPulses; i++)
        {
            if (!_live[i]) continue;
            _x[i] += _dir[i] * Speed * dt;
            if (_x[i] < -.08f || _x[i] > 1.08f) _live[i] = false;
        }

        _flash *= MathF.Exp(-dt * 7);
        // every clock that moves a pixel rides the music level: silence holds the frame
        _time = (_time + (dt * music)) % 1000;
        _rain += dt * (2.4f * audio.Level);
        _tickHold -= dt * music;
        if (_tickHold <= 0)
        {
            _tick = (_tick + 1) % 4096;
            _tickHold = .06f;
        }
    }

    private void Spawn(float x, float dir, float amp)
    {
        _x[_next] = x;
        _start[_next] = x;
        _dir[_next] = dir;
        _amp[_next] = amp;
        _live[_next] = true;
        _next = (_next + 1) % MaxPulses;
    }

    protected override void Fill(GpuFrame frame)
    {
        var d = Data;
        var audio = frame.Audio;
        d[0] = frame.Width;
        d[1] = frame.Height;
        d[2] = frame.PixelsPerDip;
        d[3] = Math.Clamp(frame.Fade, 0, 1);
        d[4] = _dockLeft;
        d[5] = _dockRight;
        d[6] = audio.Level;
        d[7] = _flash;
        d[8] = _tick;
        d[9] = Speed;
        d[10] = audio.Treble;
        d[11] = _time;
        d[12] = _rain;
        d[13] = frame.Dt * 60f;
        PutBands(16, frame);
        var body = Lead(Body);
        Put(40, Theme is null ? Core : Mix(body, Colors.White, .45f));
        Put(44, body);
        Put(48, Theme is null ? Deep : Mix(Colors.Black, body, .4f));
        for (int i = 0; i < MaxPulses; i++)
        {
            int at = 52 + (i * 4);
            // a beat loses its strength as it runs out: whole at the dock, gone at the end of the strip
            float side = _dir[i] < 0 ? _start[i] : 1 - _start[i];
            float run = Math.Abs(_x[i] - _start[i]) / Math.Max(.01f, side);
            d[at] = _x[i];
            d[at + 1] = _live[i] ? _amp[i] * MathF.Pow(Math.Clamp(1 - run, 0, 1), .7f) : 0;
            d[at + 2] = _dir[i];
            d[at + 3] = _start[i];
        }
    }

    private const string Shared = """
        cbuffer Pulse : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float DockLeft;
            float DockRight;
            float Level;
            float Flash;
            float Tick;
            float Speed;
            float Treble;
            float Time;
            float Rain;
            float Step;
            float2 Spare2;
            float4 Bands[6];
            float4 CoreColor;
            float4 BodyColor;
            float4 DeepColor;
            float4 Pulses[8];
        };

        """ + ShaderHelpers + """

        // Data Rain draws its points one pixel at a time; no blur, no sampling
        int Grain() { return max(1, (int)floor(Dip)); }

        float G(float d, float w)
        {
            return exp(-(d / w) * (d / w));
        }

        // One heartbeat, in DIPs behind its peak (s < 0 is ahead): a small P, the sharp QRS, a slow T.
        float Heart(float s)
        {
            return (.08 * G(s + 20, 4.5)) - (.12 * G(s + 2.6, 1)) + G(s, .9) - (.5 * G(s - 2.6, 1.1)) + (.16 * G(s - 15, 5));
        }

        // The trace's height at x in pixels; peak says how near x is to a beat's peak, for the pen.
        float Trace(float x, out float peak)
        {
            float y = Size.y * .62;
            peak = 0;
            for (int i = 0; i < 8; i++)
            {
                float4 p = Pulses[i];
                if (p.y <= 0) continue;
                float s = -(x - (p.x * Size.x)) * p.z / Dip;
                if (abs(s) > 36) continue;
                y -= Heart(s) * p.y * Size.y * .56;
                peak = max(peak, G(s, 3) * p.y);
            }

            return y;
        }

        // 0..1: how freshly a beat's pen has passed x, so the streaks stand up behind it and burn down
        float Caught(float x)
        {
            float caught = 0;
            for (int i = 0; i < 8; i++)
            {
                float4 p = Pulses[i];
                if (p.y <= 0) continue;
                float passed = ((p.x * Size.x) - x) * p.z;
                float since = (x - (p.w * Size.x)) * p.z;
                if (passed < 0 || since < 0) continue;
                caught = max(caught, p.y * exp(-passed / (Size.x * Speed * .7)));
            }

            return caught;
        }
        """;

    // Data Rain's settled points (RainGpuScene.cs:163-168): a point lands behind the pen, is kept in Last,
    // sinks one pixel a frame and fades with its age. R is the age/energy, G the short-lived hot head.
    // Both clocks ride Level, so in silence the points freeze instead of raining on.
    private const string FeedbackPs = """

        float4 main(float4 position : SV_Position) : SV_Target
        {
            int size = Grain();
            float2 id = floor(position.xy / size);
            float2 pixel = (id + .5) * size;

            float caught = Caught(pixel.x);
            float peak;
            float y = Trace(pixel.x, peak);
            float reach = (8 + (40 * caught)) * Dip;
            float shape = pow(saturate(1 - (abs(pixel.y - y) / max(1, reach))), .6) * caught;
            // the wave itself draws the cloud: points gather where the trace has been pulled out of its flat line
            float dev = saturate(abs(y - (Size.y * .62)) / (Size.y * .32));
            shape *= .35 + (.65 * dev);

            float landing = Hash(id + float2(floor(Rain * 11), Tick * .37));
            float spawn = shape > .02 && landing < (.0025 + (.018 * Level)) * shape * Step ? 1 : 0;

            // the points stay where they landed, like Data Rain's settled points; only their age moves
            float2 prev = Last.Load(int3((int2)pixel, 0)).rg;
            float keep = pow(.978, Step * (.2 + (.8 * saturate(Level * 3))));
            float settled = max(prev.r * keep, spawn);
            float fresh = max(prev.g * pow(.86, Step), spawn);
            return float4(settled, fresh, 0, 1);
        }
        """;

    private const string ShowPs = """

        // 0 at the dock's edge, 1 at the end of the strip
        float Side(float u)
        {
            return u < DockLeft ? (DockLeft - u) / DockLeft : (u - DockRight) / (1 - DockRight);
        }

        float Distance(float2 pixel, out float peak)
        {
            float a, b;
            float y = Trace(pixel.x, peak);
            float slope = (Trace(pixel.x + 1, a) - Trace(pixel.x - 1, b)) * .5;
            return abs(pixel.y - y) / sqrt(1 + (slope * slope));
        }

        // One layer of streaks: separate lines with black between them, each with its own tone, height and burn
        // time, sliding slowly out from the dock. The far layers are thin, dense and dim, the near one sparse and hot.
        float3 Layer(float2 pixel, float caught, float side, float pitchDip, float density, float gain, float widthDip, float drift, float seed, float3 tint)
        {
            float pitch = max(1, pitchDip * Dip);
            float x = pixel.x + ((pixel.x < Size.x * .5 ? 1 : -1) * drift * Dip * Time);
            float column = floor(x / pitch);
            if (Hash(float2(column, seed)) > density) return 0;
            float centre = (column + .5 + ((Hash(float2(column, seed + 1)) - .5) * .6)) * pitch;
            float width = (.5 + (.5 * Hash(float2(column, seed + 2)))) * widthDip * Dip;
            float cover = saturate((width * .5) + .5 - abs(x - centre));
            if (cover <= 0) return 0;

            float life = pow(caught, .6 + (1.6 * Hash(float2(column, seed + 3))));
            float energy = saturate(Band(side * .95) * 1.2);
            float tall = saturate(sqrt(life) * 1.7) * (.45 + (.55 * Hash(float2(column, seed + 4)))) * (.6 + (.4 * energy));
            float base = Size.y * .62;
            float offset = base - pixel.y;
            float reach = offset >= 0 ? tall * base : tall * .85 * (Size.y - base);
            float t = abs(offset) / max(1, reach);
            if (t >= 1) return 0;

            float tone = .4 + (.6 * pow(Hash(float2(column, seed + 5)), 1.5));
            float grain = .7 + (.3 * Noise(float2((column * .37) + seed, pixel.y / (14 * Dip))));
            float k = tone * pow(1 - t, .7) * grain * saturate(life * 2.5) * (offset >= 0 ? 1 : .65);
            return tint * (k * gain * cover);
        }

        // One sample of the picture, premultiplied. Light adds up and is tone mapped, so where lines cross they run
        // towards white instead of painting over each other.
        float4 Shade(float2 pixel, float caught)
        {
            float u = pixel.x / Size.x;
            // the dock covers the middle: everything runs only from its edges out
            if (u > DockLeft && u < DockRight) return 0;

            float side = Side(u);
            float base = Size.y * .62;
            float3 light = 0;
            if (caught > .01)
            {
                light += Layer(pixel, caught, side, 1.5, .7, 1.3, .6, 5, 10, DeepColor.rgb * 1.6);
                light += Layer(pixel, caught, side, 3, .5, 2.2, .8, 13, 20, BodyColor.rgb);
                light += Layer(pixel, caught, side, 7, .35, 3.6, 1.3, 28, 30, CoreColor.rgb);

                // two hairlines that run along the trace behind the beat, like the horizontal light in the clip
                for (int h = -1; h <= 1; h += 2)
                {
                    float away = abs(pixel.y - (base + (h * 2.5 * Dip)));
                    light += BodyColor.rgb * (saturate(1 - away) * caught * .7);
                }
            }

            // the trace: a thin line, burning white at a beat's peak
            float peak;
            float d = Distance(pixel, peak);
            float core = .8 * Dip;
            float pen = saturate(core + .5 - d);
            float3 ink = lerp(BodyColor.rgb, CoreColor.rgb, saturate(peak * 1.6)) * (1.8 + (3 * peak));
            light = (light * (1 - pen)) + (ink * pen);

            float3 color = 1 - exp(-light * 1.4);
            float alpha = max(max(color.r, color.g), color.b);
            return float4(color, alpha);
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 pixel = position.xy;
            float u = pixel.x / Size.x;
            // the dock covers the middle: everything runs only from its edges out
            if (u > DockLeft && u < DockRight) return 0;

            float caught = Caught(pixel.x);
            // four samples in a rotated grid: smooth edges on the steep beat and the thin lines, with no blur
            float4 sum = Shade(pixel + float2(-.125, -.375), caught) + Shade(pixel + float2(.375, -.125), caught)
                + Shade(pixel + float2(.125, .375), caught) + Shade(pixel + float2(-.375, .125), caught);
            float4 base = sum * .25;

            // ---- Data Rain particles: hard single pixels read straight from the feedback texture ----
            int size = Grain();
            float3 dotColor = 0;
            float dotAlpha = 0;

            // a kick throws each column's settled points up by its own amount (RainGpuScene.cs:218)
            float column = floor(pixel.x / (6 * size));
            float2 thrown = pixel + float2(0, Flash * Hash(float2(column, 7)) * Size.y * .14);
            float2 id = floor(thrown / size);
            float2 at = (id + .5) * size;
            float2 state = Last.Load(int3((int2)at, 0)).rg;
            if (state.r > .06)
            {
                // age sets the tone: deep blood -> body -> hot core (RainGpuScene.cs:223-228)
                float3 tone = state.r > .72 ? CoreColor.rgb : (state.r > .36 ? BodyColor.rgb : DeepColor.rgb * 2.1);
                if (state.g > .45) tone = CoreColor.rgb;
                dotColor = tone;
                dotAlpha = 1;
            }

            // one falling streak per 6 px column with a bright head (RainGpuScene.cs:231-252)
            float streakSpeed = .5 + Hash(float2(column, 1));
            float head = (frac((Rain * streakSpeed) + Hash(float2(column, 3))) * (Size.y * 1.3)) - (Size.y * .1);
            float tail = Size.y * (.12 + (.22 * Hash(float2(column, 4))));
            float away = head - pixel.y;
            bool lit = Hash(float2(column, 2)) < .18 + (.62 * saturate(Level * 2.5));
            bool onColumn = fmod(pixel.x, 6 * size) < 1;
            if (lit && onColumn && away > 0 && away < tail)
            {
                float density = (1 - (away / tail)) * (.55 + (.45 * caught));
                if (Hash(float2(column, floor(pixel.y / 3))) < density)
                {
                    dotColor = lerp(BodyColor.rgb, CoreColor.rgb, .45);
                    dotAlpha = 1;
                }
            }
            if (lit && onColumn && away <= 0 && away > -2 * size)
            {
                dotColor = CoreColor.rgb;
                dotAlpha = 1;
            }

            float3 rgb = lerp(base.rgb, dotColor, dotAlpha);
            float alpha = max(base.a, dotAlpha);
            // the top melts away, so the strip has no cut line over an empty desktop
            float top = smoothstep(0, .18, pixel.y / Size.y);
            return float4(rgb, alpha) * Fade * top;
        }
        """;
}
