using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
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
    // Both visualizers fade out over 400 ms; hide once that has finished.
    private const double HideDelaySeconds = 0.45;

    private readonly VisualizerGlow _glow = new();
    private readonly HologramVisualizer _hologram = new();
    private readonly MistVisualizer _mist = new();
    private IntPtr _hwnd;
    private IntPtr _dock;
    private Rect _bounds = Rect.Empty;
    private DispatcherTimer? _hideTimer;
    private bool _placed;
    private bool _running;

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
        root.Children.Add(_mist);
        Content = root;
        SizeChanged += (_, _) => UpdateCapsule();
    }

    public bool IsRunning => _glow.IsActive || _hologram.IsActive || _mist.IsActive;

    public IntPtr Handle => _hwnd;

    protected override void OnClosed(EventArgs e)
    {
        _hideTimer?.Stop();
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
        // The dock re-places the strip on every poll; touching the window (and
        // the z-order) only when something changed keeps that poll cheap.
        if (!_placed || bounds != _bounds)
        {
            _bounds = bounds;
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
        }

        if (!_placed || dock != _dock)
        {
            _dock = dock;
            WindowStyling.PlaceBelow(_hwnd, dock);
        }

        _placed = true;
    }

    public void PlaceBelowDock(IntPtr dock)
    {
        _dock = dock;
        WindowStyling.PlaceBelow(_hwnd, dock);
    }

    public void SetRunning(bool running)
    {
        _running = running;
        var look = GlassSettings.VisualizerLook;
        _glow.IsActive = running && look == VisualizerStyle.Glow;
        _hologram.IsActive = running && look == VisualizerStyle.Hologram;
        _mist.IsActive = running && look == VisualizerStyle.Mist;

        if (running)
        {
            _hideTimer?.Stop();
            if (!IsVisible)
            {
                Show();
                if (!_bounds.IsEmpty)
                {
                    // Show() restores the window's own placement; re-apply the strip at once.
                    _placed = false;
                    Place(_bounds, _dock);
                }
            }
        }
        else if (IsVisible && _hideTimer is not { IsEnabled: true })
        {
            // The fade-out still has to paint; hide the layered window after it.
            _hideTimer ??= CreateHideTimer();
            _hideTimer.Start();
        }
    }

    private DispatcherTimer CreateHideTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromSeconds(HideDelaySeconds)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!_running)
            {
                Hide();
            }
        };
        return timer;
    }

    private void UpdateCapsule()
    {
        // The whole strip is the "capsule": the light rises from the screen's bottom edge.
        var strip = new Rect(0, 0, Math.Max(1, ActualWidth), Math.Max(1, ActualHeight));
        _glow.Capsule = strip with { Y = strip.Height - 2, Height = 2 };
        // the hologram keeps its base line 4 DIP above the capsule's bottom; push that onto the screen edge
        _hologram.Capsule = strip with { Height = strip.Height + 4 };
        _mist.Capsule = strip;
    }
}
