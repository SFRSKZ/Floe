using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using MacDock.App.Effects.Visualizer;
using MacDock.App.Services;
using MacDock.Core.Interop;

namespace MacDock.App.Windows;

/// <summary>
/// Click-through strip along the bottom of the screen that carries the music visualizer.
/// It lives in its own window so its repaints never re-run the dock's glass shaders.
/// </summary>
internal sealed class VisualizerWindow : Window
{
    private readonly VisualizerGlow _glow = new();
    private readonly HologramVisualizer _hologram = new();
    private IntPtr _hwnd;

    public VisualizerWindow()
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
        Title = "MacDock Visualizer";

        var root = new Grid { IsHitTestVisible = false };
        root.Children.Add(_glow);
        root.Children.Add(_hologram);
        Content = root;
        SizeChanged += (_, _) => UpdateCapsule();
    }

    public bool IsRunning => _glow.IsActive || _hologram.IsActive;

    protected override void OnClosed(EventArgs e)
    {
        CaptureReveal.Forget(_hwnd);
        base.OnClosed(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        WindowStyling.MakeClickThrough(_hwnd);

        // keep the dock's backdrop capture from picking the light up
        CaptureReveal.Exclude(_hwnd);
    }

    public void Place(Rect bounds, IntPtr dock)
    {
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        WindowStyling.PlaceBelow(_hwnd, dock);
    }

    public void SetRunning(bool running)
    {
        var hologram = GlassSettings.VisualizerLook == VisualizerStyle.Hologram;
        _glow.IsActive = running && !hologram;
        _hologram.IsActive = running && hologram;
    }

    private void UpdateCapsule()
    {
        // The whole strip is the "capsule": the light rises from the screen's bottom edge.
        var strip = new Rect(0, 0, Math.Max(1, ActualWidth), Math.Max(1, ActualHeight));
        _glow.Capsule = strip with { Y = strip.Height - 2, Height = 2 };
        _hologram.Capsule = strip;
    }
}
