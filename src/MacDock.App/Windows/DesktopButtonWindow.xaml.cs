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
using MacDock.Core.Interop;

namespace MacDock.App.Windows;

public partial class DesktopButtonWindow : Window
{
    private const double Box = 72;
    private const double Center = Box / 2;

    public event Action? Clicked;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly List<RotateTransform> _spins = new();
    private readonly Ellipse _core = new() { IsHitTestVisible = false };
    private readonly Ellipse _rim = new() { IsHitTestVisible = false };
    private double _phase;
    private double _hover;

    public DesktopButtonWindow()
    {
        InitializeComponent();

        var ghost = Color.FromRgb(0xCF, 0xE8, 0xFF);

        var halo = new Ellipse
        {
            Width = Box,
            Height = Box,
            IsHitTestVisible = false,
            Effect = new BlurEffect { Radius = 10, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance },
        };
        var haloBrush = new RadialGradientBrush { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5) };
        haloBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, ghost.R, ghost.G, ghost.B), 0.0));
        haloBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x4E, ghost.R, ghost.G, ghost.B), 0.62));
        haloBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, ghost.R, ghost.G, ghost.B), 1.0));
        halo.Fill = haloBrush;
        Canvas.SetLeft(halo, 0);
        Canvas.SetTop(halo, 0);
        Root.Children.Add(halo);

        for (var i = 0; i < 2; i++)
        {
            var spin = new RotateTransform(i * 180.0, 13, 24);
            var wisp = new Ellipse
            {
                Width = 26,
                Height = 6,
                IsHitTestVisible = false,
                Fill = new SolidColorBrush(Color.FromArgb(0x55, ghost.R, ghost.G, ghost.B)),
                Effect = new BlurEffect { Radius = 5, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance },
                RenderTransform = spin,
            };
            Canvas.SetLeft(wisp, Center - 13);
            Canvas.SetTop(wisp, Center - 24);
            _spins.Add(spin);
            Root.Children.Add(wisp);
        }

        _rim.Width = 50;
        _rim.Height = 50;
        _rim.Stroke = new SolidColorBrush(Color.FromArgb(0xC0, ghost.R, ghost.G, ghost.B));
        _rim.StrokeThickness = 1.5;
        _rim.StrokeDashArray = new DoubleCollection { 0.8, 1.8 };
        _rim.Effect = new BlurEffect { Radius = 2.5, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
        Canvas.SetLeft(_rim, (Box - 50) / 2);
        Canvas.SetTop(_rim, (Box - 50) / 2);
        Root.Children.Add(_rim);

        _core.Width = 26;
        _core.Height = 26;
        var coreBrush = new RadialGradientBrush { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5) };
        coreBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), 0.0));
        coreBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0xD8, 0xEC, 0xF7, 0xFF), 0.42));
        coreBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xEC, 0xF7, 0xFF), 1.0));
        _core.Fill = coreBrush;
        _core.Effect = new BlurEffect { Radius = 5, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
        Canvas.SetLeft(_core, (Box - 26) / 2);
        Canvas.SetTop(_core, (Box - 26) / 2);
        Root.Children.Add(_core);

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
