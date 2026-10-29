using Floe.App.Services;

namespace Floe.App.Effects.Gpu;

/// <summary>What the dust's grains make, beyond the wave: each shape keeps the trails, the glow and the aurora.</summary>
public enum DustShape
{
    Wave,
    // sand on a vibrating plate, gathering on the nodal lines of the note that sounds
    Chladni,
    // fireflies that drift apart and slowly fall into step with the beat
    Fireflies,
    // a magnetic liquid at the bottom, raising a spike field from the spectrum
    Ferro,
}

public sealed partial class StardustGpuScene
{
    /// <summary>What the grains draw; anything but <see cref="DustShape.Wave"/> runs without the free dust.</summary>
    public DustShape Shape { get; init; }

    private bool Shaped => Shape != DustShape.Wave;

    // per grain, for the shapes: two fixed random numbers, and for the fireflies an anchor, a phase and its pace
    private float[] _gu = Array.Empty<float>(), _gv = Array.Empty<float>();
    private float[] _theta = Array.Empty<float>(), _omega = Array.Empty<float>(), _flash = Array.Empty<float>();

    private int ShapeCount(PerformanceLevel performance)
    {
        float lite = performance == PerformanceLevel.Lite ? .5f : 1;
        return Shape switch
        {
            DustShape.Chladni => Math.Clamp((int)(_width * 2.3f * lite), 400, 4600),
            DustShape.Fireflies => (int)(130 * lite),
            _ => Math.Clamp((int)(_width * 1.9f * lite), 400, 4200),
        };
    }

    private void ShapeInit(int n)
    {
        _gu = new float[n];
        _gv = new float[n];
        _theta = new float[n];
        _omega = new float[n];
        _flash = new float[n];
        for (int i = 0; i < n; i++)
        {
            _gu[i] = (float)_random.NextDouble();
            _gv[i] = (float)_random.NextDouble();
            _theta[i] = (float)(_random.NextDouble() * Math.PI * 2);
            _omega[i] = (float)(1 + (Gaussian() * .07));
            _wx[i] = _gu[i] * _width;
            _wy[i] = Shape == DustShape.Ferro ? _height - 2 : 3 + (_gv[i] * (_height - 6));
        }

        if (Shape == DustShape.Ferro) PlaceSpikes();
    }

    private void MoveShape(float dt)
    {
        switch (Shape)
        {
            case DustShape.Chladni: MoveChladni(dt); break;
            case DustShape.Fireflies: MoveFireflies(dt); break;
            case DustShape.Ferro: MoveFerro(dt); break;
        }
    }

    private void ShapeKick(float kick)
    {
        switch (Shape)
        {
            case DustShape.Chladni:
                // the plate jumps: the sand hops off its lines and finds them again
                for (int i = 0; i < _waveCount; i++)
                {
                    _wvx[i] += (float)(Gaussian() * 60 * kick);
                    _wvy[i] += (float)(Gaussian() * 45 * kick);
                }
                break;
            case DustShape.Fireflies:
                Beat(kick);
                break;
            case DustShape.Ferro:
                for (int k = 0; k < _spikes; k++) _spikeV[k] += kick * _height * (2.5f + (4 * (float)_random.NextDouble())) * _spikeGain[k];
                break;
        }
    }

    // trails and how much aurora shows behind each shape
    private float ShapeDecayTau => Shape switch { DustShape.Chladni => .22f, DustShape.Fireflies => .08f, _ => .12f };

    private float ShapeAura => Shape switch { DustShape.Chladni => .45f, DustShape.Fireflies => .22f, DustShape.Ferro => .5f, _ => 1 };

    // ---- Chladni sand -----------------------------------------------------------------------------

    // plate modes (n, m), from the deepest note to the highest: more lines the higher it sounds
    private static readonly (int N, int M)[] Modes =
    {
        (1, 2), (1, 3), (2, 3), (1, 4), (2, 5), (3, 4), (1, 5), (3, 5), (2, 7), (4, 5), (3, 7), (5, 6),
    };

    private int _mode = 2, _modeCandidate = 2;
    private double _modeHeld, _candidateHeld;

