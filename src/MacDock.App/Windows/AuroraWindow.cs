using MacDock.App.Services;
using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MacDock.App.Effects.Visualizer;
using MacDock.Core.Interop;

namespace MacDock.App.Windows;

/// <summary>
/// "Hologram aurora" for the active icon: a curtain of fine white strands rising from behind
/// the icon, folding like the northern lights, their tips turning green and violet.
/// A click-through overlay kept directly under the dock window, so the icons stay in front
/// and the strands rise through the dock's glass.
/// </summary>
internal sealed class AuroraWindow : Window
{
    private const double OverlayWidth = 220;
    private const double OverlayHeight = 230;

    /// <summary>Where the icon centre sits inside the overlay (DIP).</summary>
    private const double AnchorX = OverlayWidth / 2;
    private const double AnchorY = 210;

    private const double FadeSeconds = 0.35;

    private readonly AuroraVisual _visual = new(AnchorX, AnchorY);
    private readonly PacedClock _clock;
    private readonly Func<(Point Center, double Scale)?> _anchor;
    private readonly IntPtr _dock;
    private readonly bool _excludeFromCapture;
    private IntPtr _hwnd;
    private double _fade;
    private double _target;
    private double _last = -1;
    private double _lastRaster = double.NegativeInfinity;

    /// <param name="anchor">Active icon centre in screen DIP and its on-screen scale; null to fade out.</param>
    public AuroraWindow(IntPtr dock, Func<(Point Center, double Scale)?> anchor, bool excludeFromCapture)
    {
        _dock = dock;
        _anchor = anchor;
        _excludeFromCapture = excludeFromCapture;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        Title = "MacDock Aurora";
        Width = OverlayWidth;
        Height = OverlayHeight;
        Content = _visual;

        // a separate window: it re-presents on its own, so it ticks on every visualizer frame
        _clock = new PacedClock(Tick, 1, everyFrameWhileDockAnimates: false);
    }

    protected override void OnClosed(EventArgs e)
    {
        CaptureReveal.Forget(_hwnd);
        _clock.Stop();
        base.OnClosed(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        WindowStyling.MakeClickThrough(_hwnd);
        if (_excludeFromCapture)
        {
            CaptureReveal.Exclude(_hwnd);
        }
    }

    /// <summary>Fades the aurora in at the anchor (or keeps it there).</summary>
    public void Begin()
    {
        if (_target == 1 && IsVisible)
        {
            Track();
            return;
        }

        _target = 1;
        if (!Track())
        {
            return;
        }

        if (!IsVisible)
        {
            _fade = 0;
            Show();
        }

        WindowStyling.PlaceBelow(_hwnd, _dock);
        _clock.Start();
    }

    /// <summary>Fades out and hides.</summary>
    public void End() => _target = 0;

    /// <summary>Moves onto the anchor; false when there is none.</summary>
    public bool Track()
    {
        if (_anchor() is not { } anchor)
        {
            _target = 0;
            return false;
        }

        var left = Math.Round(anchor.Center.X - AnchorX, 1);
        var top = Math.Round(anchor.Center.Y - AnchorY, 1);
        if (Math.Abs(left - Left) > 0.2)
        {
            Left = left;
        }

        if (Math.Abs(top - Top) > 0.2)
        {
            Top = top;
        }

        _visual.Scale = anchor.Scale;
        return true;
    }

    private void Tick(double now)
    {
        // the dock and the visualizer both drive this clock: 24 fps loses nothing a smooth curtain shows
        if (now - _lastRaster < 1.0 / 24)
        {
            return;
        }

        _lastRaster = now;
        var dt = _last < 0 ? 1.0 / 30 : Math.Clamp(now - _last, 0.001, 0.1);
        _last = now;
        if (_target > 0)
        {
            Track();
        }

        _fade = _target > _fade ? Math.Min(_target, _fade + (dt / FadeSeconds)) : Math.Max(_target, _fade - (dt / FadeSeconds));
        if (_fade <= 0 && _target <= 0)
        {
            _clock.Stop();
            _last = -1;
            Hide();
            return;
        }

        _visual.Render(now, _fade);
    }
}

/// <summary>
/// Rasterises the strands itself into one bitmap, added together like light (the canvas
/// preview's "lighter"). 300 gradient strokes a frame through WPF cost the render thread
/// far more, and that thread is shared with the visualizer.
/// </summary>
internal sealed class AuroraVisual : FrameworkElement
{
    // narrow enough that it reads as one icon's light: bases across the icon, tips fanning a little wider
    private const int Strands = 64;
    private const double BaseSpan = 50;
    private const double Fan = 1.45;
    private const double CurtainHeight = 115;
    private const double Fold = 9;
    private const double Sway = 4;

