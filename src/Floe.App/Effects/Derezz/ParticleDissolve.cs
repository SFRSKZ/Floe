using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Floe.App.Effects.Derezz;

/// <summary>
/// The dock comes apart into a river of sharp points: a front sweeps the bar from left to right, every pixel it
/// passes lets go and streams off to the right as a rippling ribbon that spreads into spray and fades. Each point's
/// path is closed-form in time, so the same field plays backwards to put the dock together again.
/// Works in the snapshot's own pixels; the layer is drawn at that resolution, one dot per point, no blur.
/// </summary>
internal sealed class ParticleDissolve
{
    // tuned on a 1.5x screen: distances scale with the snapshot's pixels per dock unit
    private const double TunedScale = 1.5;
    private const double Sweep = .8;
    private const double MaxJitter = .22;
    private const double MaxLife = 1.3;

    private readonly int _count;
    private readonly float[] _homeX, _homeY, _sampleX, _sampleY, _release, _speed, _gain, _life, _sprayX, _sprayY, _band, _lift;
    private readonly uint[] _homeColor;
    private readonly int[] _hot;
    private readonly bool[] _star;
    private readonly int _step;
    private readonly double _k;

    public ParticleDissolve(BitmapSource snapshot, Rect capsule, int seed = 7)
    {
        Capsule = capsule;
        int sw = snapshot.PixelWidth, sh = snapshot.PixelHeight;
        Scale = sw / Math.Max(1, capsule.Width);
        _k = Scale / TunedScale;
        var pixels = new uint[sw * sh];
        var source = snapshot.Format == PixelFormats.Pbgra32 ? snapshot : new FormatConvertedBitmap(snapshot, PixelFormats.Pbgra32, null, 0);
        source.CopyPixels(pixels, sw * 4, 0);

        // one point per step x step block, coarser on big screens so the count stays near 45k
        _step = Math.Max(2, (int)Math.Ceiling(Math.Sqrt(sw * (double)sh / 45000)));
        var rng = new Random(seed);

        // the bar's own bright colour: what its dark parts glow as once they let go
        double ar = 0, ag = 0, ab = 0, an = 0;
        foreach (var p in pixels)
        {
            int a = (int)(p >> 24);
            if (a < 128) continue;
            int r = (int)((p >> 16) & 255), g = (int)((p >> 8) & 255), b = (int)(p & 255);
            double lum = (.3 * r) + (.59 * g) + (.11 * b);
            if (lum < 90) continue;
            double w = lum * lum;
            ar += r * w; ag += g * w; ab += b * w; an += w;
        }

        if (an <= 0) { ar = 0x02; ag = 0xD7; ab = 0xF2; an = 1; }
        ar /= an; ag /= an; ab /= an;

        var list = new List<int>();
        for (int y = 0; y < sh; y += _step)
        {
            for (int x = 0; x < sw; x += _step)
            {
                uint p = pixels[(y * sw) + x];
                if ((p >> 24) < 128) continue;
                list.Add((y * sw) + x);
            }
        }

        _count = list.Count;
        _homeX = new float[_count]; _homeY = new float[_count]; _sampleX = new float[_count]; _sampleY = new float[_count];
        _release = new float[_count]; _speed = new float[_count]; _gain = new float[_count]; _life = new float[_count];
        _sprayX = new float[_count]; _sprayY = new float[_count]; _band = new float[_count]; _lift = new float[_count];
        _homeColor = new uint[_count]; _hot = new int[_count]; _star = new bool[_count];

        double left = capsule.Left * Scale, top = capsule.Top * Scale;
        int kept = 0;
        foreach (int index in list)
        {
            int x = index % sw, y = index / sw;
            uint p = pixels[index];
            int a = (int)(p >> 24);
            // premultiplied: back to straight colour
            double r = ((p >> 16) & 255) * 255.0 / a, g = ((p >> 8) & 255) * 255.0 / a, b = (p & 255) * 255.0 / a;
            double lum = (.3 * r) + (.59 * g) + (.11 * b);
            bool dark = lum < 64;
            if (dark && rng.NextDouble() < .55) continue;

            double u = x / (double)sw, v = y / (double)sh;
            double xd = x / _k, yd = y / _k;
            int i = kept++;
            _homeX[i] = (float)(left + x);
            _homeY[i] = (float)(top + y);
            _sampleX[i] = (float)xd;
            _sampleY[i] = (float)yd;
            double jitter = rng.NextDouble();
            _release[i] = (float)((Sweep * u) + (jitter * jitter * MaxJitter) + ((1 - v) * .06));
            _speed[i] = (float)(160 + (120 * ((Coherent(xd, yd, .006, .02, .4, .0021) * .5) + .5)) + (rng.NextDouble() * 30));
            _gain[i] = (float)(260 + (200 * rng.NextDouble()));
            _life[i] = (float)(.7 + (.6 * rng.NextDouble()));
            _sprayX[i] = (float)Gauss(rng);
            _sprayY[i] = (float)Gauss(rng);
            _band[i] = (float)((Math.Sin((yd * .21) + (xd * .004)) * .5) + .5);
            _lift[i] = (float)(34 * (1 - (v * .5)));
            _star[i] = rng.NextDouble() < .012;
            _homeColor[i] = p | 0xFF000000u;

            double hr, hg, hb;
            if (dark) { hr = ar * .55; hg = ag * .55; hb = ab * .55; }
            else { hr = Math.Min(255, (r * 1.1) + 80); hg = Math.Min(255, (g * 1.1) + 80); hb = Math.Min(255, (b * 1.1) + 80); }
            _hot[i] = ((int)hr << 16) | ((int)hg << 8) | (int)hb;
        }

        _count = kept;
    }

