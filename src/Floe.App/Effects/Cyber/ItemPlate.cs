using System.Windows;
using System.Windows.Media;
using Floe.App.Services;
using Floe.App.Windows;

namespace Floe.App.Effects.Cyber;

/// <summary>The Cyber theme's panel: <see cref="CyberFxLayer.ItemFrame"/> in the theme's colours, sized by layout.</summary>
public sealed class ItemPlate : FrameworkElement
{
    private bool _hot;

    public ItemPlate()
    {
        IsHitTestVisible = false;
    }

    public double Bar { get; init; } = 7;

    public bool Hot
    {
        set
        {
            _hot = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth <= 2 || ActualHeight <= 2) return;
        var box = new Rect(1, 1, ActualWidth - 2, ActualHeight - 2);
        CyberFxLayer.ItemFrame(dc, box, CyberLook.Current.Accent, DockWindow.FxCyan, CyberLook.PanelInk, _hot, Bar);
    }
}
