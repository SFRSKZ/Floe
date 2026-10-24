using System.Runtime.InteropServices;

namespace Floe.Core.Interop;

/// <summary>What the music is doing right now, as <see cref="AudioSpectrum.Read"/> fills it in.</summary>
public sealed class SpectrumFrame
{
    public SpectrumFrame(int bands) => Bands = new float[bands];

    /// <summary>Log-spaced bands from ~30 Hz to ~16 kHz, each 0..1 against its own recent loudest.</summary>
    public float[] Bands { get; }

    /// <summary>Energy of the lows (to 150 Hz), mids (to 2 kHz) and highs, 0..1, each self-normalised.</summary>
    public float Bass { get; set; }
    public float Mid { get; set; }
    public float Treble { get; set; }

    /// <summary>Overall loudness 0..1 against the recent loudest; 0 in silence.</summary>
    public float Level { get; set; }

    /// <summary>Onsets since the last read: a kick in the lows, a snare in the mids, a hat in the highs; 0 or its strength 0..1.</summary>
    public float Kick { get; set; }
    public float Snare { get; set; }
    public float Hat { get; set; }

    /// <summary>
    /// The newest <see cref="WaveLength"/> samples as an oscilloscope shows them: mono, starting on a rising zero
    /// crossing so the trace stands still, scaled against its own recent loudest to -1..1; all 0 in silence.
    /// </summary>
    public float[] Wave { get; } = new float[WaveLength];

    public const int WaveLength = 512;
}

/// <summary>
/// Listens to what the default render endpoint plays (WASAPI loopback) on a background thread and turns the
/// latest samples into a spectrum, three band energies and onsets (spectral flux against its own recent
/// average). Silence, a missing device or any failure reads as an all-zero frame; the capture re-attaches
/// itself when the default device changes.
/// </summary>
public sealed class AudioSpectrum : IDisposable
{
    private const int FftSize = 2048;
    private const int Ring = 8192;
    private const float MinHz = 30, MaxHz = 16000;
    private const int FluxHistory = 43; // ~1.4 s of reads at 30 fps

    private readonly object _gate = new();
    private readonly float[] _ring = new float[Ring];
    private readonly Thread _thread;
    private volatile bool _disposed;
    private int _write;
    private int _sampleRate = 48000;
    private long _received;

    // analysis state, only touched by Read
    private readonly float[] _re = new float[FftSize];
    private readonly float[] _im = new float[FftSize];
    private readonly float[] _window = new float[FftSize];
    private readonly float[] _magnitude = new float[FftSize / 2];
    private readonly float[] _previous = new float[FftSize / 2];
    // per band, then bass, mid, treble and level: a slow average of the loudness in dB and a decaying peak
    private float[] _average = Array.Empty<float>();
    private float[] _peak = Array.Empty<float>();
    private int _reads;
    private float _lastBass;
    private readonly float[][] _flux = { new float[FluxHistory], new float[FluxHistory], new float[FluxHistory] };
    private readonly double[] _sinceOnset = { 1, 1, 1 };
    private int _fluxAt;
    private long _lastReceived;
    private readonly float[] _scope = new float[SpectrumFrame.WaveLength * 2];
    private float _wavePeak;

