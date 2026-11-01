using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Floe.App.Effects.Cyber;
using Floe.App.Services;
using Floe.Core.Interop;

namespace Floe.App.Windows;

/// <summary>Show-desktop button at the dock's leading edge, where Windows keeps its Start button.</summary>
public partial class DockWindow
{
    private const double StartWidth = 40;
    private const double StartGap = 14;
    private const double StartGlyph = 28;

    // icons begin after the start button and its divider
    private const double LeadX = PadX + StartWidth + StartGap;

    private readonly Grid _start = new() { Width = StartWidth, Height = StartWidth, Background = Brushes.Transparent, Cursor = Cursors.Hand };
    private readonly Image _startGlyph = new() { Width = StartGlyph, Height = StartGlyph, IsHitTestVisible = false };
    private readonly Rectangle _startDivider = new() { Width = 1, Height = 34, RadiusX = 0.5, RadiusY = 0.5, IsHitTestVisible = false };
    private readonly ScaleTransform _startScale = new(1, 1);

    // the Cyber button: three windows that gather into one, with the desktop's dot blinking on it
    private readonly Viewbox _startPanes = new() { Width = StartGlyph, Height = StartGlyph, IsHitTestVisible = false };
    private readonly Path _paneBack = StartPane(), _paneMiddle = StartPane(), _paneFront = StartPane();
    private readonly Rectangle _startDot = new() { Width = 7, Height = 7, Opacity = 0 };
    private readonly TranslateTransform _paneBackShift = new(), _paneFrontShift = new();

    private void InitializeStartButton()
    {
        _start.ToolTip = Loc.T("Masaüstünü göster");
        _start.RenderTransformOrigin = new Point(0.5, 0.5);
        _start.RenderTransform = _startScale;
        _start.Children.Add(_startGlyph);
        _start.Children.Add(BuildStartPanes());
        _start.MouseLeftButtonUp += (_, _) =>
        {
            PlayStartPanes();
            DesktopController.ShowDesktop();
        };
        _start.MouseEnter += (_, _) =>
        {
            AnimateStart(1.12, 1);
            PlayStartPanes();
        };
        _start.MouseLeave += (_, _) => AnimateStart(1, 0.82);
        _startGlyph.Opacity = 0.82;
        RenderOptions.SetBitmapScalingMode(_startGlyph, BitmapScalingMode.HighQuality);

        _startDivider.Fill = _lineBrush;
        _clockLayer.Children.Add(_start);
        _clockLayer.Children.Add(_startDivider);

        UpdateStartGlyph();
        DesktopButtonSettings.Changed += OnStartSettingsChanged;
        DpiChanged += (_, _) => UpdateStartGlyph();
        Closed += (_, _) => DesktopButtonSettings.Changed -= OnStartSettingsChanged;
    }

    private void OnStartSettingsChanged() => Dispatcher.BeginInvoke(UpdateStartGlyph);

    private void UpdateStartGlyph()
    {
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        bool cyber = GlassSettings.Cyber;
        _startPanes.Visibility = cyber ? Visibility.Visible : Visibility.Collapsed;
        _startGlyph.Visibility = cyber ? Visibility.Collapsed : Visibility.Visible;
        if (cyber)
        {
            var ink = CyberFxLayer.Solid(StartGlyphColor);
            var plate = CyberFxLayer.Solid(CyberLook.Current.Body);
            foreach (var pane in new[] { _paneBack, _paneMiddle, _paneFront })
            {
                pane.Stroke = ink;
                pane.Fill = plate;
            }

            _startDot.Fill = ink;
            return;
        }

        _startGlyph.Source = DotMatrixIcons.Render("desktop", StartGlyph, StartGlyphColor, dpi);
        _startGlyph.Opacity = 0.82;
    }

    private static Path StartPane() => new()
    {
        Data = Geometry.Parse("M0,0 H30 L36,6 V26 H0 Z M0,7 H36"),
        StrokeThickness = 3.5,
        StrokeLineJoin = PenLineJoin.Miter,
    };

    private Viewbox BuildStartPanes()
    {
        var canvas = new Canvas { Width = 64, Height = 64 };
        Canvas.SetLeft(_paneBack, 6);
        Canvas.SetTop(_paneBack, 8);
        Canvas.SetLeft(_paneMiddle, 14);
        Canvas.SetTop(_paneMiddle, 18);
        Canvas.SetLeft(_paneFront, 22);
        Canvas.SetTop(_paneFront, 28);
        Canvas.SetLeft(_startDot, 28.5);
        Canvas.SetTop(_startDot, 31);
        _paneBack.RenderTransform = _paneBackShift;
        _paneFront.RenderTransform = _paneFrontShift;
        canvas.Children.Add(_paneBack);
        canvas.Children.Add(_paneMiddle);
        canvas.Children.Add(_paneFront);
        canvas.Children.Add(_startDot);
        _startPanes.Child = canvas;
        return _startPanes;
    }

    // the outer windows slide onto the middle one, the dot blinks while they lie together, and they part again;
    // nothing runs between plays, so the button costs no frames while it rests
    private void PlayStartPanes()
    {
        if (!GlassSettings.Cyber) return;
        Gather(_paneBackShift, TranslateTransform.XProperty, 8);
        Gather(_paneBackShift, TranslateTransform.YProperty, 10);
        Gather(_paneFrontShift, TranslateTransform.XProperty, -8);
        Gather(_paneFrontShift, TranslateTransform.YProperty, -10);

        var blink = new DoubleAnimationUsingKeyFrames();
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320))));
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(760))));
        _startDot.BeginAnimation(OpacityProperty, blink);
    }

    private static void Gather(TranslateTransform shift, DependencyProperty axis, double by)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var move = new DoubleAnimationUsingKeyFrames();
        move.KeyFrames.Add(new EasingDoubleKeyFrame(by, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(280)), ease));
        move.KeyFrames.Add(new LinearDoubleKeyFrame(by, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(760))));
        move.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1040)), ease));
        shift.BeginAnimation(axis, move);
    }

    private void AnimateStart(double scale, double opacity)
    {
        var duration = TimeSpan.FromMilliseconds(160);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        _startScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, duration) { EasingFunction = ease });
        _startScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, duration) { EasingFunction = ease });
        _startGlyph.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, duration) { EasingFunction = ease });
    }

    private void PositionStartButton()
    {
        var left = _capsuleLeft + PadX;
        Canvas.SetLeft(_start, left);
        Canvas.SetTop(_start, RestTop + ((RestBarHeight - StartWidth) / 2));
        Canvas.SetLeft(_startDivider, left + StartWidth + (StartGap / 2));
        Canvas.SetTop(_startDivider, RestTop + ((RestBarHeight - _startDivider.Height) / 2));
    }
}