    /// <summary>Half the dock's icon box; the strands leave from the icon's top edge.</summary>
    private const double IconHalf = 27;
    private const double HueFrom = 150;
    private const double HueSpan = 90;
    private const double Speed = 0.8;
    private const double CoreWidth = 1.0;
    private const double GlowWidth = 4.5;
    private const float GlowAlpha = 0.12f;

    /// <summary>The preview's canvas antialiases a little wider than this rasteriser; more light makes up for it.</summary>
    private const float Gain = 1.5f;
    private const int Buckets = 32;
    private const int Steps = 64;

    /// <summary>Premultiplied rgb along a strand, base to tip, per hue bucket.</summary>
    private static readonly float[][] Ramp = BuildRamps();

    private readonly double _anchorX;
    private readonly double _anchorY;
    private readonly double[] _seed = new double[Strands];
    private WriteableBitmap? _bitmap;
    private float[] _light = Array.Empty<float>();
    private byte[] _pixels = Array.Empty<byte>();
    private int _width;
    private int _height;
    private double _dpi = 1;

    public AuroraVisual(double anchorX, double anchorY)
    {
        _anchorX = anchorX;
        _anchorY = anchorY;
        IsHitTestVisible = false;
        for (var i = 0; i < Strands; i++)
        {
            var x = Math.Sin((i * 127.1) + 311.7) * 43758.5453;
            _seed[i] = x - Math.Floor(x);
        }
    }

    /// <summary>On-screen icon scale (the dock magnifies and compacts); the curtain grows with it.</summary>
    public double Scale { get; set; } = 1;

    public void Render(double t, double fade)
    {
        Opacity = fade;
        if (!EnsureBitmap())
        {
            return;
        }

        Array.Clear(_light);

        // the compact dock shrinks icons to ~0.6; the curtain only follows part of the way
        var s = Math.Clamp(0.8 + ((Scale - 1) * 0.5), 0.8, 1.3);
        var k = s * _dpi;
        var ax = _anchorX * _dpi;
        var topY = (_anchorY - (IconHalf * Scale)) * _dpi;
        Source(ax, topY, IconHalf * Scale * 0.8 * _dpi, s * _dpi, t);
        // a tent of radius w carries as much light as a solid line w wide
        var core = Math.Max(1, CoreWidth * k);
        var glow = GlowWidth * k;

        for (var i = 0; i < Strands; i++)
        {
            var u = i / (double)(Strands - 1);
            var xn = (u - 0.5) * BaseSpan;
            var env = Math.Exp(-Math.Pow(xn / (BaseSpan * 0.45), 2));
            var fold = (Math.Sin((u * 9) + (t * 0.7)) * Fold) + (Math.Sin((u * 23) - (t * 1.3)) * Fold * 0.35);
            var burst = Math.Pow(Math.Max(0, Math.Sin((u * 31) + (t * 3.1) + (_seed[i] * 6))), 3);
            var h = CurtainHeight * env * Noise(u * 6, t * Speed) * (0.75 + (0.5 * burst));
            if (h < 3)
            {
                continue;
            }

            var flick = Gain * (float)(0.65 + (0.35 * Math.Sin((t * (4 + (_seed[i] * 6))) + i)));
            // the base stays put on the icon; only the upper part folds and sways
            var bx = xn;
            var tx = (xn * Fan) + fold + (Math.Sin((t * 1.2) + (u * 5)) * Sway);
            var cx = bx + ((tx - bx) * 0.1);
            var hue = Noise((u * 2.3) + 3, t * 0.4);
            var ramp = Ramp[Math.Clamp((int)Math.Round(hue * (Buckets - 1)), 0, Buckets - 1)];
            Strand(ax + (bx * k), ax + (cx * k), ax + (tx * k), topY, h * k, core, glow, ramp, flick);
        }

        Present();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_bitmap is not null)
        {
            dc.DrawImage(_bitmap, new Rect(0, 0, _width / _dpi, _height / _dpi));
        }
    }

