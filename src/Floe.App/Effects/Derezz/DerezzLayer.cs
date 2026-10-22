using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Floe.App.Effects.Derezz;

/// <summary>
/// Hosts running derezz fields over the dock: a sharp layer and, on top, the lit edges blurred into bloom.
/// </summary>
internal sealed class DerezzLayer : FrameworkElement
{
    private readonly DrawingVisual _sharp = new();
    private readonly DrawingVisual _glow = new()
    {
        Effect = new BlurEffect { Radius = 5, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance },
    };

    private bool _empty = true;

    public DerezzLayer()
    {
        IsHitTestVisible = false;
        AddVisualChild(_sharp);
        AddVisualChild(_glow);
    }

    protected override int VisualChildrenCount => 2;

    protected override Visual GetVisualChild(int index) => index == 0 ? _sharp : _glow;

    public void Draw(IEnumerable<(DerezzField Field, double Time)> runs)
    {
        using (var dc = _sharp.RenderOpen())
        using (var glow = _glow.RenderOpen())
        {
            foreach (var (field, time) in runs)
            {
                field.Render(dc, glow, time);
            }
        }

        _empty = false;
    }

    public void Clear()
    {
        if (_empty) return;
        using (_sharp.RenderOpen()) { }
        using (_glow.RenderOpen()) { }
        _empty = true;
    }
}
