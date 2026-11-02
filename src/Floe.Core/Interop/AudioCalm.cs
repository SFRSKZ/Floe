namespace Floe.Core.Interop;

/// <summary>
/// Takes the startle out of a <see cref="SpectrumFrame"/> before a visualizer sees it, without delaying anything: a
/// level answers in the same frame, but only part of its swing away from its recent average gets through, so one sound
/// moves the strip without lighting all of it. Weak onsets are dropped and the rest are thinned to a playable rate.
/// </summary>
public sealed class AudioCalm
{
    private const float Settle = 4;
    private const float Swing = .65f;
    private const float Ceiling = .92f;
    private const float Fall = 8;
    private const float WeakOnset = .3f;
    private const float OnsetStrength = .7f;
    private const float OnsetGap = .2f;

    private float[] _bands = Array.Empty<float>(), _slowBands = Array.Empty<float>();
    private readonly float[] _shown = new float[4], _slow = new float[4];
    private float _wait;

    public void Apply(SpectrumFrame frame, float dt)
    {
        if (_bands.Length != frame.Bands.Length)
        {
            _bands = new float[frame.Bands.Length];
            _slowBands = new float[frame.Bands.Length];
        }

        for (int i = 0; i < _bands.Length; i++) frame.Bands[i] = Follow(ref _bands[i], ref _slowBands[i], frame.Bands[i], dt);

        frame.Bass = Follow(ref _shown[0], ref _slow[0], frame.Bass, dt);
        frame.Mid = Follow(ref _shown[1], ref _slow[1], frame.Mid, dt);
        frame.Treble = Follow(ref _shown[2], ref _slow[2], frame.Treble, dt);
        frame.Level = Follow(ref _shown[3], ref _slow[3], frame.Level, dt);

        _wait = Math.Max(0, _wait - dt);
        bool open = _wait <= 0;
        float kick = Onset(frame.Kick, open), snare = Onset(frame.Snare, open && kick <= 0);
        frame.Kick = kick;
        frame.Snare = snare;
        frame.Hat = Onset(frame.Hat, open && kick <= 0 && snare <= 0);
        if (kick > 0 || snare > 0 || frame.Hat > 0) _wait = OnsetGap;
    }

    private static float Follow(ref float shown, ref float slow, float target, float dt)
    {
        slow += (target - slow) * (1 - MathF.Exp(-dt * Settle));
        float wanted = (slow + ((target - slow) * Swing)) * Ceiling;
        shown = Math.Max(wanted, shown - (Fall * dt));
        return shown;
    }

    private static float Onset(float strength, bool open) => open && strength >= WeakOnset ? strength * OnsetStrength : 0;
}