    /// <summary>Snapshot pixels per dock unit: the layer is drawn at this resolution.</summary>
    public double Scale { get; }

    public Rect Capsule { get; }

    public int Count => _count;

    /// <summary>Seconds until the last point has faded, at speed 1.</summary>
    public static double Duration => Sweep + MaxJitter + .06 + MaxLife;

    /// <summary>Where the front stands, in dock units: the live dock shows only to its right.</summary>
    public double FrontX(double t) => Capsule.Left + (Math.Clamp(t / Sweep, 0, 1) * Capsule.Width);

    /// <summary>Draws the points at time t into a Pbgra32 buffer of the layer; returns the touched rows' bounds.</summary>
    public Int32Rect Render(uint[] buffer, int width, int height, double t)
    {
        int minX = width, minY = height, maxX = -1, maxY = -1;
        double front = FrontX(t) * Scale;
        double k = _k;
        double fadeTop = 22 * k, fadeBottom = 14 * k;
        for (int i = 0; i < _count; i++)
        {
            double a = t - _release[i];
            if (a <= 0)
            {
                // still in place behind the front: the dock's own pixel, the mask has taken the live one away
                if (_homeX[i] >= front) continue;
                int hx = (int)_homeX[i], hy = (int)_homeY[i];
                if (hx < 0 || hy < 0 || hx + _step > width || hy + _step > height) continue;
                uint c = _homeColor[i];
                for (int dy = 0; dy < _step; dy++)
                {
                    int row = (hy + dy) * width;
                    for (int dx = 0; dx < _step; dx++) buffer[row + hx + dx] = c;
                }

                Grow(hx, hy, hx + _step, hy + _step);
                continue;
            }

            double life = _life[i];
            if (a >= life) continue;
            double band = _band[i];
            double d = ((_speed[i] * (.55 + (.9 * band))) * a) + (.5 * _gain[i] * a * a);
            double px = _homeX[i] + (d * k);
            double lift = _lift[i] * (1 - Math.Exp(-a * 2.5));
            double amp = 30 * (1 - Math.Exp(-a * 2.2));
            double py = _homeY[i] + (k * (-lift + (amp * Math.Sin((px / k * .0085) - (t * 5.0) + (_sampleY[i] * .035) + (band * 1.2)))));
            double spray = Math.Pow(a, 1.6) * 34 * k;
            px += _sprayX[i] * spray;
            py += _sprayY[i] * spray * .6;

            // the ribbons' crests carry the light: bright filaments through a dimmer haze of points
            double crest = band * band * band;
            crest *= crest;
            double alpha = (1 + (.8 * Math.Exp(-a * 7))) * Math.Pow(1 - (a / life), 1.3) * (1.0 + (2.6 * crest));
            alpha *= Math.Clamp(py / fadeTop, 0, 1) * Math.Clamp((height - py) / fadeBottom, 0, 1);
            if (alpha <= .02) continue;

            int ix = (int)Math.Round(px), iy = (int)Math.Round(py);
            if (ix < 1 || iy < 1 || ix >= width - 1 || iy >= height - 1) continue;
            int hot = _hot[i];
            int r = (int)(((hot >> 16) & 255) * alpha), g = (int)(((hot >> 8) & 255) * alpha), b = (int)((hot & 255) * alpha);
            Add(buffer, (iy * width) + ix, r, g, b);
            if (_star[i])
            {
                int r2 = r * 6 / 10, g2 = g * 6 / 10, b2 = b * 6 / 10;
                Add(buffer, (iy * width) + ix - 1, r2, g2, b2);
                Add(buffer, (iy * width) + ix + 1, r2, g2, b2);
                Add(buffer, ((iy - 1) * width) + ix, r2, g2, b2);
                Add(buffer, ((iy + 1) * width) + ix, r2, g2, b2);
            }

            Grow(ix - 1, iy - 1, ix + 2, iy + 2);
        }

        return maxX < 0 ? Int32Rect.Empty : new Int32Rect(minX, minY, maxX - minX, maxY - minY);

        void Grow(int x0, int y0, int x1, int y1)
        {
            if (x0 < minX) minX = x0;
            if (y0 < minY) minY = y0;
            if (x1 > maxX) maxX = x1;
            if (y1 > maxY) maxY = y1;
        }
    }

    // additive light: the channels add up and saturate, alpha follows the brightest so it reads as glow on a clear window
    private static void Add(uint[] buffer, int at, int r, int g, int b)
    {
        uint p = buffer[at];
        int nr = Math.Min(255, (int)((p >> 16) & 255) + r);
        int ng = Math.Min(255, (int)((p >> 8) & 255) + g);
        int nb = Math.Min(255, (int)(p & 255) + b);
        int na = Math.Max((int)(p >> 24), Math.Max(nr, Math.Max(ng, nb)));
        buffer[at] = ((uint)na << 24) | ((uint)nr << 16) | ((uint)ng << 8) | (uint)nb;
    }

    private static double Coherent(double x, double y, double a, double b, double c, double d) =>
        Math.Sin((x * a) + (y * b) + c) * Math.Cos((x * d) - (c * 1.7));

    private static double Gauss(Random rng)
    {
        double u1 = 1 - rng.NextDouble(), u2 = rng.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }
}
