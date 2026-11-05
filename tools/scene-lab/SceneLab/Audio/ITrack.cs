using Floe.App.Effects.Gpu;

namespace SceneLab.Audio;

/// <summary>
/// One drive track for the harness: it answers "what does the music do at time t" and fills the frame's
/// audio and fade. The mock track is a deterministic 16 s song; a WAV track is real music run through the
/// same analyser the dock uses, so a scene's reaction can be judged on actual sound.
/// </summary>
public interface ITrack
{
    string Name { get; }

    /// <summary>The track's length in seconds.</summary>
    float Length { get; }

    /// <summary>Seconds of the track that the host would call silent (the end of the mock track).</summary>
    float SilenceFrom { get; }

    /// <summary>Fills <c>frame.Audio</c> and <c>frame.Fade</c> for time <paramref name="time"/>.</summary>
    void Fill(GpuFrame frame, float time);

    /// <summary>The audio's own summary at a time, for the report: level/bass/mid/treble/onsets.</summary>
    string Describe(float time);
}