    private void MoveChladni(float dt)
    {
        PickMode(dt);
        var (n, m) = Modes[_mode];
        float cell = _height * 1.6f, height = _height;
        float pn = MathF.PI * n, pm = MathF.PI * m;
        // overdamped: the sand slides down the slope of the vibration's square into the still lines
        float mobility = 7f / ((pn * pn) + (pm * pm)) * 2;
        float vib = .35f + (1.8f * (float)_level) + (2.2f * (float)_pulse);
        float follow = 1 - MathF.Exp(-dt * 10);
        float jitter = 22 * vib * MathF.Sqrt(dt) / dt;
        for (int i = 0; i < _waveCount; i++)
        {
            float px = _wx[i] / cell, py = _wy[i] / height;
            float cnx = MathF.Cos(pn * px), snx = MathF.Sin(pn * px), cmx = MathF.Cos(pm * px), smx = MathF.Sin(pm * px);
            float cny = MathF.Cos(pn * py), sny = MathF.Sin(pn * py), cmy = MathF.Cos(pm * py), smy = MathF.Sin(pm * py);
            float f = (cnx * cmy) - (cmx * cny);
            float gx = (-pn * snx * cmy) + (pm * smx * cny);
            float gy = (-pm * cnx * smy) + (pn * cmx * sny);
            float tx = -mobility * f * gx * cell, ty = -mobility * f * gy * height;
            _wvx[i] += (tx - _wvx[i]) * follow;
            _wvy[i] += (ty - _wvy[i]) * follow;
            // grains on a loud part of the plate bounce, the ones on a line stay put
            float shake = jitter * (.08f + MathF.Abs(f));
            _wx[i] += (_wvx[i] + ((float)Gaussian() * shake * .6f)) * dt;
            _wy[i] += (_wvy[i] + ((float)Gaussian() * shake * .35f)) * dt;
            if (_wx[i] < 0) _wx[i] += _width;
            else if (_wx[i] > _width) _wx[i] -= _width;
            _wy[i] = Math.Clamp(_wy[i], 2, height - 2);
        }
    }

    // the loudest band picks the mode, once it has held a moment, and the plate keeps a mode a while
    private void PickMode(float dt)
    {
        int best = 0;
        for (int b = 1; b < Bands; b++)
            if (_band[b] * (1 + (b * .04f)) > _band[best] * (1 + (best * .04f))) best = b;
        int candidate = _level < .03 ? 2 : Math.Min(Modes.Length - 1, best * Modes.Length / Bands);
        if (candidate != _modeCandidate)
        {
            _modeCandidate = candidate;
            _candidateHeld = 0;
        }

        _candidateHeld += dt;
        _modeHeld += dt;
        if (_modeCandidate != _mode && _candidateHeld > .45 && _modeHeld > 1.6)
        {
            _mode = _modeCandidate;
            _modeHeld = 0;
            ShapeKick(.5f);
        }
    }

    // ---- fireflies --------------------------------------------------------------------------------

    private double _beatPeriod = 1, _lastBeat = -10, _irregular = 1, _coupling = .1;

    private void Beat(float kick)
    {
        double interval = _time - _lastBeat;
        _lastBeat = _time;
        if (interval > .28 && interval < 2.2)
        {
            // a steady beat lowers the irregularity, a stumbling one raises it
            double miss = Math.Abs(interval - _beatPeriod) / _beatPeriod;
            _irregular += (Math.Min(1, miss * 3) - _irregular) * .25;
            _beatPeriod += (interval - _beatPeriod) * .3;
        }

        // the beat nudges every phase towards a flash, harder the more they already agree
        float pull = (float)(.08 + (.3 * _coupling / 1.6)) * kick;
        for (int i = 0; i < _waveCount; i++) _theta[i] -= pull * MathF.Sin(_theta[i]);
    }

    private void MoveFireflies(float dt)
    {
        int n = _waveCount;
        // the swarm couples while the beat is steady and the music plays; silence lets it drift apart
        bool steady = _time - _lastBeat < 2.5;
        double target = steady ? 1.6 * (1 - _irregular) * Math.Min(1, _level * 4) : .05;
        _coupling += (target - _coupling) * (1 - Math.Exp(-dt / 6));
        float k = (float)_coupling;
        float baseOmega = (float)(2 * Math.PI / Math.Clamp(_beatPeriod, .35, 2));
        float t = (float)_time;
        const float reach = 1f / (2 * 110 * 110);

        for (int i = 0; i < n; i++)
        {
            // neighbours pull each other's phases: sync spreads as waves through the swarm
            float sum = 0, weight = 0, xi = _wx[i], yi = _wy[i], ti = _theta[i];
            for (int j = 0; j < n; j++)
            {
                float dx = _wx[j] - xi;
                if (dx > 330 || dx < -330) continue;
                float dy = _wy[j] - yi;
                float w = MathF.Exp(-((dx * dx) + (dy * dy)) * reach);
                sum += w * MathF.Sin(_theta[j] - ti);
                weight += w;
            }

            float next = ti + ((baseOmega * _omega[i]) + (k * sum / MathF.Max(weight, 1e-3f))) * dt;
            if (next >= MathF.PI * 2)
            {
                next -= MathF.PI * 2;
                _flash[i] = 1;
            }

            _theta[i] = next;
        }

        float decay = MathF.Exp(-dt / .22f);
        for (int i = 0; i < n; i++)
        {
            _flash[i] *= decay;
            // a lazy wander round a drifting anchor, and a little hop as it lights
            float ax = _gu[i] * _width + (MathF.Sin((t * .05f) + (_gv[i] * 40)) * 60);
            float ay = (.2f + (.65f * _gv[i])) * _height + (MathF.Sin((t * .19f) + (_gu[i] * 30)) * _height * .18f);
            _wvx[i] += (((ax - _wx[i]) * .2f) + (MathF.Sin((t * .6f) + (i * 1.7f)) * 6) - (_wvx[i] * 2.5f)) * dt;
            _wvy[i] += (((ay - _wy[i]) * .3f) + (MathF.Cos((t * .8f) + (i * 2.3f)) * 4) - (_flash[i] * 14) - (_wvy[i] * 2.5f)) * dt;
            _wx[i] += _wvx[i] * dt;
            _wy[i] = Math.Clamp(_wy[i] + (_wvy[i] * dt), 4, _height - 4);
        }
    }

