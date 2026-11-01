using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Floe.App.Windows;

/// <summary>
/// The barcode beside the clock, bars alone on the dock. A red line scans down it, each bar reaches left as the line crosses it, and now
/// and then the whole code jolts sideways.
/// </summary>
public sealed class BarcodeTag : Canvas
{
    private const double TagWidth = 39, TagHeight = 72;
    private const double BarLeft = 18.5, BarRight = 33, ScanTop = 10, ScanTravel = 46;
    private const double SweepSeconds = 3.6, CrossSeconds = 2.4, GlitchSeconds = 7.2;
    private const double FrameRate = 20;

    private static readonly (double Top, double Height)[] Bars = { (11, 4), (18, 1), (21, 3), (29, 1), (32, 5), (39, 3), (45, 1), (51, 4), (57, 1) };
    private static readonly Color Scan = Color.FromRgb(0xFF, 0x00, 0x3C);

    private readonly SolidColorBrush _ink = new();
    private readonly double _unit;

    public BarcodeTag(double height)
    {
        _unit = height / TagHeight;
        Width = TagWidth * _unit;
        Height = height;
        IsHitTestVisible = false;
        ClipToBounds = true;

        var jolt = new TranslateTransform();
        var code = new Canvas { RenderTransform = jolt };
        Children.Add(code);
        var line = new TranslateTransform();
        var scan = new Rectangle { Width = 31 * _unit, Height = Math.Max(1, _unit), Fill = new SolidColorBrush(Scan), Opacity = 0, RenderTransform = line };
        SetLeft(scan, 4 * _unit);
        SetTop(scan, ScanTop * _unit);
        Children.Add(scan);

        bool moving = SystemParameters.ClientAreaAnimation;
        foreach (var (top, tall) in Bars)
        {
            var stretch = new ScaleTransform(1, 1, (BarRight - BarLeft) * _unit, 0);
            var bar = new Rectangle { Width = (BarRight - BarLeft) * _unit, Height = tall * _unit, Fill = _ink, RenderTransform = stretch };
            SetLeft(bar, BarLeft * _unit);
            SetTop(bar, top * _unit);
            code.Children.Add(bar);
            if (moving) stretch.BeginAnimation(ScaleTransform.ScaleXProperty, Stretch((top - Bars[0].Top) / ScanTravel * CrossSeconds));
        }

        if (!moving) return;
        line.BeginAnimation(TranslateTransform.YProperty, Sweep());
        scan.BeginAnimation(OpacityProperty, ScanFade());
        jolt.BeginAnimation(TranslateTransform.XProperty, Jolt());
    }

    public void Paint(Color ink) => _ink.Color = ink;

    private static DoubleAnimationUsingKeyFrames Loop(double seconds, double delay = 0)
    {
        var loop = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(seconds), BeginTime = TimeSpan.FromSeconds(delay), RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(loop, (int)FrameRate);
        return loop;
    }

    private static KeyTime At(double seconds, double share) => KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds * share));

    private DoubleAnimationUsingKeyFrames Sweep()
    {
        var sweep = Loop(SweepSeconds);
        sweep.KeyFrames.Add(new LinearDoubleKeyFrame(0, At(SweepSeconds, 0)));
        sweep.KeyFrames.Add(new LinearDoubleKeyFrame(ScanTravel * _unit, At(SweepSeconds, CrossSeconds / SweepSeconds)));
        sweep.KeyFrames.Add(new LinearDoubleKeyFrame(ScanTravel * _unit, At(SweepSeconds, 1)));
        return sweep;
    }

    private static DoubleAnimationUsingKeyFrames ScanFade()
    {
        var fade = Loop(SweepSeconds);
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, At(SweepSeconds, 0)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(.95, At(SweepSeconds, .04)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(.95, At(SweepSeconds, .66)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, At(SweepSeconds, .70)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, At(SweepSeconds, 1)));
        return fade;
    }

    // the bar stretches from its right edge as the line reaches it
    private static DoubleAnimationUsingKeyFrames Stretch(double delay)
    {
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var stretch = Loop(SweepSeconds, delay);
        stretch.KeyFrames.Add(new LinearDoubleKeyFrame(1, At(SweepSeconds, .02)));
        stretch.KeyFrames.Add(new EasingDoubleKeyFrame(1.5, At(SweepSeconds, .07), ease));
        stretch.KeyFrames.Add(new EasingDoubleKeyFrame(1, At(SweepSeconds, .22), ease));
        stretch.KeyFrames.Add(new LinearDoubleKeyFrame(1, At(SweepSeconds, 1)));
        return stretch;
    }

    private DoubleAnimationUsingKeyFrames Jolt()
    {
        var jolt = Loop(GlitchSeconds);
        jolt.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, At(GlitchSeconds, 0)));
        jolt.KeyFrames.Add(new DiscreteDoubleKeyFrame(-1.6 * _unit, At(GlitchSeconds, .95)));
        jolt.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.2 * _unit, At(GlitchSeconds, .96)));
        jolt.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, At(GlitchSeconds, .97)));
        return jolt;
    }
}
