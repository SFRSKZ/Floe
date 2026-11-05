using System.IO;
using System.Buffers.Binary;

namespace SceneLab.Audio;

/// <summary>A decoded WAV file: mono float samples at the file's rate.</summary>
public sealed class WavData
{
    public required float[] Samples { get; init; }
    public required int SampleRate { get; init; }
    public float Length => Samples.Length / (float)SampleRate;

    /// <summary>Reads PCM 8/16/24/32-bit or IEEE 32/64-bit float WAV, any channel count, downmixed to mono.</summary>
    public static WavData Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("not a RIFF file");
        _ = reader.ReadUInt32();
        if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("not a WAVE file");

        int channels = 1, rate = 48000, bits = 16, format = 1;
        byte[]? data = null;
        while (stream.Position + 8 <= stream.Length)
        {
            string id = new(reader.ReadChars(4));
            uint size = reader.ReadUInt32();
            long next = stream.Position + size + (size & 1);
            if (id == "fmt ")
            {
                format = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                rate = reader.ReadInt32();
                _ = reader.ReadInt32();
                _ = reader.ReadUInt16();
                bits = reader.ReadUInt16();
            }
            else if (id == "data")
            {
                data = reader.ReadBytes((int)Math.Min(size, (uint)(stream.Length - stream.Position)));
            }

            if (next > stream.Length) break;
            stream.Position = next;
        }

        if (data is null || channels <= 0) throw new InvalidDataException("no data chunk");
        int bytesPerSample = bits / 8;
        int frames = data.Length / (bytesPerSample * channels);
        var samples = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            double sum = 0;
            for (int c = 0; c < channels; c++)
            {
                int at = ((f * channels) + c) * bytesPerSample;
                sum += bytesPerSample switch
                {
                    1 => (data[at] - 128) / 128.0,
                    2 => BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(at)) / 32768.0,
                    3 => ((data[at] | (data[at + 1] << 8) | (data[at + 2] << 16)) << 8 >> 8) / 8388608.0,
                    4 => format == 3
                        ? BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at))
                        : BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at)) / 2147483648.0,
                    8 => BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(at)),
                    _ => 0.0,
                };
            }

            samples[f] = (float)(sum / channels);
        }

        return new WavData { Samples = samples, SampleRate = rate };
    }
}
