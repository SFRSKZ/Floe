namespace Floe.App.Effects.Gpu;

/// <summary>
/// The motion the feedback looks share, as a MilkDrop preset has it: the last frame zooms, turns and wobbles a little
/// before the new one lands on it, the beat kicks all three, and every so often the motion eases into another one.
/// It is mirrored around the middle of the strip, as the spectrum is, and held down at the far ends of a wide strip.
/// </summary>
internal sealed class MilkMotion
{
    /// <summary>The floats <see cref="Hlsl"/> reads from b1.</summary>
    public const int Floats = 4;

    private readonly Random _random = new(5);
    private float _zoom, _spin, _warp, _wantZoom, _wantSpin, _wantWarp;
    private float _time, _left;
    private int _beats;

    public void Update(float dt, float level, float bass, float pulse, float swell, bool hit, float[] data)
    {
        _left -= dt;
        if (hit) _beats++;
        if (_left <= 0 || _beats >= 16) Pick();

        float ease = 1 - MathF.Exp(-dt * .8f);
        _zoom += (_wantZoom - _zoom) * ease;
        _spin += (_wantSpin - _spin) * ease;
        _warp += (_wantWarp - _warp) * ease;
        _time += dt * (.6f + (1.5f * level));

        float step = dt * 60;
        data[0] = (_zoom + (.009f * pulse) + (.004f * swell)) * step;
        data[1] = _spin * (1 + (2.5f * pulse)) * step;
        data[2] = _warp * (.6f + (1.8f * bass)) * step;
        data[3] = _time;
    }

    private void Pick()
    {
        float Between(float low, float high) => low + ((float)_random.NextDouble() * (high - low));
        _left = Between(7, 14);
        _beats = 0;
        // mostly outward; now and then the picture falls back into itself
        _wantZoom = _random.NextDouble() < .25 ? -Between(.001f, .002f) : Between(.0016f, .004f);
        _wantSpin = Between(.0008f, .003f) * (_random.Next(2) == 0 ? -1 : 1);
        _wantWarp = Between(.001f, .0035f);
    }

    /// <summary>MilkFlow gives how far, in pixels, the picture moves this frame at a pixel; MilkStep the same in whole cells, for a look that stays on its grid, with the fraction left to a dice.</summary>
    public const string Hlsl = """
        cbuffer Motion : register(b1)
        {
            float MilkZoom;
            float MilkSpin;
            float MilkWarp;
            float MilkTime;
        };

        float2 MilkFlow(float2 at)
        {
            float2 p = (at - (Size * .5)) / Size.y;
            float side = clamp(p.x * 6, -1, 1);
            p.x = abs(p.x);
            float2 move = ((p * MilkZoom) + (float2(-p.y, p.x) * MilkSpin)) / (1 + (.35 * p.x));
            move += float2(sin((p.y * 5.1) + MilkTime) + sin((p.x * 2.3) - (MilkTime * .7)),
                           cos((p.x * 3.7) + (MilkTime * .9)) + sin((p.y * 4.3) - (MilkTime * .6))) * MilkWarp * .5;
            move.x *= side;
            return move * Size.y;
        }

        int2 MilkStep(float2 at, int size, float dice)
        {
            return (int2)floor((MilkFlow(at) / size) + dice) * size;
        }

        """;
}
