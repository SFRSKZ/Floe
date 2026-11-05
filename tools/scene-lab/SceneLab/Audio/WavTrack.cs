using Floe.App.Effects.Gpu;

namespace SceneLab.Audio;

/// <summary>
/// Real music through the dock's own analysis: the same 2048-point Hann FFT, the same 24 log bands
/// (30 Hz – 16 kHz), the same per-band dB normalisation against a slow average and a sinking peak, and the
/// same spectral-flux onsets (kick/snare/hat) as <c>Floe.Core.Interop.AudioSpectrum</c>. A scene driven by
/// this track is reacting to the actual song, not to a hand-drawn envelope.
/// </summary>
public sealed class WavTrack : ITrack
{
    private const int FftSize = 2048;
    private const float MinHz = 30, MaxHz = 16000;
    private const int FluxHistory = 43;
    private const int WaveSamples = 512;

    private readonly WavData _wav;
    private readonly float[] _window = new float[FftSize];
    private readonly float[] _re = new float[FftSize];
    private readonly float[] _im = new float[FftSize];
    private readonly float[] _magnitude = new float[FftSize / 2];
    private readonly float[] _previous = new float[FftSize / 2];
    private float[] _average = Array.Empty<float>();
    private float[] _peak = Array.Empty<float>();
    private readonly float[][] _flux = { new float[FluxHistory], new float[FluxHistory], new float[FluxHistory] };
    private readonly double[] _sinceOnset = { 1, 1, 1 };
    private int _fluxAt;
    private int _reads;
    private float _lastBass;
    private float _wavePeak;
    private float _lastTime = float.MinValue;