    public AudioSpectrum()
    {
        for (int i = 0; i < FftSize; i++) _window[i] = (float)(.5 - (.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1))));
        _thread = new Thread(CaptureLoop) { IsBackground = true, Name = "FloeSpectrum", Priority = ThreadPriority.AboveNormal };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public void Dispose() => _disposed = true;

    /// <summary>Analyses the newest samples into <paramref name="frame"/>; <paramref name="dt"/> is the time since the last read.</summary>
    public void Read(SpectrumFrame frame, double dt)
    {
        int rate;
        long received;
        lock (_gate)
        {
            int start = (_write - FftSize + Ring) % Ring;
            for (int i = 0; i < FftSize; i++) _re[i] = _ring[(start + i) % Ring] * _window[i];
            int scopeStart = (_write - _scope.Length + Ring) % Ring;
            for (int i = 0; i < _scope.Length; i++) _scope[i] = _ring[(scopeStart + i) % Ring];
            rate = _sampleRate;
            received = _received;
        }

        // nothing new arrived: loopback sends no packets while nothing plays
        bool silent = received == _lastReceived;
        _lastReceived = received;
        Array.Clear(_im);
        if (silent) Array.Clear(_re);
        Fft(_re, _im);

        int bins = FftSize / 2;
        float total = 0;
        for (int k = 1; k < bins; k++)
        {
            float m = MathF.Sqrt((_re[k] * _re[k]) + (_im[k] * _im[k]));
            _magnitude[k] = m;
            total += m;
        }

        float hzPerBin = rate / (float)FftSize;
        var bands = frame.Bands;
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
            int k0 = Math.Clamp((int)(lo / hzPerBin), 1, bins - 1), k1 = Math.Clamp((int)MathF.Ceiling(hi / hzPerBin), k0 + 1, bins);
            float sum = 0;
            for (int k = k0; k < k1; k++) sum = Math.Max(sum, _magnitude[k]);
            bands[b] = Normalise(b, 20 * MathF.Log10(sum + 1e-6f), dt, silent);
        }

        int n = bands.Length;
        frame.Bass = Normalise(n, Group(30, 150, hzPerBin), dt, silent);
        frame.Mid = Normalise(n + 1, Group(150, 2000, hzPerBin), dt, silent);
        frame.Treble = Normalise(n + 2, Group(2000, 16000, hzPerBin), dt, silent);
        frame.Level = Normalise(n + 3, 20 * MathF.Log10((total / bins) + 1e-6f), dt, silent);

        frame.Kick = Onset(0, 30, 150, hzPerBin, dt, .14, silent);
        // a kick also shows as the lows leaping up from where they just were
        float jump = frame.Bass - _lastBass;
        _lastBass = frame.Bass;
        if (frame.Kick == 0 && jump > .3f && frame.Bass > .6f && _sinceOnset[0] >= .14)
        {
            _sinceOnset[0] = 0;
            frame.Kick = Math.Clamp(jump * 1.5f, .3f, 1);
        }

        frame.Snare = Onset(1, 150, 2500, hzPerBin, dt, .12, silent);
        frame.Hat = Onset(2, 6000, 16000, hzPerBin, dt, .07, silent);
        ReadWave(frame.Wave, dt, silent);
        _fluxAt = (_fluxAt + 1) % FluxHistory;
        Array.Copy(_magnitude, _previous, bins);
    }

    // The newest half of the scope buffer, from the last rising zero crossing that still leaves a full trace.
    private void ReadWave(float[] wave, double dt, bool silent)
    {
        int n = wave.Length;
        if (silent)
        {
            Array.Clear(wave);
            _wavePeak = 0;
            return;
        }

        int from = n;
        for (int i = n; i > 0; i--)
        {
            if (_scope[i - 1] < 0 && _scope[i] >= 0)
            {
                from = i;
                break;
            }
        }

        float peak = 0;
        for (int i = 0; i < n; i++) peak = Math.Max(peak, Math.Abs(_scope[from + i]));
        // quick to rise, slow to fall, so quiet passages still show a trace without loud ones clipping long
        _wavePeak = Math.Max(peak, _wavePeak * (float)Math.Exp(-dt * 1.2));
        float gain = 1 / Math.Max(_wavePeak, .02f);
        for (int i = 0; i < n; i++) wave[i] = Math.Clamp(_scope[from + i] * gain, -1, 1);
    }

    private float Group(float loHz, float hiHz, float hzPerBin)
    {
        int k0 = Math.Max(1, (int)(loHz / hzPerBin)), k1 = Math.Min(FftSize / 2, (int)(hiHz / hzPerBin) + 1);
        float sum = 0;
        for (int k = k0; k < k1; k++) sum += _magnitude[k];
        return 20 * MathF.Log10((sum / Math.Max(1, k1 - k0)) + 1e-6f);
    }

    // Loudness the way it is heard, in dB, against what this band has done lately: its average sits low,
    // its recent peak at the top, so mastered-loud music still moves instead of pinning at full.
    private float Normalise(int i, float db, double dt, bool silent)
    {
        if (silent || db < -90) return 0;
        if (float.IsNaN(_average[i]))
        {
            // the first sound sets where this band sits
            _average[i] = db;
            _peak[i] = db + 6;
        }

        _average[i] += (db - _average[i]) * (float)(1 - Math.Exp(-dt / 2.5));
        // the peak sinks back towards the average at 8 dB a second
        _peak[i] = Math.Max(db, Math.Max(_average[i], _peak[i] - (float)(8 * dt)));
        float floor = _average[i] - 6;
        return Math.Clamp((db - floor) / Math.Max(10, _peak[i] - floor), 0, 1);
    }

    // Spectral flux: how much the band grew since the last read, against its own recent average and spread.
    private float Onset(int g, float loHz, float hiHz, float hzPerBin, double dt, double refractory, bool silent)
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

    // ---- capture ---------------------------------------------------------------------------------

    private const int ERender = 0;
    private const int EMultimedia = 1;
    private const uint ClsCtxAll = 23;
    private const int SharedMode = 0;
    private const uint LoopbackFlag = 0x00020000;
    private const uint SilentFlag = 0x2;
    private static readonly Guid AudioClientIid = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    private static readonly Guid CaptureClientIid = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
    private static readonly Guid FloatSubtype = new("00000003-0000-0010-8000-00aa00389b71");

    private void CaptureLoop()
    {
        while (!_disposed)
        {
            try
            {
                Capture();
            }
            catch
            {
                // a device that went away, or none at all: try again shortly
            }

            if (!_disposed) Thread.Sleep(1000);
        }
    }

    private void Capture()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDevice? device = null;
        IAudioClient? client = null;
        IAudioCaptureClient? capture = null;
        IntPtr format = IntPtr.Zero;
        try
        {
            if (enumerator.GetDefaultAudioEndpoint(ERender, EMultimedia, out device) < 0 || device is null) return;
            device.GetId(out string id);
            var iid = AudioClientIid;
            if (device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out object activated) < 0) return;
            client = (IAudioClient)activated;
            if (client.GetMixFormat(out format) < 0) return;

            int tag = Marshal.ReadInt16(format, 0) & 0xFFFF;
            int channels = Marshal.ReadInt16(format, 2);
            int rate = Marshal.ReadInt32(format, 4);
            int bits = Marshal.ReadInt16(format, 14);
            bool isFloat = tag == 3 || (tag == 0xFFFE && new Guid(ReadBytes(format, 24, 16)) == FloatSubtype);
            if (channels <= 0 || (isFloat && bits != 32) || (!isFloat && bits != 16)) return;

            // 100 ms of buffer; the loop drains it every 10 ms
            if (client.Initialize(SharedMode, LoopbackFlag, 1_000_000, 0, format, IntPtr.Zero) < 0) return;
            var ciid = CaptureClientIid;
            if (client.GetService(ref ciid, out object service) < 0) return;
            capture = (IAudioCaptureClient)service;
            lock (_gate) _sampleRate = rate;
            if (client.Start() < 0) return;

            int checks = 0;
            while (!_disposed)
            {
                Thread.Sleep(10);
                while (capture.GetNextPacketSize(out uint packet) >= 0 && packet > 0)
                {
                    if (capture.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _) < 0) return;
                    Push(data, (int)frames, channels, isFloat, (flags & SilentFlag) != 0);
                    capture.ReleaseBuffer(frames);
                }

                // the default device may have changed: re-attach to the new one
                if (++checks % 200 == 0
                    && (enumerator.GetDefaultAudioEndpoint(ERender, EMultimedia, out var current) < 0 || current is null || Changed(current, id)))
                {
                    return;
                }
            }

            client.Stop();
        }
        finally
        {
            if (format != IntPtr.Zero) Marshal.FreeCoTaskMem(format);
            Release(capture);
            Release(client);
            Release(device);
            Release(enumerator);
        }
    }

    private static bool Changed(IMMDevice current, string id)
    {
        current.GetId(out string now);
        Release(current);
        return now != id;
    }

    private unsafe void Push(IntPtr data, int frames, int channels, bool isFloat, bool silent)
    {
        lock (_gate)
        {
            for (int f = 0; f < frames; f++)
            {
                float mono = 0;
                if (!silent)
                {
                    for (int c = 0; c < channels; c++)
                    {
                        mono += isFloat ? ((float*)data)[(f * channels) + c] : ((short*)data)[(f * channels) + c] / 32768f;
                    }

                    mono /= channels;
                }

                _ring[_write] = mono;
                _write = (_write + 1) % Ring;
            }

            _received += frames;
        }
    }

    private static byte[] ReadBytes(IntPtr p, int offset, int count)
    {
        var bytes = new byte[count];
        Marshal.Copy(p + offset, bytes, 0, count);
        return bytes;
    }

    private static void Release(object? o)
    {
        if (o is not null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o);
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? endpoint);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);

        [PreserveSig]
        int OpenPropertyStore(uint access, out IntPtr properties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport]
    [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig]
        int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);

        [PreserveSig]
        int GetBufferSize(out uint frames);

        [PreserveSig]
        int GetStreamLatency(out long latency);

        [PreserveSig]
        int GetCurrentPadding(out uint padding);

        [PreserveSig]
        int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);

        [PreserveSig]
        int GetMixFormat(out IntPtr format);

        [PreserveSig]
        int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);

        [PreserveSig]
        int Start();

        [PreserveSig]
        int Stop();

        [PreserveSig]
        int Reset();

        [PreserveSig]
        int SetEventHandle(IntPtr handle);

        [PreserveSig]
        int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport]
    [Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig]
        int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);

        [PreserveSig]
        int ReleaseBuffer(uint frames);

        [PreserveSig]
        int GetNextPacketSize(out uint frames);
    }
}
