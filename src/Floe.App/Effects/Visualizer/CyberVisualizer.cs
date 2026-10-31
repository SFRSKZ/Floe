using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Floe.App.Effects.Cyber;
using Floe.App.Services;
using Floe.Core.Interop;

namespace Floe.App.Effects.Visualizer;

public enum CyberVisualLook { Bars, Pulse, Grid, Code }

/// <summary>The cyber themes' strip: chamfered bars, a heartbeat line, a wireframe net or rising code dust, all hard edged.</summary>
public sealed class CyberVisualizer : FrameworkElement
{
    private const int Count = 24;
    private const double Chamfer = 4;
    private const string CodeGlyphs = "01X7A#$%/&";
    private readonly SpectrumFrame _frame = new(Count);
    private readonly double[] _bands = new double[Count];
    private AudioSpectrum? _capture;
    private bool _active, _hooked;
    private double _fade, _time, _last = -1, _elapsed;
    private FormattedText[,]? _glyphs;
    private Color _glyphsFor;

    public CyberVisualizer()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => { if (_active) Hook(); };
        Unloaded += (_, _) => { Unhook(); Release(); _fade = 0; _last = -1; };
    }

    public CyberVisualLook Look { get; set; }

    public bool IsActive
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            if (value) { _last = -1; if (IsLoaded) Hook(); }
            else Release();
        }
    }

    internal void Step(double dt)
    {
        _time += dt;
        for (var i = 0; i < Count; i++)
        {
            double target = i < _frame.Bands.Length ? Math.Clamp(_frame.Bands[i], 0, 1) : 0;
            _bands[i] += (target - _bands[i]) * (1 - Math.Exp(-dt / (target > _bands[i] ? .04 : .16)));
        }
    }

    /// <summary>One still of a look with a made-up spectrum, for --renderviz=look:path.</summary>
    internal static void RenderStill(CyberVisualLook look, string path)
    {
        const int width = 1600, height = 76;
        var strip = new CyberVisualizer { Look = look, _fade = 1, _time = 1.3 };
        for (var i = 0; i < Count; i++) strip._bands[i] = .2 + (.75 * Math.Abs(Math.Sin(i * .55)) * (1 - (i / 40.0)));
        strip.Measure(new Size(width, height));
        strip.Arrange(new Rect(0, 0, width, height));
        var host = new DrawingVisual();
        using (var dc = host.RenderOpen())
        {
            dc.DrawRectangle(CyberFxLayer.Solid(Color.FromRgb(0x24, 0x18, 0x48)), null, new Rect(0, 0, width, height));
            strip.OnRender(dc);
        }

        var target = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(host);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(target));
        using var file = System.IO.File.Create(path);
        encoder.Save(file);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_fade <= 0 || ActualWidth < 2) return;
        var look = CyberLook.Current;
        dc.PushOpacity(_fade);
        switch (Look)
        {
            case CyberVisualLook.Pulse: DrawPulse(dc, look); break;
            case CyberVisualLook.Grid: DrawGrid(dc); break;
            case CyberVisualLook.Code: DrawCode(dc, look); break;
            default: DrawBars(dc, look); break;
        }

        dc.Pop();
    }

    // the bands mirrored about the middle, so the bass sits at both ends of the strip
    private double Band(double u)
    {
        double at = Math.Abs((u * 2) - 1) * (Count - 1);
        int low = (int)at, high = Math.Min(Count - 1, low + 1);
        return _bands[low] + ((_bands[high] - _bands[low]) * (at - low));
    }

    private void DrawBars(DrawingContext dc, CyberLook look)
    {
        const double step = 13, width = 9;
        var body = CyberFxLayer.Solid(look.Body);
        var accent = CyberFxLayer.Solid(look.Accent);
        int count = (int)(ActualWidth / step);
        double bottom = ActualHeight;
        for (var i = 0; i < count; i++)
        {
            double height = 3 + (Band((double)i / count) * (bottom - 6));
            var r = new Rect(i * step, bottom - height, width, height);
            var bar = new StreamGeometry();
            using (var c = bar.Open())
            {
                c.BeginFigure(r.BottomLeft, true, true);
                c.PolyLineTo(new[] { new Point(r.Left, r.Top + Chamfer), new Point(r.Left + Chamfer, r.Top), r.TopRight, r.BottomRight }, false, false);
            }

            dc.DrawGeometry(i % 6 == 5 ? accent : body, null, bar);
        }
    }

    // a heartbeat: flat line, then a hard spike whose height is the music at that spot
    private void DrawPulse(DrawingContext dc, CyberLook look)
    {
        const double beat = 96, spike = 26, step = 2;
        var pen = new Pen(CyberFxLayer.Solid(look.Accent), 2) { LineJoin = PenLineJoin.Miter };
        pen.Freeze();
        var line = new StreamGeometry();
        double middle = ActualHeight * .6;
        using (var c = line.Open())
        {
            c.BeginFigure(new Point(0, middle), false, false);
            for (double x = step; x <= ActualWidth; x += step)
            {
                double at = (x + (_time * 70)) % beat;
                double height = Band(x / ActualWidth) * ActualHeight * .56;
                double swing = at >= spike ? 0
                    : at < spike / 4 ? -(at / (spike / 4)) * height
                    : at < spike * .625 ? -height + (((at - (spike / 4)) / (spike * .375)) * height * 1.45)
                    : (1 - ((at - (spike * .625)) / (spike * .375))) * height * .45;
                c.LineTo(new Point(x, middle + swing), true, false);
            }
        }

        dc.DrawGeometry(null, pen, line);
    }

    // the net: rows of peaks in perspective, tied together by the columns
    private void DrawGrid(DrawingContext dc)
    {
        const int rows = 7, columns = 120, every = 4;
        var pen = new Pen(CyberFxLayer.Solid(Color.FromRgb(0x02, 0xD7, 0xF2)), 1);
        pen.Freeze();

        Point At(int r, int i)
        {
            double z = (double)r / (rows - 1), u = (double)i / columns;
            double spread = .62 + (z * .38);
            double x = (.5 + ((u - .5) / spread)) * ActualWidth;
            double y = ActualHeight * (.42 + (z * z * .58));
            double peak = Math.Pow(Math.Abs(Math.Sin((u * 26) + (r * 1.7) + (_time * .7))), 3);
            return new Point(x, y - (Band(u) * peak * ActualHeight * .62 * (1 - (z * .55))));
        }

        var net = new StreamGeometry();
        using (var c = net.Open())
        {
            for (var r = 0; r < rows; r++)
            {
                c.BeginFigure(At(r, 0), false, false);
                for (var i = 1; i <= columns; i++) c.LineTo(At(r, i), true, false);
            }

            for (var i = 0; i <= columns; i += every)
            {
                c.BeginFigure(At(0, i), false, false);
                for (var r = 1; r < rows; r++) c.LineTo(At(r, i), true, false);
            }
        }

        dc.DrawGeometry(null, pen, net);
    }

    // dust made of single characters on a fixed column grid, mostly the dock's colour; the music lifts each column
    private void DrawCode(DrawingContext dc, CyberLook look)
    {
        const int count = 150;
        const double column = 9, glyph = 12;
        var glyphs = CodeGlyphSheet(look, glyph);
        for (var i = 0; i < count; i++)
        {
            double u = CyberFxLayer.Random(i + .1), v = CyberFxLayer.Random(i + .2), tone = CyberFxLayer.Random(i + .3);
            double speed = .4 + CyberFxLayer.Random(i + .4);
            double x = Math.Round(u * ActualWidth / column) * column;
            double rise = ((v * ActualHeight) + (_time * 23 * speed)) % ActualHeight;
            double y = ActualHeight - glyph - (rise * (.35 + (Band(Math.Clamp(x / ActualWidth, 0, 1)) * .65)));
            int shade = tone < .72 ? 0 : tone < .92 ? 1 : 2;
            dc.DrawText(glyphs[shade, (i + (int)(_time * 6 * speed)) % CodeGlyphs.Length], new Point(x, y));
        }
    }

    private FormattedText[,] CodeGlyphSheet(CyberLook look, double size)
    {
        if (_glyphs is not null && _glyphsFor == look.Body) return _glyphs;
        var typeface = new Typeface(CyberLook.Font, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var shades = new[] { look.Body, look.Accent, Color.FromRgb(0x02, 0xD7, 0xF2) };
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        _glyphs = new FormattedText[shades.Length, CodeGlyphs.Length];
        for (var s = 0; s < shades.Length; s++)
        {
            for (var g = 0; g < CodeGlyphs.Length; g++)
            {
                _glyphs[s, g] = new FormattedText(CodeGlyphs[g].ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    typeface, size, CyberFxLayer.Solid(shades[s]), dip);
            }
        }

        _glyphsFor = look.Body;
        return _glyphs;
    }

    private void Hook() { if (_hooked) return; CompositionTarget.Rendering += RenderFrame; _hooked = true; }
    private void Unhook() { if (!_hooked) return; CompositionTarget.Rendering -= RenderFrame; _hooked = false; }
    private void Release() { _capture?.Dispose(); _capture = null; }

    private void RenderFrame(object? sender, EventArgs e)
    {
        double now = e is RenderingEventArgs r ? r.RenderingTime.TotalSeconds : 0;
        if (_last < 0) { _last = now; return; }
        double dt = Math.Clamp(now - _last, 0, .1);
        _last = now;
        _elapsed += dt;
        if (_elapsed < VisualizerPacing.RepaintInterval) return;
        double step = _elapsed;
        _elapsed = 0;
        if (_active) { _fade = Math.Min(1, _fade + (step / .22)); _capture ??= new AudioSpectrum(); _capture.Read(_frame, step); }
        else
        {
            _fade = Math.Max(0, _fade - (step / .4));
            if (_fade <= 0) { Unhook(); _last = -1; InvalidateVisual(); return; }
        }

        Step(step);
        InvalidateVisual();
        VisualizerPacing.MarkRepaint();
    }
}
