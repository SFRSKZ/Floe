using System.Linq;
using System.Threading;
using Floe.Core.Interop;
using Xunit;

namespace Floe.Tests;

public class IdleDreamTests
{
    [Fact]
    public void A_silent_frame_gets_a_calm_moving_spectrum()
    {
        var dream = new IdleDream();
        var frame = new SpectrumFrame(24);
        dream.Fill(frame);
        var first = frame.Bands.ToArray();

        Assert.InRange(frame.Level, .1f, .5f);
        Assert.All(frame.Bands, b => Assert.InRange(b, 0f, 1f));
        Assert.Contains(frame.Bands, b => b > .05f);
        Assert.All(frame.Wave, w => Assert.InRange(w, -1f, 1f));

        Thread.Sleep(300);
        frame.Level = 0;
        dream.Fill(frame);
        Assert.NotEqual(first, frame.Bands);
    }

    [Fact]
    public void Real_sound_is_left_alone()
    {
        var dream = new IdleDream();
        var frame = new SpectrumFrame(24) { Level = .6f };
        frame.Bands[5] = .9f;
        dream.Fill(frame);

        Assert.Equal(.6f, frame.Level);
        Assert.Equal(.9f, frame.Bands[5]);
        Assert.Equal(0f, frame.Bands[0]);
    }
}
