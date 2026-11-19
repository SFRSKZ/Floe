using System.Buffers;
using System.Text;
using System.Text.Json;
using Floe.Core.Interop;

namespace Floe.Plugins;

/// <summary>Everything the plugin needs about the strip and the dock, independent of the desktop's size.</summary>
public sealed class PluginState
{
    public int Width { get; init; }
    public int Height { get; init; }
    public double Dpr { get; init; } = 1;
    public double DockLeft { get; init; }
    public double DockRight { get; init; } = 1;
    public double DockBottom { get; init; }
    public double DockTop { get; init; } = 1;
    public string Core { get; init; } = "#70E2F5";
    public string Second { get; init; } = "#AF8CFF";
    public string Accent { get; init; } = "#FF7AB8";
    public string Deep { get; init; } = "#081826";
    public string OptionsJson { get; init; } = "{}";
    public int FpsCap { get; init; }
    public bool Running { get; init; }

    public bool HasCapsule => DockRight - DockLeft > 0.01 && DockTop - DockBottom > 0.01;
}

/// <summary>
/// Builds the two messages Floe posts into the plugin. The frame is written by hand: it goes out at
/// display rate, so it must not allocate a serializer graph every time.
/// </summary>
public static class PluginProtocol
{
    /// <summary>One analysis frame. <paramref name="seconds"/> is on the plugin's own clock domain.</summary>
    public static string BuildFrame(SpectrumFrame audio, double seconds, float balance)
    {
        var buffer = new ArrayBufferWriter<byte>(4096);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { SkipValidation = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "frame");
            writer.WriteNumber("t", Math.Round(seconds, 3));

            writer.WritePropertyName("bands");
            writer.WriteStartArray();
            for (int i = 0; i < audio.Bands.Length; i++)
            {
                writer.WriteNumberValue(Round(audio.Bands[i]));
            }

            writer.WriteEndArray();

            writer.WriteNumber("bass", Round(audio.Bass));
            writer.WriteNumber("mid", Round(audio.Mid));
            writer.WriteNumber("treble", Round(audio.Treble));
            writer.WriteNumber("level", Round(audio.Level));
            writer.WriteNumber("kick", Round(audio.Kick));
            writer.WriteNumber("snare", Round(audio.Snare));
            writer.WriteNumber("hat", Round(audio.Hat));
            writer.WriteNumber("balance", Math.Round(balance, 3));

            writer.WritePropertyName("wave");
            writer.WriteStartArray();
            for (int i = 0; i < audio.Wave.Length; i++)
            {
                writer.WriteNumberValue(Math.Round(audio.Wave[i], 3));
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static string BuildState(PluginState state)
    {
        var buffer = new ArrayBufferWriter<byte>(1024);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { SkipValidation = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "state");
            writer.WriteNumber("w", state.Width);
            writer.WriteNumber("h", state.Height);
            writer.WriteNumber("dpr", Math.Round(state.Dpr, 3));
            writer.WritePropertyName("dock");
            writer.WriteStartArray();
            writer.WriteNumberValue(Math.Round(state.DockLeft, 4));
            writer.WriteNumberValue(Math.Round(state.DockRight, 4));
            writer.WriteNumberValue(Math.Round(state.DockBottom, 4));
            writer.WriteNumberValue(Math.Round(state.DockTop, 4));
            writer.WriteEndArray();

            writer.WritePropertyName("palette");
            writer.WriteStartObject();
            writer.WriteString("core", state.Core);
            writer.WriteString("second", state.Second);
            writer.WriteString("accent", state.Accent);
            writer.WriteString("deep", state.Deep);
            writer.WriteEndObject();

            writer.WritePropertyName("options");
            writer.WriteRawValue(state.OptionsJson, skipInputValidation: true);
            writer.WriteNumber("fpsCap", state.FpsCap);
            writer.WriteBoolean("running", state.Running);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static float Round(float value) => MathF.Round(value, 3);
}
