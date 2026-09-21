using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using MacDock.App.Services;
using MacDock.Core.Interop;

namespace MacDock.App.Windows;

public partial class DesktopButtonWindow : Window
{
    private const double BaseBox = 72;

    public event Action? Clicked;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly List<RotateTransform> _spins = new();
    private Ellipse _core = new() { IsHitTestVisible = false };
    private Ellipse _rim = new() { IsHitTestVisible = false };
    private double _phase;
    private double _hover;

    public DesktopButtonWindow()
    {
        InitializeComponent();
        DesktopButtonSettings.Load();
        DesktopButtonSettings.Changed += Build;
        Build();

        _timer.Tick += (_, _) => Animate();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _timer.Start();
            }
            else
            {
                _timer.Stop();
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
        DesktopButtonSettings.Changed -= Build;
        base.OnClosed(e);
    }

    private void Build()
    {
        Root.Children.Clear();
        _spins.Clear();

        var scale = DesktopButtonSettings.Size;
        var box = BaseBox * scale;
        var center = box / 2;
        var color = DesktopButtonSettings.Color;
        var brightness = DesktopButtonSettings.Brightness;

        Width = box;
        Height = box;

        byte Alpha(int alpha) => (byte)Math.Clamp(alpha * brightness, 0, 255);

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

        var wispWidth = 26 * scale;
        var wispHeight = 6 * scale;
        var orbit = 24 * scale;
        for (var i = 0; i < 2; i++)
        {
            var spin = new RotateTransform(i * 180.0, wispWidth / 2, orbit);
            var wisp = new Ellipse
            {
                Width = wispWidth,
                Height = wispHeight,
                IsHitTestVisible = false,
                Fill = new SolidColorBrush(Color.FromArgb(Alpha(0x55), color.R, color.G, color.B)),
                Effect = new BlurEffect { Radius = 5 * scale, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance },
                RenderTransform = spin,
            };
            Canvas.SetLeft(wisp, center - (wispWidth / 2));
            Canvas.SetTop(wisp, center - orbit);
            _spins.Add(spin);
            Root.Children.Add(wisp);
        }

        var rimSize = 50 * scale;
        _rim = new Ellipse
        {
            Width = rimSize,
            Height = rimSize,
            IsHitTestVisible = false,
            Stroke = new SolidColorBrush(Color.FromArgb(Alpha(0xC0), color.R, color.G, color.B)),
            StrokeThickness = 1.5 * scale,
            StrokeDashArray = new DoubleCollection { 0.8, 1.8 },
            Effect = new BlurEffect { Radius = 2.5 * scale, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance },
        };
        Canvas.SetLeft(_rim, (box - rimSize) / 2);
        Canvas.SetTop(_rim, (box - rimSize) / 2);
        Root.Children.Add(_rim);

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

    private void Animate()
    {
        _phase += 0.09;
        var pulse = 0.72 + (0.28 * (0.5 + (0.5 * Math.Sin(_phase * 2.0))));
        _core.Opacity = Math.Min(1.0, pulse + (_hover * 0.2));
        _rim.StrokeDashOffset = (_phase * 5) % 12;
        _rim.Opacity = 0.85 + (_hover * 0.15);
        foreach (var spin in _spins)
        {
            spin.Angle += 1.4 + (_hover * 2.0);
        }
    }
}
