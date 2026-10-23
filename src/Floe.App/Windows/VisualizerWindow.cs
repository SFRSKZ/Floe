using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Floe.App.Effects.Gpu;
using Floe.App.Effects.Visualizer;
using Floe.App.Services;
using Floe.Core.Interop;

namespace Floe.App.Windows;

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
    private readonly CubeVisualizer _cubes = new();
    private readonly StardustVisualizer _stardust = new();
    private readonly StardustVisualizer _dustWave = new() { Wave = true };
    private readonly SilkContourVisualizer _silkContour = new();
    private GpuLayerWindow? _gpu;
    private GpuLayerWindow? _flame;
    private GpuLayerWindow? _oled;
    private FluidFireGpuScene? _fire;
    private Rect _dockRect;
    private bool _gpuFailed;
    private IntPtr _hwnd;
    private IntPtr _dock;
    private Rect _bounds = Rect.Empty;
    private double _scale = 1;
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
        Title = "Floe Visualizer";

        var root = new Grid { IsHitTestVisible = false };
        root.Children.Add(_glow);
        root.Children.Add(_hologram);
        root.Children.Add(_cubes);
        root.Children.Add(_stardust);
        root.Children.Add(_dustWave);
        root.Children.Add(_silkContour);
        Content = root;
        SizeChanged += (_, _) => UpdateCapsule();
    }

    public bool IsRunning => _glow.IsActive || _hologram.IsActive || _cubes.IsActive || _stardust.IsActive || _dustWave.IsActive || _silkContour.IsActive;

    public IntPtr Handle => _hwnd;

    /// <summary>The window the light is drawn in right now: the GPU layer when it carries the look.</summary>
    public IntPtr TopHandle => _oled is { IsRunning: true } oled ? oled.Handle : _flame is { IsRunning: true } flame ? flame.Handle : _gpu is { IsRunning: true } gpu ? gpu.Handle : _hwnd;

    /// <summary>The OLED panel runs under the dock: its glass has to keep up with what moves behind it.</summary>
    public bool PanelBehindDock => _oled is { IsRunning: true };

    public bool Showing => IsVisible || _gpu is { IsRunning: true } || _flame is { IsRunning: true } || _oled is { IsRunning: true };

    protected override void OnClosed(EventArgs e)
    {
        _hideTimer?.Stop();
        CaptureReveal.Forget(_hwnd);
        _gpu?.Dispose();
        _gpu = null;
        _flame?.Dispose();
        _flame = null;
        _oled?.Dispose();
        _oled = null;
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

    /// <summary>dockRect: the dock's capsule on screen, in DIPs.</summary>
    public void Place(Rect bounds, double scale, IntPtr dock, Rect dockRect)
    {
        _scale = scale;
        _dockRect = dockRect;
        PlaceFire(bounds);
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
        PlaceGpu();
    }

    public void PlaceBelowDock(IntPtr dock)
    {
        _dock = dock;
        WindowStyling.PlaceBelow(_hwnd, dock);
        _gpu?.PlaceBelowDock(dock);
        _flame?.PlaceBelowDock(dock);
        _oled?.PlaceBelowDock(dock);
    }

    public void SetRunning(bool running)
    {
        var look = GlassSettings.VisualizerLook;
        // the eye flame only exists on the GPU; the dust looks move there when the setting asks for it
        bool flame = running && look == VisualizerStyle.EyeFlame && Layer(ref _flame, () => _fire = new FluidFireGpuScene());
        PlaceFire(_bounds);
        bool oled = running && look == VisualizerStyle.Oled && Layer(ref _oled, () => new OledGpuScene(), capturable: true);
        bool dust = running && GlassSettings.GpuVisualizer && look is VisualizerStyle.Stardust or VisualizerStyle.DustWave
            && Layer(ref _gpu, () => new StardustGpuScene());
        if (_gpu is not null)
        {
            _gpu.Wave = look == VisualizerStyle.DustWave;
            _gpu.SetRunning(dust);
        }

        _flame?.SetRunning(flame);
        _oled?.SetRunning(oled);

        // without a GPU the eye flame and the OLED panel fall back to stardust
        if ((look == VisualizerStyle.EyeFlame && !flame) || (look == VisualizerStyle.Oled && !oled)) look = VisualizerStyle.Stardust;

        // a GPU layer carries its look by itself; this window only fades out what it drew before
        running &= !(dust || flame || oled);
        _running = running;
        _glow.IsActive = running && look == VisualizerStyle.Glow;
        _hologram.IsActive = running && look == VisualizerStyle.Hologram;
        _cubes.IsActive = running && look == VisualizerStyle.Cubes;
        _stardust.IsActive = running && look == VisualizerStyle.Stardust;
        _dustWave.IsActive = running && look == VisualizerStyle.DustWave;
        _silkContour.IsActive = running && look == VisualizerStyle.SilkContour;

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
                    Place(_bounds, _scale, _dock, _dockRect);
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

    private void PlaceFire(Rect bounds)
    {
        if (_fire is null || bounds.Width <= 0 || bounds.Height <= 0 || _dockRect.IsEmpty) return;
        _fire.SetDock(
            (float)((_dockRect.Left - bounds.Left) / bounds.Width),
            (float)((_dockRect.Right - bounds.Left) / bounds.Width),
            (float)((bounds.Bottom - _dockRect.Bottom) / bounds.Height),
            (float)((bounds.Bottom - _dockRect.Top) / bounds.Height));
    }

    private bool Layer(ref GpuLayerWindow? layer, Func<IGpuScene> scene, bool capturable = false)
    {
        if (_gpuFailed) return false;
        if (layer is null)
        {
            try
            {
                layer = new GpuLayerWindow(scene(), capturable);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Floe.Gpu] layer: {ex.Message}");
            }

            // no window means no device or no composition: stay on the WPF path for good
            if (layer is null || layer.Handle == IntPtr.Zero)
            {
                layer?.Dispose();
                layer = null;
                _gpuFailed = true;
                return false;
            }

            PlaceGpu();
        }

        return true;
    }

    private void PlaceGpu()
    {
        if (_bounds.IsEmpty) return;
        // the dock hands over DIP bounds; the composition layers work in physical pixels
        var pixels = new Int32Rect(
            (int)Math.Round(_bounds.Left * _scale),
            (int)Math.Round(_bounds.Top * _scale),
            (int)Math.Round(_bounds.Width * _scale),
            (int)Math.Round(_bounds.Height * _scale));
        _gpu?.Place(pixels, _scale, _dock);
        _flame?.Place(pixels, _scale, _dock);
        _oled?.Place(pixels, _scale, _dock);
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
        // the cubes stand on the screen's bottom edge
        _cubes.Capsule = strip;
        _stardust.Capsule = strip;
        _dustWave.Capsule = strip;
        _silkContour.Capsule = strip;
    }
}
