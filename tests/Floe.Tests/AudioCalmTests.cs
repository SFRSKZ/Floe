using Floe.Core.Interop;
using Xunit;

namespace Floe.Tests;

public class AudioCalmTests
{
    [Fact]
    public void A_loud_frame_answers_at_once_but_not_at_full()
    {
        var calm = new AudioCalm();
        var frame = new SpectrumFrame(24) { Level = 1, Bass = 1 };
        frame.Bands[3] = 1;

        calm.Apply(frame, 1 / 60f);

        Assert.InRange(frame.Level, .5f, .75f);
        Assert.InRange(frame.Bass, .5f, .75f);
        Assert.InRange(frame.Bands[3], .5f, .75f);
    }

    [Fact]
    public void A_held_sound_settles_below_full()
    {
        var calm = new AudioCalm();
        var frame = new SpectrumFrame(24);
        for (int i = 0; i < 120; i++)
        {
            frame.Level = 1;
            calm.Apply(frame, 1 / 60f);
        }

        Assert.Equal(.92f, frame.Level, 3);
    }

    [Fact]
    public void Onsets_keep_their_distance_and_weak_ones_are_dropped()
    {
        var calm = new AudioCalm();
        var frame = new SpectrumFrame(24) { Kick = .2f };
        calm.Apply(frame, 1 / 60f);
        Assert.Equal(0, frame.Kick);

        frame.Kick = 1;
        calm.Apply(frame, 1 / 60f);
        Assert.True(frame.Kick > 0);

        frame.Kick = 1;
        calm.Apply(frame, 1 / 60f);
        Assert.Equal(0, frame.Kick);
    }
}
