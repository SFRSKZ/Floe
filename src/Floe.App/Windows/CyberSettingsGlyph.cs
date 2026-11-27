using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Floe.App.Windows;

/// <summary>
/// Animated Cyberpunk quick-settings glyph: three adjustment rails whose calibrated knobs glide cleanly across
/// their tracks on hover and return on leave.
/// </summary>
public sealed class CyberSettingsGlyph : Canvas
{
    private const double View = 24;
    private const double KnobRadius = 2.1;

    private static readonly (double Y, double RestX, double HoverX)[] RailSpecs =
    {
        (6, 14, 7),
        (12, 8, 16),
        (18, 17, 9),
    };

    private readonly SolidColorBrush _inkBrush = new(Colors.Black);
    private readonly SolidColorBrush _accentBrush = new(Colors.Red);
    private readonly TranslateTransform[] _knobShifts = new TranslateTransform[3];
    private readonly Ellipse[] _knobs = new Ellipse[3];
    private readonly ScaleTransform _scale = new(1, 1);
    private Color _ink, _accent;
    private bool _engaged;

    public CyberSettingsGlyph(double size)
    {
        IsHitTestVisible = false;

        var root = new Canvas { RenderTransform = _scale };
        for (int i = 0; i < RailSpecs.Length; i++)
        {
            var (y, restX, _) = RailSpecs[i];
            root.Children.Add(new Line
            {
                X1 = 4,
                Y1 = y,
                X2 = 20,
                Y2 = y,
                Stroke = _inkBrush,
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            });

            var shift = new TranslateTransform(0, 0);
            _knobShifts[i] = shift;
            var knob = new Ellipse
            {
                Width = KnobRadius * 2,
                Height = KnobRadius * 2,
                Stroke = _inkBrush,
                StrokeThickness = 1.8,
                Fill = _inkBrush,
                RenderTransform = shift,
            };
            SetLeft(knob, restX - KnobRadius);
            SetTop(knob, y - KnobRadius);
            _knobs[i] = knob;
            root.Children.Add(knob);
        }

        Children.Add(root);
        SetSize(size);
    }

    /// <summary>Resizes the glyph (dock or taskbar); the rails keep their proportions.</summary>
    public void SetSize(double size)
    {
        Width = Height = size;
        _scale.ScaleX = _scale.ScaleY = size / View;
    }

    /// <summary>Updates ink and accent colors for the rails and sliding knobs.</summary>
    public void Paint(Color ink, Color accent)
    {
        _ink = ink;
        _accent = accent;
        _inkBrush.Color = ink;
        _accentBrush.Color = accent;
        foreach (var knob in _knobs)
        {
            knob.Stroke = _inkBrush;
            knob.Fill = _engaged ? _accentBrush : _inkBrush;
        }
    }

    /// <summary>Glides all three knobs cleanly to their active calibration positions on hover.</summary>
    public void Engage(bool on)
    {
        if (_engaged == on) return;
        _engaged = on;

        for (int i = 0; i < _knobShifts.Length; i++)
        {
            var (_, restX, hoverX) = RailSpecs[i];
            var anim = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(i * 25) };
            anim.KeyFrames.Add(new SplineDoubleKeyFrame(on ? (hoverX - restX) : 0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)), new KeySpline(.25, .8, .25, 1)));
            _knobShifts[i].BeginAnimation(TranslateTransform.XProperty, anim);
            _knobs[i].Fill = on ? _accentBrush : _inkBrush;
        }
    }
}