    /// <summary>The bright seam on the icon's top edge the strands rise from, so the light clearly belongs to this icon.</summary>
    private void Source(double cx, double y, double halfWidth, double k, double t)
    {
        var pulse = (float)(0.85 + (0.15 * Math.Sin(t * 2.2)));
        var glow = 5 * k;
        var reachX = (int)Math.Ceiling(halfWidth * 1.3);
        var reachY = (int)Math.Ceiling(glow);
        for (var dy = -reachY; dy <= reachY; dy++)
        {
            var row = (int)y + dy;
            if (row < 0 || row >= _height)
            {
                continue;
            }

            var d = Math.Abs(row + 0.5 - y);
            var vertical = (float)(Math.Max(0, 1 - (d / Math.Max(1, 1.2 * k))) + (0.22 * Math.Max(0, 1 - (d / glow))));
            for (var dx = -reachX; dx <= reachX; dx++)
            {
                var px = (int)cx + dx;
                if (px < 0 || px >= _width)
                {
                    continue;
                }

                var across = (px + 0.5 - cx) / halfWidth;
                var w = vertical * pulse * (float)Math.Exp(-across * across * across * across);
                var o = ((row * _width) + px) * 3;
                _light[o] += 0.72f * w;
                _light[o + 1] += 1.0f * w;
                _light[o + 2] += 0.95f * w;
            }
        }
    }

    /// <summary>One strand: a quadratic curve from the base up to the tip, a pixel row at a time.</summary>
    private void Strand(double bx, double cx, double tx, double baseY, double h, double core, double glow, float[] ramp, float flick)
    {
        var rows = (int)Math.Ceiling(h);
        var reach = (int)Math.Ceiling(glow);
        for (var r = 0; r <= rows; r++)
        {
            // the control point sits near mid-height, so height along the curve is close to linear in its parameter
            var v = r / h;
            if (v > 1)
            {
                break;
            }

            var y = (int)(baseY - r);
            if (y < 0 || y >= _height)
            {
                continue;
            }

            var step = Math.Min(Steps - 1, (int)(v * (Steps - 1)));
            var cr = ramp[step * 3] * flick;
            var cg = ramp[(step * 3) + 1] * flick;
            var cb = ramp[(step * 3) + 2] * flick;
            if (cr + cg + cb < 0.004f)
            {
                continue;
            }

            var x = ((1 - v) * (1 - v) * bx) + (2 * (1 - v) * v * cx) + (v * v * tx);
            var left = Math.Max(0, (int)x - reach);
            var right = Math.Min(_width - 1, (int)x + reach);
            var row = y * _width * 3;
            for (var px = left; px <= right; px++)
            {
                var d = Math.Abs(px + 0.5 - x);
                var w = (float)Math.Max(0, 1 - (d / core)) + (GlowAlpha * (float)Math.Max(0, 1 - (d / glow)));
                if (w <= 0)
                {
                    continue;
                }

                var o = row + (px * 3);
                _light[o] += cr * w;
                _light[o + 1] += cg * w;
                _light[o + 2] += cb * w;
            }
        }
    }