    // ---- ferrofluid -------------------------------------------------------------------------------

    private int _spikes;
    private float[] _spikeX = Array.Empty<float>(), _spikeR = Array.Empty<float>(), _spikeGain = Array.Empty<float>();
    private float[] _spikeH = Array.Empty<float>(), _spikeV = Array.Empty<float>();
    private float[] _surface = Array.Empty<float>();
    private double _sheen;

    private void PlaceSpikes()
    {
        // a loose lattice: a spike every ~24 DIP, nudged, of varied girth and strength
        var xs = new List<float>();
        for (float x = 10; x < _width - 6; x += 18 + (float)(_random.NextDouble() * 12)) xs.Add(x);
        _spikes = xs.Count;
        _spikeX = xs.ToArray();
        _spikeR = new float[_spikes];
        _spikeGain = new float[_spikes];
        _spikeH = new float[_spikes];
        _spikeV = new float[_spikes];
        for (int k = 0; k < _spikes; k++)
        {
            _spikeR[k] = 8 + (float)(_random.NextDouble() * 7);
            _spikeGain[k] = .55f + (float)(_random.NextDouble() * .6);
        }

        _surface = new float[(int)_width + 2];
    }

    private void MoveFerro(float dt)
    {
        if (_surface.Length != (int)_width + 2) PlaceSpikes();
        float height = _height, level = (float)_level;
        _sheen += dt * (.12 + (.5 * level));
        for (int k = 0; k < _spikes; k++)
        {
            // each spike listens to the band under it, laid out from the middle outwards like the aurora
            float xn = MathF.Abs((_spikeX[k] / _width) - .5f) * 2;
            float at = xn * (Bands - 1);
            int b0 = Math.Min(Bands - 2, (int)at);
            float band = _band[b0] + ((_band[b0 + 1] - _band[b0]) * (at - b0));
            float target = (.06f + (.8f * MathF.Pow(band, 1.25f) * (.55f + (.45f * level)) * _spikeGain[k])) * height * .82f;
            // underdamped: a spike overshoots and wobbles like a liquid
            _spikeV[k] += ((240 * (target - _spikeH[k])) - (11 * _spikeV[k])) * dt;
            _spikeH[k] = Math.Clamp(_spikeH[k] + (_spikeV[k] * dt), 0, height - 3);

            // a spike shot up hard throws a spark from its tip
            if (_spikeV[k] > height * 5 && _count < 500 && _random.NextDouble() < .35)
            {
                float xs = _spikeX[k] / _width;
                Spawn(_spikeX[k], height - 6 - _spikeH[k], Gaussian() * 18, -(30 + (40 * _random.NextDouble())),
                    .8 + (.6 * _random.NextDouble()), .9, .15, Hue(MathF.Abs(xs - .5f) * 2));
            }
        }

        // the surface: a shallow pool with sharp cones on it
        float floor = 5 + (2 * level);
        for (int x = 0; x < _surface.Length; x++) _surface[x] = floor;
        for (int k = 0; k < _spikes; k++)
        {
            float r = _spikeR[k] * (1 + (_spikeH[k] / height * .6f));
            int from = Math.Max(0, (int)(_spikeX[k] - r)), to = Math.Min(_surface.Length - 1, (int)(_spikeX[k] + r) + 1);
            for (int x = from; x <= to; x++)
            {
                float d = 1 - (MathF.Abs(x - _spikeX[k]) / r);
                if (d <= 0) continue;
                float h = floor + (_spikeH[k] * MathF.Pow(d, 1.8f));
                if (h > _surface[x]) _surface[x] = h;
            }
        }

        for (int i = 0; i < _waveCount; i++)
        {
            float x = _gu[i] * _width;
            int c = Math.Clamp((int)x, 0, _surface.Length - 2);
            float s = _surface[c] + ((_surface[c + 1] - _surface[c]) * (x - c));
            // most grains sit at the skin, so the surface reads as one bright line over a dark body
            float depth = 1 - MathF.Pow(_gv[i], 2.8f);
            float hy = height - 1.5f - (depth * s);
            _wvx[i] += ((150 * (x - _wx[i])) - (12 * _wvx[i])) * dt;
            _wvy[i] += ((150 * (hy - _wy[i])) - (12 * _wvy[i])) * dt;
            _wx[i] += _wvx[i] * dt;
            _wy[i] += _wvy[i] * dt;
        }
    }

