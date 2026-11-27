using System.Diagnostics;

namespace Floe.Core.Interop;

/// <summary>
/// A calm, made-up spectrum for the visualizers while no music plays, so each look keeps moving like a screensaver
/// instead of standing still. The bands drift on slow overlapping swells and a wave that walks across the spectrum,
/// the level breathes over ~9 s, and now and then a soft kick, snare or hat lands, so the looks that answer onsets
/// still have something to answer. Every reader sees the same dream (one clock), so the strip and its layers agree.
/// Real sound wins: a frame that already carries some keeps it.
/// </summary>
public sealed class IdleDream
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static volatile bool _active;

    /// <summary>Set while the strip runs without music and the idle animation is wanted.</summary>
    public static bool Active
    {
        get => _active;
        set => _active = value;
    }

    private const double Slot = .5; // onsets are decided per half second
    private double _last = -1;

    /// <summary>Overwrites a silent frame with the dream; a frame with real sound in it is left alone.</summary>
    public void Fill(SpectrumFrame frame)
    {
        double t = Clock.Elapsed.TotalSeconds;
        double last = _last < 0 || t - _last > 2 ? t : _last;
        _last = t;
        if (frame.Level > .05f) return;

        // the level breathes; a second, slower swell keeps two breaths from looking alike
        float breathe = (float)((.5 + (.5 * Math.Sin(t * 2 * Math.PI / 9))) * (.75 + (.25 * Math.Sin(t * 2 * Math.PI / 23 + 1.3))));
        float level = .16f + (.26f * breathe);

        var bands = frame.Bands;
        int n = bands.Length;
        float bass = 0, mid = 0, treble = 0;
        for (int i = 0; i < n; i++)
        {
            double u = n > 1 ? (double)i / (n - 1) : 0;
            // a wave walking up the spectrum, plus two slower, unrelated swells per band
            double walk = .5 + (.5 * Math.Sin((u * 5.2) - (t * .7)));
            double swell = (.5 + (.5 * Math.Sin((t * .31) + (i * 1.7)))) * (.5 + (.5 * Math.Sin((t * .53) + (i * .9) + 2.1)));
            double tilt = 1 - (.45 * u); // a little heavier in the lows, as music is
            float v = (float)Math.Clamp(((.25 + (.75 * walk)) * (.35 + (.65 * swell)) * tilt * (level * 1.9)), 0, 1);
            bands[i] = v;
            if (u < .25) bass += v;
            else if (u < .7) mid += v;
            else treble += v;
        }

        int lows = Math.Max(1, (int)Math.Ceiling(n * .25));
        int highs = Math.Max(1, n - (int)Math.Ceiling(n * .7));
        frame.Bass = Math.Clamp(bass / lows, 0, 1);
        frame.Mid = Math.Clamp(mid / Math.Max(1, n - lows - highs), 0, 1);
        frame.Treble = Math.Clamp(treble / highs, 0, 1);
        frame.Level = level;
        frame.Balance = (float)(.3 * Math.Sin(t * .13));

        // soft onsets, now and then, the same for every reader
        float kick = 0, snare = 0, hat = 0;
        for (long s = (long)Math.Floor(last / Slot) + 1; s <= (long)Math.Floor(t / Slot); s++)
        {
            float h = Hash(s), strength = .4f + (.3f * Hash(s * 7 + 3));
            if (h < .11f) kick = Math.Max(kick, strength);
            else if (h < .16f) snare = Math.Max(snare, strength);
            else if (h < .24f) hat = Math.Max(hat, strength * .8f);
        }

        frame.Kick = kick;
        frame.Snare = snare;
        frame.Hat = hat;

        // the scope: two slow sines beating against each other, as loud as the level
        var wave = frame.Wave;
        double f1 = 2 + Math.Sin(t * .2), f2 = 5.3 + (1.5 * Math.Sin(t * .37 + 1));
        for (int i = 0; i < wave.Length; i++)
        {
            double x = (double)i / wave.Length * 2 * Math.PI;
            wave[i] = (float)(level * 1.6 * ((.7 * Math.Sin(x * f1)) + (.3 * Math.Sin((x * f2) + (t * 1.1)))));
        }
    }

    private static float Hash(long s)
    {
        ulong x = (ulong)s * 0x9E3779B97F4A7C15UL;
        x ^= x >> 31;
        x *= 0xBF58476D1CE4E5B9UL;
        x ^= x >> 27;
        return (x >> 40) / (float)(1UL << 24);
    }
}
