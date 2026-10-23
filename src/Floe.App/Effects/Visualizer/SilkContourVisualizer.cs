using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floe.App.Services;
using Floe.Core.Interop;
using Floe.Core.Visualization;

namespace Floe.App.Effects.Visualizer;

/// <summary>A paced WPF host for the audio-driven satin contour surface.</summary>
public sealed class SilkContourVisualizer : FrameworkElement
{
    private const double FadeInSeconds = .22;
    private const double FadeOutSeconds = .40;
    private readonly SilkContourRenderer _renderer = new();
    private readonly SpectrumFrame _frame = new(24);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly float[] _red = new float[16], _green = new float[16], _blue = new float[16];
    private readonly int[] _colors = new int[16];
    private AudioSpectrum? _spectrum;
    private WriteableBitmap? _bitmap;
    private Rect _capsule = Rect.Empty;
    private bool _active, _hooked, _presentedEmpty = true;
    private double _fade, _sinceRepaint, _time;
    private double _lastFrameSeconds = -1;
    private int _paletteVersion = -1;

    public SilkContourVisualizer()
    {
        IsHitTestVisible = false;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);
        Loaded += (_, _) => { if (_active) Hook(); };
        Unloaded += (_, _) =>
        {
            Unhook();
            ReleaseCapture();
            _renderer.Reset();
            _fade = _sinceRepaint = 0;
            _lastFrameSeconds = -1;
        };
    }

    public Rect Capsule
    {
        get => _capsule;
        set
        {
            if (value.IsEmpty || !double.IsFinite(value.X) || !double.IsFinite(value.Y)
                || !double.IsFinite(value.Width) || !double.IsFinite(value.Height)
                || value.Width < 2 || value.Height < 2) value = Rect.Empty;
            if (_capsule == value) return;
            _capsule = value;
            InvalidateVisual();
        }
    }

    public bool IsActive
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            if (value)
            {
                _lastFrameSeconds = -1;
                _sinceRepaint = 0;
                if (IsLoaded) Hook();
            }
            else
            {
                ReleaseCapture();
            }
        }
    }

    internal SpectrumFrame? DebugFrame { get; set; }

    // Returns false when a silent, already clear strip needs no layered-window repaint.
    internal bool Step(double dt)
    {
        if (_capsule.IsEmpty) return false;
        _time += dt;
        var quality = Performance.Level switch
        {
            PerformanceLevel.Full => ContourQuality.Full,
            PerformanceLevel.Balanced => ContourQuality.Balanced,
            _ => ContourQuality.Lite,
        };
        _renderer.Resize(_capsule.Width, _capsule.Height, quality);
        if (_renderer.Width == 0 || _renderer.Height == 0) return false;
        if (_bitmap is null || _bitmap.PixelWidth != _renderer.Width || _bitmap.PixelHeight != _renderer.Height)
        {
            _bitmap = new WriteableBitmap(_renderer.Width, _renderer.Height, 96, 96, PixelFormats.Pbgra32, null);
            _presentedEmpty = true;
        }

        if (_paletteVersion != GlassSettings.ColorVersion || GlassSettings.DustColors == DustPalette.Rainbow)
        {
            _paletteVersion = GlassSettings.ColorVersion;
            VisualizerPalette.Fill(_red, _green, _blue, _time, Color.FromRgb(0x75, 0xD9, 0xED));
            for (int i = 0; i < _colors.Length; i++)
            {
                _colors[i] = ((int)(_red[i] * 255) << 16) | ((int)(_green[i] * 255) << 8) | (int)(_blue[i] * 255);
            }

            _renderer.SetPalette(_colors);
        }

        _renderer.Step(DebugFrame ?? _frame, dt, _active || DebugFrame is not null, DebugFrame is not null ? 1 : (float)_fade);
        if (_renderer.IsEmpty && _presentedEmpty) return false;
        _bitmap.WritePixels(new Int32Rect(0, 0, _renderer.Width, _renderer.Height), _renderer.Pixels, _renderer.Width * 4, 0);
        _presentedEmpty = _renderer.IsEmpty;
        return true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_bitmap is not null && !_capsule.IsEmpty && (_fade > .001 || DebugFrame is not null)) dc.DrawImage(_bitmap, _capsule);
    }

    private void Hook()
    {
        if (_hooked) return;
        CompositionTarget.Rendering += OnRendering;
        _hooked = true;
    }

    private void Unhook()
    {
        if (!_hooked) return;
        CompositionTarget.Rendering -= OnRendering;
        _hooked = false;
    }

    private void ReleaseCapture()
    {
        _spectrum?.Dispose();
        _spectrum = null;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        double now = e is RenderingEventArgs args ? args.RenderingTime.TotalSeconds : _clock.Elapsed.TotalSeconds;
        if (now <= _lastFrameSeconds) return;
        double dt = _lastFrameSeconds < 0 ? 1.0 / 60 : Math.Clamp(now - _lastFrameSeconds, .001, .1);
        _lastFrameSeconds = now;
        _sinceRepaint += dt;
        if (_sinceRepaint < VisualizerPacing.RepaintInterval) return;
        dt = Math.Min(_sinceRepaint, .1);
        _sinceRepaint = 0;
        if (_active)
        {
            _fade = Math.Min(1, _fade + (dt / FadeInSeconds));
            _spectrum ??= new AudioSpectrum();
            _spectrum.Read(_frame, dt);
        }
        else
        {
            _fade = Math.Max(0, _fade - (dt / FadeOutSeconds));
            if (_fade <= 0)
            {
                _renderer.Reset();
                _lastFrameSeconds = -1;
                Unhook();
                InvalidateVisual();
                return;
            }
        }

        if (Step(dt))
        {
            VisualizerPacing.MarkRepaint();
            InvalidateVisual();
        }
    }
}