    // ---- drawing ----------------------------------------------------------------------------------

    private int ShapeInstances(int n, float fade)
    {
        float ppd = _ppd, t = (float)_time, level = (float)_level;
        float trail = Math.Min(1, fade * 1.5f);
        switch (Shape)
        {
            case DustShape.Chladni:
            {
                float energy = (.5f + (.5f * level)) * trail;
                for (int i = 0; i < _waveCount; i++)
                {
                    float speed = MathF.Min(1, (MathF.Abs(_wvx[i]) + MathF.Abs(_wvy[i])) / 220);
                    float twinkle = .8f + (.2f * MathF.Sin((_gu[i] * 90) + (t * (2 + (_gv[i] * 3)))));
                    float b = .75f * energy * twinkle * (1 + (1.2f * speed));
                    float xn = MathF.Abs((_wx[i] / _width) - .5f) * 2;
                    int hue = Math.Clamp((int)(xn * (PaletteSize - 1)), 0, PaletteSize - 1);
                    _instanceCount = n;
                    n = Add(_wx[i] * ppd, _wy[i] * ppd, 0, _palR[hue] * b * _norm[0], _palG[hue] * b * _norm[0], _palB[hue] * b * _norm[0]);
                }
                break;
            }

            case DustShape.Fireflies:
            {
                for (int i = 0; i < _waveCount; i++)
                {
                    float flash = _flash[i];
                    // an ember between flashes, a soft bloom when it lights
                    float glow = (.12f + (2.4f * flash)) * trail;
                    int hue = (int)(_gu[i] * (PaletteSize - 1));
                    float warm = flash * .55f;
                    float r = (_palR[hue] * (1 - warm)) + warm, g = (_palG[hue] * (1 - warm)) + (warm * .9f), bl = (_palB[hue] * (1 - warm)) + (warm * .62f);
                    _instanceCount = n;
                    n = Add(_wx[i] * ppd, _wy[i] * ppd, 0, r * glow * _norm[0], g * glow * _norm[0], bl * glow * _norm[0]);
                    if (flash > .05f)
                    {
                        float halo = flash * flash * 1.2f * trail;
                        _instanceCount = n;
                        n = Add(_wx[i] * ppd, _wy[i] * ppd, 1, r * halo * _norm[1], g * halo * _norm[1], bl * halo * _norm[1]);
                    }
                }
                break;
            }

            case DustShape.Ferro:
            {
                // a highlight sliding along the liquid, like a lamp moving over polished metal
                float sweep = (float)((_sheen % 1.4) / 1.4 * (_width + 400)) - 200;
                for (int i = 0; i < _waveCount; i++)
                {
                    float depth = 1 - MathF.Pow(_gv[i], 2.8f);
                    float skin = MathF.Pow(MathF.Max(0, (depth - .8f) / .2f), 2);
                    float lift = (_height - _wy[i]) / _height;
                    float xn = MathF.Abs((_wx[i] / _width) - .5f) * 2;
                    int hue = Math.Clamp((int)(xn * (PaletteSize - 1)), 0, PaletteSize - 1);
                    float sheen = MathF.Exp(-MathF.Pow((_wx[i] - sweep) / 90, 2));
                    // a dark body, a lit skin, the tips burning white
                    float b = ((.07f + (1.2f * skin)) * (.6f + (.4f * level)) + (sheen * skin * .8f)) * trail;
                    float white = MathF.Min(1, (lift * lift * 2.2f * skin) + (sheen * skin * .5f));
                    float r = (_palR[hue] * (1 - white)) + white, g = (_palG[hue] * (1 - white)) + white, bl = (_palB[hue] * (1 - white)) + white;
                    int s = skin > .3f ? 0 : 1;
                    _instanceCount = n;
                    n = Add(_wx[i] * ppd, _wy[i] * ppd, s, r * b * _norm[s], g * b * _norm[s], bl * b * _norm[s]);
                }
                break;
            }
        }

        return n;
    }
}