    private void Present()
    {
        for (int i = 0, o = 0; i < _light.Length; i += 3, o += 4)
        {
            var r = Math.Min(1f, _light[i]);
            var g = Math.Min(1f, _light[i + 1]);
            var b = Math.Min(1f, _light[i + 2]);
            _pixels[o] = (byte)(b * 255);
            _pixels[o + 1] = (byte)(g * 255);
            _pixels[o + 2] = (byte)(r * 255);
            _pixels[o + 3] = (byte)(Math.Max(r, Math.Max(g, b)) * 255);
        }

        _bitmap!.WritePixels(new Int32Rect(0, 0, _width, _height), _pixels, _width * 4, 0);
    }

    private bool EnsureBitmap()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return false;
        }

        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var width = (int)Math.Round(ActualWidth * dpi);
        var height = (int)Math.Round(ActualHeight * dpi);
        if (_bitmap is not null && width == _width && height == _height)
        {
            return true;
        }

        _dpi = dpi;
        _width = width;
        _height = height;
        _light = new float[width * height * 3];
        _pixels = new byte[width * height * 4];
        _bitmap = new WriteableBitmap(width, height, 96 * dpi, 96 * dpi, PixelFormats.Pbgra32, null);
        InvalidateVisual();
        return true;
    }

    private static float[][] BuildRamps()
    {
        // the preview's strand gradient: clear at the base, white low down, the hue, then hue + 50 fading out at the tip
        (double At, double Hue, double Sat, double Light, double Alpha)[] stops =
        {
            (0, 0, 0.40, 0.95, 0), (0.12, 0, 0.40, 0.96, 0.55), (0.55, 0, 0.90, 0.72, 0.35),
            (0.85, 50, 0.95, 0.66, 0.18), (1, 60, 0.95, 0.60, 0),
        };
        var ramps = new float[Buckets][];
        for (var bucket = 0; bucket < Buckets; bucket++)
        {
            var hue = HueFrom + (HueSpan * bucket / (Buckets - 1));
            var ramp = ramps[bucket] = new float[Steps * 3];
            for (var step = 0; step < Steps; step++)
            {
                var v = step / (double)(Steps - 1);
                var n = 1;
                while (n < stops.Length - 1 && stops[n].At < v)
                {
                    n++;
                }

                var (p, q) = (stops[n - 1], stops[n]);
                var f = Math.Clamp((v - p.At) / (q.At - p.At), 0, 1);
                var (r, g, b) = Hsl(hue + p.Hue + ((q.Hue - p.Hue) * f), p.Sat + ((q.Sat - p.Sat) * f), p.Light + ((q.Light - p.Light) * f));
                var alpha = p.Alpha + ((q.Alpha - p.Alpha) * f);
                ramp[step * 3] = (float)(r * alpha);
                ramp[(step * 3) + 1] = (float)(g * alpha);
                ramp[(step * 3) + 2] = (float)(b * alpha);
            }
        }

        return ramps;
    }

    private static double Noise(double x, double t) =>
        0.5 + (0.3 * Math.Sin((x * 1.7) + (t * 0.9))) + (0.15 * Math.Sin((x * 3.9) - (t * 1.6))) + (0.08 * Math.Sin((x * 8.3) + (t * 2.7)));

    private static (double R, double G, double B) Hsl(double hue, double sat, double light)
    {
        hue = ((hue % 360) + 360) % 360 / 360;
        var q = light < 0.5 ? light * (1 + sat) : light + sat - (light * sat);
        var p = (2 * light) - q;
        double Channel(double t)
        {
            t = t < 0 ? t + 1 : t > 1 ? t - 1 : t;
            return t < 1.0 / 6 ? p + ((q - p) * 6 * t) : t < 0.5 ? q : t < 2.0 / 3 ? p + ((q - p) * ((2.0 / 3) - t) * 6) : p;
        }

        return (Channel(hue + (1.0 / 3)), Channel(hue), Channel(hue - (1.0 / 3)));
    }
}
