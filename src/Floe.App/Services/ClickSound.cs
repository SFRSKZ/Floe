using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Floe.App.Services;

/// <summary>
/// The click: one drop of water landing on glass, synthesised here. A soft tap where it lands, the glass
/// answering with a short, rounded ring whose pitch lifts a touch like a drop's, and the sound spreading
/// out after it in a quiet, darkening tail.
/// One waveOut stream mixes the voices, so clicks overlap and answer within a buffer instead of
/// waiting in a queue behind the previous one.
/// </summary>
public static class ClickSound
{
    // Stereo PCM in ~11.6 ms buffers: three keep the device fed while the mixer thread sleeps.
    private const int Rate = 44100;
    private const int Frames = 512;
    private const int BufferCount = 3;
    private const int Channels = 2;
    private const double Length = .55;
    // Peak of a single voice is about .9; the master keeps a normal click well below clipping.
    private const float Master = .20f;
    private const int WHDR_INQUEUE = 0x10;
    private static readonly UIntPtr Mapper = new(0xFFFFFFFF);

    // Four slightly different pitches: consecutive clicks should not sound stamped out.
    private static readonly double[] Drift = { 1, 1.012, .991, 1.006 };
    private static readonly object Gate = new();
    private static readonly List<Voice> Voices = new();

    private static float[][] _dark = Array.Empty<float[]>();
    private static float[][] _light = Array.Empty<float[]>();
    private static float[] _mix = Array.Empty<float>();
    private static short[] _pcm = Array.Empty<short>();
    private static WaveHeader[] _headers = Array.Empty<WaveHeader>();
    private static GCHandle _pcmHandle;
    private static GCHandle _headerHandle;
    private static IntPtr _device;
    private static IntPtr _pcmPtr;
    private static IntPtr _headerPtr;
    private static int _state; // 0 not started, 1 running, 2 no audio
    private static int _turn;

    /// <summary>Prepares the voices and opens the stream, off the caller's thread if possible.</summary>
    public static void Warm()
    {
        if (_state != 0)
        {
            return;
        }

        lock (Gate)
        {
            if (_state != 0)
            {
                return;
            }

            try
            {
                RenderBanks();
                if (waveOutGetNumDevs() == 0 || !Open())
                {
                    _state = 2;
                    return;
                }

                var mixer = new Thread(Loop)
                {
                    IsBackground = true,
                    Name = "Floe click sound",
                    Priority = ThreadPriority.AboveNormal,
                };
                mixer.Start();
                _state = 1;
            }
            catch
            {
                _state = 2;
            }
        }
    }

    /// <summary>A click at <paramref name="pan"/> (-1 left .. 1 right), tuned by the click tone.</summary>
    public static void Play(ClickTone tone, double pan = 0)
    {
        if (!AuraSettings.ClickSound)
        {
            return;
        }

        Warm();
        if (_state != 1)
        {
            return;
        }

        var bank = tone == ClickTone.Light ? _light : _dark;
        if (bank.Length == 0)
        {
            return;
        }

        var variant = bank[(Interlocked.Increment(ref _turn) & int.MaxValue) % bank.Length];
        pan = Math.Clamp(pan, -1, 1);
        // constant power: a click keeps its loudness wherever it sits on the dock
        var angle = (pan + 1) * Math.PI / 4;
        var voice = new Voice(variant, (float)(Math.Cos(angle) * Master), (float)(Math.Sin(angle) * Master));

        lock (Gate)
        {
            Voices.Add(voice);
        }
    }

