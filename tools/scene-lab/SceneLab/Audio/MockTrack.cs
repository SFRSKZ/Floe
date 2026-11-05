using Floe.App.Effects.Gpu;

namespace SceneLab.Audio;

/// <summary>
/// The dock's design track as C#: a 16 s loop at 120 BPM in four 4 s parts — intro, build, drop, breakdown —
/// then 0.8 s of true silence. It is the same shape the app's own <c>--render-gpu-look</c> harness uses, so
/// frames drawn here match what the dock shows, and a beat frame can be named exactly (0.5 s grid).
/// </summary>
public sealed class MockTrack : ITrack
{
    public const float Beat = 0.5f;
    public const float Bar = 4 * Beat;

    public string Name => "mock-16s-120bpm";
    public float Length => 16f;
    public float SilenceFrom => 15.2f;

    /// <summary>When false the strip is held at full opacity through the silence (see the resting skeleton).</summary>
    public bool HoldFadeThroughSilence { get; init; }

    /// <summary>Seconds into a bar where the drop's kick lands, used to pick the "beat" evidence frame.</summary>
    public const float FirstDropKick = 8f;

    public void Fill(GpuFrame frame, float tRaw)
    {
        const float beat = Beat;
        const float bar = Bar;
        float t = ((tRaw % 16f) + 16f) % 16f;
        frame.Time = tRaw;
        frame.Fade = HoldFadeThroughSilence ? 1f : 1f - SmoothStep(15.25f, 15.95f, t);

        bool intro = t < 4f, build = t >= 4f && t < 8f, drop = t >= 8f && t < 12f;
        float inBeat = (t % beat) / beat;
        int beatInBar = (int)((t % bar) / beat);
        float kick = drop
            ? MathF.Exp(-inBeat * 9f)
            : build && beatInBar % 2 == 0
                ? 0.85f * MathF.Exp(-inBeat * 9f)
                : intro && beatInBar == 0
                    ? 0.55f * MathF.Exp(-inBeat * 9f)
                    : t >= 12f && beatInBar % 2 == 0
                        ? MathF.Exp(-inBeat * 9f) * Math.Clamp(1f - ((t - 12f) / 4f), 0f, 1f)
                        : 0f;

        float posInBar = t % bar;
        float snareAge = posInBar >= beat && posInBar < 2 * beat ? posInBar - beat
            : posInBar >= 3 * beat ? posInBar - (3 * beat)
            : 10f;
        float snare = build || drop ? MathF.Exp(-snareAge / beat * 11f) : 0f;
        float eighth = (t % (beat / 2f)) / (beat / 2f);
        float hat = build || drop ? MathF.Exp(-eighth * 20f) : 0f;
        bool silent = t > 15.2f;

        float baseLevel = intro ? 0.30f
            : build ? 0.30f + (0.55f * SmoothStep(4f, 8f, t))
            : drop ? 0.92f
            : (0.92f * (1f - SmoothStep(12f, 15.2f, t))) + 0.03f;

        var audio = frame.Audio;
        audio.Level = silent ? 0f : Math.Clamp(baseLevel * (0.86f + (0.14f * kick)), 0f, 1f);
        audio.Bass = silent ? 0f : Math.Clamp(0.06f + (0.78f * kick) + (0.10f * (0.5f + (0.5f * MathF.Sin(t * 1.1f)))), 0f, 1f);
        audio.Mid = silent ? 0f : Math.Clamp(0.10f + (0.34f * (0.5f + (0.5f * MathF.Sin((t * 2.3f) + 1.2f)))) + (0.30f * audio.Level), 0f, 1f);
        audio.Treble = silent ? 0f : Math.Clamp(0.06f + (0.66f * hat) + (0.12f * (0.5f + (0.5f * MathF.Sin(t * 3.7f)))), 0f, 1f);
        audio.Balance = 0.40f * MathF.Sin(2f * MathF.PI * t / 16f);
        audio.Kick = silent ? 0f : kick;
        audio.Snare = silent ? 0f : snare;
        audio.Hat = silent ? 0f : hat;

        var bands = audio.Bands;
        for (int i = 0; i < bands.Length; i++)
        {
            float u = i / (float)Math.Max(1, bands.Length - 1);
            float low = MathF.Exp(-u * 6.5f);
            float mid = MathF.Exp(-MathF.Pow((u - 0.42f) / 0.27f, 2f));
            float high = MathF.Exp(-MathF.Pow((u - 0.86f) / 0.18f, 2f));
            float wobble = 0.5f + (0.5f * MathF.Sin((t * (0.8f + (i * 0.13f))) + (i * 1.7f)));
            float value = 0.02f
                + (0.58f * low * (0.30f + (0.78f * kick)))
                + (0.46f * mid * (0.22f + (0.62f * audio.Mid)) * (0.55f + (0.45f * wobble)))
                + (0.55f * high * (0.16f + (0.84f * hat)));
            value *= silent ? 0f : 0.92f + (0.08f * MathF.Sin((i * 3.1f) + (t * 0.9f)));
            bands[i] = Math.Clamp(value, 0f, 1f);
        }
    }

    public string Describe(float time)
    {
        var frame = new GpuFrame();
        Fill(frame, time);
        var a = frame.Audio;
        return $"level {a.Level:0.00} bass {a.Bass:0.00} mid {a.Mid:0.00} treble {a.Treble:0.00} kick {a.Kick:0.00} snare {a.Snare:0.00} hat {a.Hat:0.00}";
    }

    /// <summary>The first drop kick's frame time, where a look should show its hardest hit.</summary>
    public static float BeatFrameTime => FirstDropKick + (Beat * 0.10f);

    private static float SmoothStep(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - (2f * t));
    }
}
