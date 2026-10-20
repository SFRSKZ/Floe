using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using MacDock.App.Services;
using MacDock.Core.Interop;

namespace MacDock.App.Windows;

public partial class DesktopButtonWindow : Window
{
    private const double BaseBox = 72;
    private static readonly Color CoreRed = Color.FromRgb(0xFF, 0x3B, 0x3B);
    private static readonly int[] MountainHeights = { 1, 3, 4, 5, 4, 3, 2, 1 };

    public event Action? Clicked;

    private readonly List<Ellipse> _dots = new();
    private readonly List<TranslateTransform> _spreads = new();
    private readonly List<Ellipse> _zigDots = new();
    private readonly List<int> _zigColumns = new();
    private RotateTransform? _comet;
    private Ellipse? _core;
    private double _scale = 1;
    private double _brightness = 1;
    private double _zigAlpha;
    private bool _animating;
    private long _lastTick;
    private double _phase;
    private double _hover;
    private double _spread;

    public DesktopButtonWindow()
    {
        InitializeComponent();
        DesktopButtonSettings.Load();
        DesktopButtonSettings.Changed += Build;
        Build();

        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                EnsureAnimation();
            }
            else
            {
                StopAnimation();
            }
        };
        MouseEnter += (_, _) => _hover = 1;
        MouseLeave += (_, _) => _hover = 0;
        MouseLeftButtonUp += (_, _) => Clicked?.Invoke();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowStyling.MakeNonActivatingToolWindow(new WindowInteropHelper(this).Handle);
    }

    protected override void OnClosed(EventArgs e)
    {
        StopAnimation();
        DesktopButtonSettings.Changed -= Build;
        base.OnClosed(e);
    }

    private void EnsureAnimation()
    {
        if (_animating)
        {
            return;
        }

        _animating = true;
        _lastTick = System.Diagnostics.Stopwatch.GetTimestamp();
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopAnimation()
    {
        if (!_animating)
        {
            return;
        }

        _animating = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var dt = (now - _lastTick) / (double)System.Diagnostics.Stopwatch.Frequency;
        _lastTick = now;
        if (dt <= 0 || dt > 0.1)
        {
            dt = 1.0 / 60.0;
        }

        Animate(dt);
    }

    private void Build()
    {
        Root.Children.Clear();
        _dots.Clear();
        _spreads.Clear();
        _zigDots.Clear();
        _zigColumns.Clear();
        _comet = null;
        _core = null;
        _spread = 0;

        var scale = _scale = DesktopButtonSettings.Size;
        _brightness = DesktopButtonSettings.Brightness;
        var box = BaseBox * scale;
        var center = box / 2;
        var color = DesktopButtonSettings.Color;

        Width = box;
        Height = box;

        var dotBrush = new SolidColorBrush(color);
        dotBrush.Freeze();

        switch (DesktopButtonSettings.Style)
        {
            case DesktopButtonStyle.Windows:
                BuildWindows(box, scale, dotBrush);
                break;
            case DesktopButtonStyle.Monitor:
                BuildMonitor(box, center, scale, dotBrush);
                break;
            case DesktopButtonStyle.Orb:
                BuildOrb(box, center, scale, color);
                break;
            default:
                BuildRing(box, center, scale, color, dotBrush);
                break;
        }
    }

    private byte Alpha(int alpha) => (byte)Math.Clamp(alpha * _brightness, 0, 255);

    private Ellipse AddDot(double cx, double cy, double size, Brush fill, double opacity)
    {
        var dot = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = fill,
            IsHitTestVisible = false,
            Opacity = opacity,
        };
        Canvas.SetLeft(dot, cx - (size / 2));
        Canvas.SetTop(dot, cy - (size / 2));
        _dots.Add(dot);
        Root.Children.Add(dot);
        return dot;
    }

    private void BuildRing(double box, double center, double scale, Color color, Brush dotBrush)
    {
        var group = new Canvas
        {
            Width = box,
            Height = box,
            IsHitTestVisible = false,
        };
        var spin = new RotateTransform(0, center, center);
        group.RenderTransform = spin;
        _comet = spin;

        var ringRadius = 28 * scale;
        var glowSize = (ringRadius * 2) + (8 * scale);
        var glowBrush = new SolidColorBrush(Color.FromArgb(Alpha(0x3A), color.R, color.G, color.B));
        glowBrush.Freeze();
        var glow = new Ellipse
        {
            Width = glowSize,
            Height = glowSize,
            IsHitTestVisible = false,
            Stroke = glowBrush,
            StrokeThickness = 5 * scale,
            Effect = new BlurEffect { Radius = 6 * scale, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance },
        };
        Canvas.SetLeft(glow, center - (glowSize / 2));
        Canvas.SetTop(glow, center - (glowSize / 2));
        group.Children.Add(glow);

        const int count = 16;
        var dotSize = 4.4 * scale;
        for (var i = 0; i < count; i++)
        {
            var angle = (-90 + (i * 360.0 / count)) * Math.PI / 180.0;
            var x = center + (ringRadius * Math.Cos(angle));
            var y = center + (ringRadius * Math.Sin(angle));
            var fade = i == 0 ? 1.0 : 0.6 - (0.4 * (i - 1) / (count - 2));
            var dot = new Ellipse
            {
                Width = dotSize,
                Height = dotSize,
                Fill = dotBrush,
                IsHitTestVisible = false,
                Opacity = Math.Min(1.0, fade * _brightness),
            };
            Canvas.SetLeft(dot, x - (dotSize / 2));
            Canvas.SetTop(dot, y - (dotSize / 2));
            group.Children.Add(dot);
            _dots.Add(dot);
        }

        Root.Children.Add(group);

        var coreSize = 10 * scale;
        var coreBrush = new SolidColorBrush(CoreRed);
        coreBrush.Freeze();
        _core = new Ellipse
        {
            Width = coreSize,
            Height = coreSize,
            Fill = coreBrush,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(_core, center - (coreSize / 2));
        Canvas.SetTop(_core, center - (coreSize / 2));
        Root.Children.Add(_core);
    }

    private void BuildWindows(double box, double scale, Brush dotBrush)
    {
        var spacing = 6 * scale;
        var dotSize = 3.2 * scale;
        var groupSize = (3 * spacing) + dotSize;
        var gap = 8 * scale;
        var total = (groupSize * 2) + gap;
        var start = (box - total) / 2;
        var dotAlpha = Math.Min(1.0, 0.92 * _brightness);

        for (var q = 0; q < 4; q++)
        {
            var square = new Canvas
            {
                Width = groupSize,
                Height = groupSize,
                IsHitTestVisible = false,
            };
            var move = new TranslateTransform();
            square.RenderTransform = move;
            _spreads.Add(move);

            for (var j = 0; j < 4; j++)
            {
                for (var i = 0; i < 4; i++)
                {
                    if (i != 0 && i != 3 && j != 0 && j != 3)
                    {
                        continue;
                    }

                    var dot = new Ellipse
                    {
                        Width = dotSize,
                        Height = dotSize,
                        Fill = dotBrush,
                        IsHitTestVisible = false,
                        Opacity = dotAlpha,
                    };
                    Canvas.SetLeft(dot, i * spacing);
                    Canvas.SetTop(dot, j * spacing);
                    square.Children.Add(dot);
                    _dots.Add(dot);
                }
            }

            Canvas.SetLeft(square, start + (q % 2 == 0 ? 0 : groupSize + gap));
            Canvas.SetTop(square, start + (q < 2 ? 0 : groupSize + gap));
            Root.Children.Add(square);
        }
    }

    private void BuildMonitor(double box, double center, double scale, Brush dotBrush)
    {
        const int cols = 10;
        const int rows = 7;
        var spacing = 5 * scale;
        var dotSize = 3.4 * scale;
        var screenWidth = (cols - 1) * spacing;
        var screenHeight = (rows - 1) * spacing;
        var standGap = 3 * spacing;
        var baseGap = 3 * spacing;
        var span = screenHeight + standGap + baseGap;
        var left = center - (screenWidth / 2);
        var top = center - (span / 2);
        var frameAlpha = Math.Min(1.0, 0.95 * _brightness);
        var standAlpha = Math.Min(1.0, 0.85 * _brightness);

        for (var j = 0; j < rows; j++)
        {
            for (var i = 0; i < cols; i++)
            {
                if (i != 0 && i != cols - 1 && j != 0 && j != rows - 1)
                {
                    continue;
                }

                AddDot(left + (i * spacing), top + (j * spacing), dotSize, dotBrush, frameAlpha);
            }
        }

        AddDot(center, top + screenHeight + standGap, dotSize, dotBrush, standAlpha);

        var baseY = top + screenHeight + standGap + baseGap;
        var baseStep = 4 * scale;
        for (var i = 0; i < 4; i++)
        {
            AddDot(center + ((i - 1.5) * baseStep), baseY, dotSize, dotBrush, standAlpha);
        }

        _zigAlpha = Math.Min(1.0, 0.6 * _brightness);
        var mountainBase = top + (rows - 2) * spacing;
        var dotSmall = 3.0 * scale;
        for (var c = 0; c < MountainHeights.Length; c++)
        {
            var height = MountainHeights[c];
            var x = left + ((c + 1) * spacing);
            for (var k = 0; k < height; k++)
            {
                var dot = AddDot(x, mountainBase - (k * spacing), dotSmall, dotBrush, _zigAlpha);
                _zigDots.Add(dot);
                _zigColumns.Add(c);
            }
        }
    }

    private void BuildOrb(double box, double center, double scale, Color color)
    {
        var halo = new Ellipse
        {
            Width = box,
            Height = box,
            IsHitTestVisible = false,
            Effect = new BlurEffect { Radius = 10 * scale, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance },
        };
        var haloBrush = new RadialGradientBrush { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5) };
        haloBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, color.R, color.G, color.B), 0.0));
        haloBrush.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(0x4E), color.R, color.G, color.B), 0.62));
        haloBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, color.R, color.G, color.B), 1.0));
        halo.Fill = haloBrush;
        Canvas.SetLeft(halo, 0);
        Canvas.SetTop(halo, 0);
        Root.Children.Add(halo);

        var ringSize = 50 * scale;
        var ringBrush = new SolidColorBrush(Color.FromArgb(Alpha(0x59), color.R, color.G, color.B));
        ringBrush.Freeze();
        var ring = new Ellipse
        {
            Width = ringSize,
            Height = ringSize,
            IsHitTestVisible = false,
            Stroke = ringBrush,
            StrokeThickness = 1.2 * scale,
        };
        Canvas.SetLeft(ring, (box - ringSize) / 2);
        Canvas.SetTop(ring, (box - ringSize) / 2);
        Root.Children.Add(ring);

        var coreSize = 26 * scale;
        _core = new Ellipse
        {
            Width = coreSize,
            Height = coreSize,
            IsHitTestVisible = false,
            Effect = new BlurEffect { Radius = 5 * scale, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance },
        };
        var coreBrush = new RadialGradientBrush { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5) };
        coreBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), 0.0));
        coreBrush.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(0xD8), 0xEC, 0xF7, 0xFF), 0.42));
        coreBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xEC, 0xF7, 0xFF), 1.0));
        _core.Fill = coreBrush;
        Canvas.SetLeft(_core, (box - coreSize) / 2);
        Canvas.SetTop(_core, (box - coreSize) / 2);
        Root.Children.Add(_core);
    }

    private void Animate(double dt)
    {
        _phase += dt;

        switch (DesktopButtonSettings.Style)
        {
            case DesktopButtonStyle.Windows:
            {
                _spread += (_hover - _spread) * Math.Min(1.0, dt * 12.0);
                var shift = _spread * 4 * _scale;
                for (var i = 0; i < _spreads.Count; i++)
                {
                    _spreads[i].X = i % 2 == 0 ? -shift : shift;
                    _spreads[i].Y = i < 2 ? -shift : shift;
                }

                break;
            }

            case DesktopButtonStyle.Monitor:
            {
                if (_zigDots.Count > 0)
                {
                    var speed = 2.2 + (_hover * 1.2);
                    for (var i = 0; i < _zigDots.Count; i++)
                    {
                        var wave = 0.5 + (0.5 * Math.Sin((_phase * speed) - (_zigColumns[i] * 0.7)));
                        var alpha = _zigAlpha * (0.55 + (0.45 * wave));
                        _zigDots[i].Opacity = alpha > 1.0 ? 1.0 : alpha;
                    }
                }

                break;
            }

            case DesktopButtonStyle.Orb:
            {
                if (_core is not null)
                {
                    var pulse = 0.72 + (0.28 * (0.5 + (0.5 * Math.Sin(_phase * 2.0))));
                    _core.Opacity = Math.Min(1.0, (pulse + (_hover * 0.2)) * Math.Min(1.0, _brightness));
                }

                break;
            }

            default:
            {
                if (_comet is not null)
                {
                    _comet.Angle = (_comet.Angle + ((120 + (100 * _hover)) * dt)) % 360;
                }

                if (_core is not null)
                {
                    var pulse = 0.74 + (0.26 * (0.5 + (0.5 * Math.Sin(_phase * 2.2))));
                    _core.Opacity = Math.Min(1.0, (pulse + (_hover * 0.12)) * Math.Min(1.0, _brightness));
                }

                break;
            }
        }
    }
}