    public WavTrack(WavData wav)
    {
        _wav = wav;
        for (int i = 0; i < FftSize; i++) _window[i] = (float)(.5 - (.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1))));
    }

    public string Name => "wav";
    public float Length => _wav.Length;

    /// <summary>The analyser treats digital silence as nothing playing; report the tail's real silence.</summary>
    public float SilenceFrom => FindSilenceStart();

    /// <summary>The file's path, for the report.</summary>
    public string? Source { get; init; }

    public void Fill(GpuFrame frame, float time)
    {
        float dt = frame.Dt <= 0 ? 1f / 30f : frame.Dt;
        if (time < _lastTime - 1e-4f) Reset();
        _lastTime = time;

        int rate = _wav.SampleRate;
        var samples = _wav.Samples;
        int end = Math.Clamp((int)(time * rate), 0, samples.Length);
        int start = Math.Max(0, end - FftSize);

        bool silent = true;
        for (int i = start; i < end; i++)
        {
            if (Math.Abs(samples[i]) > 1e-5f)
            {
                silent = false;
                break;
            }
        }

        if (silent || end - start < FftSize)
        {
            Array.Clear(_magnitude);
        }
        else
        {
            for (int i = 0; i < FftSize; i++) _re[i] = samples[start + i] * _window[i];
            Array.Clear(_im);
            Fft(_re, _im);
            float total = 0;
            for (int k = 1; k < FftSize / 2; k++)
            {
                float m = MathF.Sqrt((_re[k] * _re[k]) + (_im[k] * _im[k]));
                _magnitude[k] = m;
                total += m;
            }

            _total = total;
        }

        if (silent) { _magnitude[0] = 0; _total = 0; }

        float hzPerBin = rate / (float)FftSize;
        var bands = frame.Audio.Bands;
        if (_average.Length != bands.Length + 4)
        {
            _average = new float[bands.Length + 4];
            _peak = new float[bands.Length + 4];
            Array.Fill(_average, float.NaN);
        }

        _reads++;
        for (int b = 0; b < bands.Length; b++)
        {
            float lo = MinHz * MathF.Pow(MaxHz / MinHz, b / (float)bands.Length);
            float hi = MinHz * MathF.Pow(MaxHz / MinHz, (b + 1) / (float)bands.Length);
            int k0 = Math.Clamp((int)(lo / hzPerBin), 1, (FftSize / 2) - 1);
            int k1 = Math.Clamp((int)MathF.Ceiling(hi / hzPerBin), k0 + 1, FftSize / 2);
            float sum = 0;
            for (int k = k0; k < k1; k++) sum = Math.Max(sum, _magnitude[k]);
            bands[b] = Normalise(b, 20 * MathF.Log10(sum + 1e-6f), dt, silent);
        }

        int n = bands.Length;
        frame.Audio.Bass = Normalise(n, Group(30, 150, hzPerBin, silent), dt, silent);
        frame.Audio.Mid = Normalise(n + 1, Group(150, 2000, hzPerBin, silent), dt, silent);
        frame.Audio.Treble = Normalise(n + 2, Group(2000, 16000, hzPerBin, silent), dt, silent);
        frame.Audio.Level = Normalise(n + 3, 20 * MathF.Log10((_total / (FftSize / 2)) + 1e-6f), dt, silent);

        frame.Audio.Kick = Onset(0, 30, 150, hzPerBin, dt, .14, silent);
        float jump = frame.Audio.Bass - _lastBass;
        _lastBass = frame.Audio.Bass;
        if (frame.Audio.Kick == 0 && jump > .3f && frame.Audio.Bass > .6f && _sinceOnset[0] >= .14)
        {
            _sinceOnset[0] = 0;
            frame.Audio.Kick = Math.Clamp(jump * 1.5f, .3f, 1);
        }

        frame.Audio.Snare = Onset(1, 150, 2500, hzPerBin, dt, .12, silent);
        frame.Audio.Hat = Onset(2, 6000, 16000, hzPerBin, dt, .07, silent);
        frame.Audio.Balance = 0;
        ReadWave(frame.Audio.Wave, end, dt, silent);
        _fluxAt = (_fluxAt + 1) % FluxHistory;
        Array.Copy(_magnitude, _previous, _magnitude.Length);

        frame.Time = time;
        frame.Fade = 1;
    }

    private float _total;

    private void ReadWave(float[] wave, int end, float dt, bool silent)
    {
        if (silent)
        {
            Array.Clear(wave);
            _wavePeak = 0;
            return;
        }

        int n = wave.Length;
        int from = Math.Max(0, end - (n * 2));
        for (int i = Math.Min(end, from + n) - 1; i > from; i--)
        {
            if (_wav.Samples[i] < 0 && _wav.Samples[i + 1] >= 0)
            {
                from = i + 1;
                break;
            }
        }

        float peak = 0;
        for (int i = 0; i < n; i++)
        {
            int at = from + i;
            if (at < _wav.Samples.Length) peak = Math.Max(peak, Math.Abs(_wav.Samples[at]));
        }

        _wavePeak = Math.Max(peak, _wavePeak * MathF.Exp(-dt * 1.2f));
        float gain = 1 / Math.Max(_wavePeak, .02f);
        for (int i = 0; i < n; i++)
        {
            int at = from + i;
            wave[i] = at < _wav.Samples.Length ? Math.Clamp(_wav.Samples[at] * gain, -1, 1) : 0;
        }
    }

    private float Group(float loHz, float hiHz, float hzPerBin, bool silent)
    {
        int k0 = Math.Max(1, (int)(loHz / hzPerBin)), k1 = Math.Min(FftSize / 2, (int)(hiHz / hzPerBin) + 1);
        float sum = 0;
        for (int k = k0; k < k1; k++) sum += _magnitude[k];
        return silent ? 0 : 20 * MathF.Log10((sum / Math.Max(1, k1 - k0)) + 1e-6f);
    }

    private float Normalise(int i, float db, float dt, bool silent)
    {
        if (silent || db < -90) return 0;
        if (float.IsNaN(_average[i]))
        {
            _average[i] = db;
            _peak[i] = db + 6;
        }

        _average[i] += (db - _average[i]) * (1 - MathF.Exp(-dt / 2.5f));
        _peak[i] = Math.Max(db, Math.Max(_average[i], _peak[i] - (8 * dt)));
        float floor = _average[i] - 6;
        return Math.Clamp((db - floor) / Math.Max(10, _peak[i] - floor), 0, 1);
    }

    private float Onset(int g, float loHz, float hiHz, float hzPerBin, float dt, double refractory, bool silent)
    {
        int k0 = Math.Max(1, (int)(loHz / hzPerBin)), k1 = Math.Min(FftSize / 2, (int)(hiHz / hzPerBin) + 1);
        float flux = 0, energy = 0;
        for (int k = k0; k < k1; k++)
        {
            float d = _magnitude[k] - _previous[k];
            if (d > 0) flux += d;
            energy += _magnitude[k];
        }

        flux /= energy + 1e-3f;
        var history = _flux[g];
        float mean = 0, spread = 0;
        for (int i = 0; i < FluxHistory; i++) mean += history[i];
        mean /= FluxHistory;
        for (int i = 0; i < FluxHistory; i++) spread += (history[i] - mean) * (history[i] - mean);
        spread = MathF.Sqrt(spread / FluxHistory);
        history[_fluxAt] = flux;
        _sinceOnset[g] += dt;
        float threshold = mean + (1.6f * spread) + .02f;
        if (silent || _reads < FluxHistory / 2 || flux <= threshold || _sinceOnset[g] < refractory) return 0;
        _sinceOnset[g] = 0;
        return Math.Clamp((flux - threshold) / (threshold + 1e-3f), .25f, 1);
    }

    private static void Fft(float[] re, float[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = -2 * Math.PI / len;
            float wr = (float)Math.Cos(angle), wi = (float)Math.Sin(angle);
            for (int i = 0; i < n; i += len)
            {
                float cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = a + (len / 2);
                    float tr = (re[b] * cr) - (im[b] * ci), ti = (re[b] * ci) + (im[b] * cr);
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                    float next = (cr * wr) - (ci * wi);
                    ci = (cr * wi) + (ci * wr);
                    cr = next;
                }
            }
        }
    }

    private void Reset()
    {
        Array.Clear(_magnitude);
        Array.Clear(_previous);
        foreach (var h in _flux) Array.Clear(h);
        Array.Fill(_average, float.NaN);
        _reads = 0;
        _wavePeak = 0;
        _lastBass = 0;
    }

    /// <summary>The time the track's last non-silent sample is at, for the "silence" evidence frame.</summary>
    private float FindSilenceStart()
    {
        var s = _wav.Samples;
        for (int i = s.Length - 1; i >= 0; i--)
        {
            if (Math.Abs(s[i]) > 1e-4f) return (i + 1) / (float)_wav.SampleRate;
        }

        return 0;
    }

    public string Describe(float time)
    {
        var frame = new GpuFrame { Dt = 1f / 30f };
        Fill(frame, time);
        var a = frame.Audio;
        return $"level {a.Level:0.00} bass {a.Bass:0.00} mid {a.Mid:0.00} treble {a.Treble:0.00} kick {a.Kick:0.00} snare {a.Snare:0.00} hat {a.Hat:0.00}";
    }
}