    /// <summary>Writes the click voices to a WAV file, for listening without the dock (diagnostics).</summary>
    public static bool Export(string path)
    {
        try
        {
            RenderBanks();
            var samples = new List<short>();
            Append(samples, _light[0], .0);
            Silence(samples, .12);
            Append(samples, _dark[0], .0);
            Silence(samples, .12);
            Append(samples, _light[1], -.5);
            Silence(samples, .10);
            Append(samples, _light[2], .5);
            Silence(samples, .12);
            for (var i = 0; i < 4; i++)
            {
                Append(samples, _dark[(i + 1) % _dark.Length], ((i % 2) * 2) - 1);
                Silence(samples, .055);
            }

            var folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream);
            var dataBytes = samples.Count * 2;
            writer.Write("RIFF"u8);
            writer.Write(36 + dataBytes);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1);              // PCM
            writer.Write((short)Channels);
            writer.Write(Rate);
            writer.Write(Rate * Channels * 2);
            writer.Write((short)(Channels * 2));
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(dataBytes);
            foreach (var sample in samples)
            {
                writer.Write(sample);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Builds the two banks: a drop on thicker glass and a lighter one on thinner glass.</summary>
    private static void RenderBanks()
    {
        if (_dark.Length != 0)
        {
            return;
        }

        _dark = Bank(860, .095);
        _light = Bank(1040, .08);
        _mix = new float[Frames * Channels];
    }

    private static float[][] Bank(double f0, double tau)
    {
        var bank = new float[Drift.Length][];
        for (var i = 0; i < Drift.Length; i++)
        {
            bank[i] = Render(f0 * Drift[i], tau, 0x5EED + (i * 7919));
        }

        return bank;
    }

    private static float[] Render(double f0, double tau, int seed)
    {
        var count = (int)(Rate * Length);
        var dry = new float[count];
        var random = new Random(seed);

        // The glass under the drop: a rounded main tone that lifts a little in pitch as the drop settles,
        // and one faint inharmonic overtone for the glassiness, gone much sooner.
        double phase = 0;
        for (var i = 0; i < count; i++)
        {
            var t = (double)i / Rate;
            var hz = f0 * (1 + (.18 * (1 - Math.Exp(-t / .025))));
            phase += Math.Tau * hz / Rate;
            var rise = Math.Sin(Math.PI * .5 * Math.Min(1, t / .0012));
            var v = Math.Sin(phase) * Math.Exp(-t / tau)
                + (.18 * Math.Sin((2.71 * phase) + .9) * Math.Exp(-t / (tau * .3)));
            dry[i] = (float)(v * rise);
        }

        Normalise(dry, .8f);

        // The tap where it lands: a breath of noise, a few milliseconds long.
        var tapCount = (int)(Rate * .005);
        var tap = new float[tapCount];
        for (var i = 0; i < tapCount; i++)
        {
            var t = (double)i / Rate;
            tap[i] = (float)(((random.NextDouble() * 2) - 1) * Math.Exp(-t / .0009));
        }

        Normalise(tap, .07f);
        for (var i = 0; i < tapCount; i++)
        {
            dry[i] += tap[i];
        }

        // The spreading: a small room of echoes (four damped combs into an all-pass), each pass darker,
        // so the drop fans out and fades instead of stopping dead.
        var data = new float[count];
        Array.Copy(dry, data, count);
        int[] delays = { 1116, 1277, 1422, 1557 };
        var wet = new float[count];
        foreach (var delay in delays)
        {
            var line = new float[delay];
            double damp = 0;
            var at = 0;
            for (var i = 0; i < count; i++)
            {
                var out1 = line[at];
                damp = (out1 * .55) + (damp * .45);
                line[at] = dry[i] + (float)(damp * .62);
                at = (at + 1) % delay;
                wet[i] += out1;
            }
        }

        var allpass = new float[556];
        var ap = 0;
        for (var i = 0; i < count; i++)
        {
            var buffered = allpass[ap];
            var input = wet[i] * .25f;
            allpass[ap] = input + (buffered * .5f);
            ap = (ap + 1) % allpass.Length;
            data[i] += (buffered - input) * .18f;
        }

        // Never let the tail end on a step.
        var fade = (int)(Rate * .15);
        for (var i = Math.Max(0, count - fade); i < count; i++)
        {
            data[i] *= (count - i) / (float)fade;
        }

        // A soft low-pass keeps the glass bright but never sharp.
        var smooth = Math.Exp(-Math.Tau * 3800 / Rate);
        double previous = 0;
        for (var i = 0; i < count; i++)
        {
            previous = ((1 - smooth) * data[i]) + (smooth * previous);
            data[i] = (float)previous;
        }

        Normalise(data, .8f);
        return data;
    }

    private static void Normalise(float[] data, float peak)
    {
        var max = 0f;
        foreach (var sample in data)
        {
            max = MathF.Max(max, MathF.Abs(sample));
        }

        if (max <= 0)
        {
            return;
        }

        var gain = peak / max;
        for (var i = 0; i < data.Length; i++)
        {
            data[i] *= gain;
        }
    }

    private static bool Open()
    {
        var format = new WaveFormat
        {
            FormatTag = 1, // PCM
            Channels = Channels,
            SamplesPerSecond = Rate,
            AverageBytesPerSecond = Rate * Channels * 2,
            BlockAlign = Channels * 2,
            BitsPerSample = 16,
        };

        if (waveOutOpen(out _device, Mapper, ref format, IntPtr.Zero, IntPtr.Zero, 0) != 0)
        {
            return false;
        }

        _pcm = new short[Frames * Channels * BufferCount];
        _pcmHandle = GCHandle.Alloc(_pcm, GCHandleType.Pinned);
        _pcmPtr = _pcmHandle.AddrOfPinnedObject();

        var size = Marshal.SizeOf<WaveHeader>();
        _headers = new WaveHeader[BufferCount];
        _headerHandle = GCHandle.Alloc(_headers, GCHandleType.Pinned);
        _headerPtr = _headerHandle.AddrOfPinnedObject();
        for (var i = 0; i < BufferCount; i++)
        {
            _headers[i].Data = _pcmPtr + (i * Frames * Channels * 2);
            _headers[i].Length = Frames * Channels * 2;
            if (waveOutPrepareHeader(_device, _headerPtr + (i * size), size) != 0)
            {
                waveOutClose(_device);
                return false;
            }
        }

        return true;
    }

    private static void Loop()
    {
        var size = Marshal.SizeOf<WaveHeader>();
        while (true)
        {
            var wrote = false;
            for (var i = 0; i < BufferCount; i++)
            {
                if ((ReadHeader(i, size).Flags & WHDR_INQUEUE) != 0)
                {
                    continue;
                }

                Mix(i);
                waveOutWrite(_device, _headerPtr + (i * size), size);
                wrote = true;
            }

            Thread.Sleep(wrote ? 1 : 3);
        }
    }

    private static WaveHeader ReadHeader(int index, int size) =>
        Marshal.PtrToStructure<WaveHeader>(_headerPtr + (index * size));

    private static void Mix(int buffer)
    {
        Array.Clear(_mix);
        lock (Gate)
        {
            for (var v = Voices.Count - 1; v >= 0; v--)
            {
                var voice = Voices[v];
                var take = Math.Min(Frames, voice.Data.Length - voice.Position);
                for (var f = 0; f < take; f++)
                {
                    var sample = voice.Data[voice.Position + f];
                    _mix[f * Channels] += sample * voice.Left;
                    _mix[(f * Channels) + 1] += sample * voice.Right;
                }

                voice.Position += take;
                if (voice.Position >= voice.Data.Length)
                {
                    Voices.RemoveAt(v);
                }
            }
        }

        var offset = buffer * Frames * Channels;
        for (var i = 0; i < Frames * Channels; i++)
        {
            _pcm[offset + i] = (short)(Math.Clamp(_mix[i], -1f, 1f) * 32767f);
        }
    }

    private static void Append(List<short> samples, float[] data, double pan)
    {
        var angle = (pan + 1) * Math.PI / 4;
        var left = (float)(Math.Cos(angle) * Master);
        var right = (float)(Math.Sin(angle) * Master);
        foreach (var sample in data)
        {
            samples.Add((short)(Math.Clamp(sample * left, -1f, 1f) * 32767f));
            samples.Add((short)(Math.Clamp(sample * right, -1f, 1f) * 32767f));
        }
    }

    private static void Silence(List<short> samples, double seconds)
    {
        for (var i = 0; i < (int)(Rate * seconds) * Channels; i++)
        {
            samples.Add(0);
        }
    }

    private sealed class Voice
    {
        public Voice(float[] data, float left, float right)
        {
            Data = data;
            Left = left;
            Right = right;
        }

        public float[] Data { get; }

        public float Left { get; }

        public float Right { get; }

        public int Position { get; set; }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WaveFormat
    {
        public short FormatTag;
        public short Channels;
        public int SamplesPerSecond;
        public int AverageBytesPerSecond;
        public short BlockAlign;
        public short BitsPerSample;
        public short ExtraSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr Data;
        public int Length;
        public int BytesRecorded;
        public IntPtr User;
        public int Flags;
        public int Loops;
        public IntPtr Next;
        public IntPtr Reserved;
    }

    [DllImport("winmm.dll")]
    private static extern uint waveOutGetNumDevs();

    [DllImport("winmm.dll")]
    private static extern int waveOutOpen(out IntPtr device, UIntPtr deviceId, ref WaveFormat format,
        IntPtr callback, IntPtr instance, int flags);

    [DllImport("winmm.dll")]
    private static extern int waveOutPrepareHeader(IntPtr device, IntPtr header, int size);

    [DllImport("winmm.dll")]
    private static extern int waveOutWrite(IntPtr device, IntPtr header, int size);

    [DllImport("winmm.dll")]
    private static extern int waveOutClose(IntPtr device);
}
