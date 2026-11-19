using Floe.Core.Interop;
using Xunit;

namespace Floe.Tests;

public class AudioSpectrumTests
{
    // A 120 BPM kick (a 60 Hz thump that dies in a tenth of a second) over a steady 1 kHz pad, handed over in 10 ms
    // packets as the loopback does, and read as fast as the display draws.
    private static (int Kicks, float LowestPad, int Reads) Play(int rate, int fps, double seconds)
    {
        using var spectrum = new AudioSpectrum(false);
        var frame = new SpectrumFrame(24);
        int packet = rate / 100, fed = 0, kicks = 0, reads = 0;
        var samples = new float[packet];
        float lowestPad = 1;
        int pad = (int)(24 * Math.Log(1000 / 30.0) / Math.Log(16000 / 30.0));
        double dt = 1.0 / fps;
        for (double t = 0; t < seconds; t += dt)
        {
            while ((fed + packet) / (double)rate <= t)
            {
                for (int i = 0; i < packet; i++)
                {
                    double at = (fed + i) / (double)rate, beat = at % .5;
                    samples[i] = (float)((.05 * Math.Sin(2 * Math.PI * 1000 * at))
                        + (.6 * Math.Exp(-beat * 30) * Math.Sin(2 * Math.PI * 60 * beat)));
                }

                spectrum.Feed(samples, rate);
                fed += packet;
            }

            spectrum.Read(frame, dt);
            if (t < 3) continue; // the analyser is still learning what this music's usual is
            reads++;
            if (frame.Kick > 0) kicks++;
            lowestPad = Math.Min(lowestPad, frame.Bands[pad]);
        }

        return (kicks, lowestPad, reads);
    }

    [Theory]
    [InlineData(48000, 60)]
    [InlineData(48000, 240)]
    [InlineData(96000, 180)]
    [InlineData(192000, 144)]
    [InlineData(44100, 30)]
    public void Every_kick_is_heard_once_whatever_the_display_and_the_device(int rate, int fps)
    {
        var (kicks, _, _) = Play(rate, fps, 11);

        // sixteen kicks in the eight seconds that count
        Assert.InRange(kicks, 15, 17);
    }

    [Theory]
    [InlineData(48000, 240)]
    [InlineData(96000, 180)]
    public void A_read_between_two_packets_is_not_a_silence(int rate, int fps)
    {
        var (_, lowestPad, reads) = Play(rate, fps, 6);

        Assert.True(reads > 0);
        Assert.True(lowestPad > .3f, $"the pad fell to {lowestPad}");
    }

    [Fact]
    public void Sound_that_stops_reads_as_silence()
    {
        using var spectrum = new AudioSpectrum(false);
        var frame = new SpectrumFrame(24);
        var samples = new float[480];
        for (int p = 0; p < 100; p++)
        {
            for (int i = 0; i < samples.Length; i++) samples[i] = (float)(.3 * Math.Sin(2 * Math.PI * 440 * ((p * 480) + i) / 48000.0));
            spectrum.Feed(samples, 48000);
            spectrum.Read(frame, .01);
        }

        Assert.True(frame.Level > .3f);
        for (int i = 0; i < 30; i++) spectrum.Read(frame, .01);

        Assert.Equal(0, frame.Level);
        Assert.Equal(0, frame.Bass);
    }
}
