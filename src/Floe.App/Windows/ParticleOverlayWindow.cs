using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Floe.App.Effects.Derezz;
using Floe.Core.Interop;

namespace Floe.App.Windows;

/// <summary>
/// Click-through overlay the particle dissolve flows in: it starts where the dock window does and runs on to the
/// monitor's right edge, so the river is not cut off by a wall where the dock window ends.
/// </summary>
internal sealed class ParticleOverlayWindow : Window
{
    private IntPtr _hwnd;

    public ParticleOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        Title = "Floe Particles";
        Layer = new ParticleLayer();
        RenderOptions.SetBitmapScalingMode(Layer, BitmapScalingMode.NearestNeighbor);
        Content = Layer;
    }

    public ParticleLayer Layer { get; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        WindowStyling.MakeClickThrough(_hwnd);
    }

    /// <summary>Covers the dock window's band from its left edge to <paramref name="right"/> (DIP) and shows.</summary>
    public void Cover(double left, double top, double right, double height)
    {
        Left = left;
        Top = top;
        Width = Math.Max(1, right - left);
        Height = Math.Max(1, height);
        if (!IsVisible) Show();
        WindowStyling.BringToTop(_hwnd);
        UpdateLayout();
    }
}
