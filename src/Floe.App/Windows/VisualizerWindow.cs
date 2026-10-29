using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private readonly SculptedVisualizer _sculpted = new();
    private readonly Grid _root;
    private GpuLayerWindow? _gpu;
    private StardustGpuScene? _dust;
    private GpuLayerWindow? _holo;
    private GpuLayerWindow? _cubeLayer;
    // the catalog's looks share one layer, rebuilt when the look changes
    private GpuLayerWindow? _novelLayer;
    private VisualizerStyle _novelLook;
    private GpuLayerWindow? _oled;
    private OledGpuScene? _panel;
    private CubeGpuScene? _cubeScene;
    private ImageSource? _coverSource;
    private byte[]? _cover;
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
        root.Children.Add(_sculpted);
        // the LED screen over the WPF looks needs the strip's size in pixels
        root.SizeChanged += (_, _) => ApplyLedScreen();
        _root = root;
        Content = root;
        SizeChanged += (_, _) => UpdateCapsule();
        GlassSettings.Changed += OnGlassSettingsChanged;
    }

    public bool IsRunning => _glow.IsActive || _hologram.IsActive || _cubes.IsActive || _stardust.IsActive || _dustWave.IsActive || _silkContour.IsActive || _sculpted.IsActive;

    public IntPtr Handle => _hwnd;

    /// <summary>The window the light is drawn in right now: the GPU layer when it carries the look.</summary>
    public IntPtr TopHandle => _oled is { IsRunning: true } oled ? oled.Handle  : _gpu is { IsRunning: true } gpu ? gpu.Handle : _holo is { IsRunning: true } holo ? holo.Handle
        : _cubeLayer is { IsRunning: true } cubes ? cubes.Handle : _novelLayer is { IsRunning: true } novel ? novel.Handle : _hwnd;

    /// <summary>The OLED panel runs under the dock: its glass has to keep up with what moves behind it.</summary>
    public bool PanelBehindDock => _oled is { IsRunning: true };

    public bool Showing => IsVisible || _gpu is { IsRunning: true } || _holo is { IsRunning: true } || _oled is { IsRunning: true } || _cubeLayer is { IsRunning: true } || _novelLayer is { IsRunning: true };

    /// <summary>The GPU cube scene while it exists; the benchmark holds its level.</summary>
    internal CubeGpuScene? CubeScene => _cubeScene;

    /// <summary>The WPF cubes; the benchmark holds their level.</summary>
    internal CubeVisualizer Cubes => _cubes;

    protected override void OnClosed(EventArgs e)
    {
        _hideTimer?.Stop();
        GlassSettings.Changed -= OnGlassSettingsChanged;
        CaptureReveal.Forget(_hwnd);
        _gpu?.Dispose();
        _gpu = null;
        _holo?.Dispose();
        _holo = null;
        _cubeLayer?.Dispose();
        _cubeLayer = null;
        _novelLayer?.Dispose();
        _novelLayer = null;
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
        PlacePanel(bounds);
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
        _holo?.PlaceBelowDock(dock);
        _cubeLayer?.PlaceBelowDock(dock);
        _novelLayer?.PlaceBelowDock(dock);
        _oled?.PlaceBelowDock(dock);
    }

    public void SetRunning(bool running)
    {
        var look = GlassSettings.VisualizerLook;
        // the OLED panel only exists on the GPU; the dust looks move there when the setting asks for it
        // the LED look is the OLED panel's show on a colour LED wall: the same layer, other flags
        if (look == VisualizerStyle.Led) look = VisualizerStyle.Oled;
        bool oled = running && look == VisualizerStyle.Oled && Layer(ref _oled, () => _panel = new OledGpuScene { Theme = GlassSettings.Theme }, capturable: true);
        PlacePanel(_bounds);
        ApplyPanelSettings();
        // without the GPU the MilkDrop dust plays as the plain dust wave
        bool milk = look == VisualizerStyle.DustMilk;
        if (milk && !GlassSettings.GpuVisualizer) look = VisualizerStyle.DustWave;
        bool dust = running && GlassSettings.GpuVisualizer && look is VisualizerStyle.Stardust or VisualizerStyle.DustWave or VisualizerStyle.DustMilk
            && Layer(ref _gpu, () => _dust = new StardustGpuScene());
        if (_dust is not null) _dust.Milk = milk;
        if (_gpu is not null)
        {
            _gpu.Wave = look is VisualizerStyle.DustWave or VisualizerStyle.DustMilk;
            _gpu.SetRunning(dust);
        }

        // the hologram's MilkDrop echoes need the GPU's feedback; without it the WPF hologram plays alone
        bool holo = running && GlassSettings.GpuVisualizer && look == VisualizerStyle.Hologram
            && Layer(ref _holo, () => new HologramGpuScene());
        _holo?.SetRunning(holo);

        // the cubes are thousands of small quads: on the GPU they cost no tessellation and no layered-window copy
        bool cubes = running && GlassSettings.GpuVisualizer && look == VisualizerStyle.Cubes
            && Layer(ref _cubeLayer, () => _cubeScene = new CubeGpuScene());
        _cubeLayer?.SetRunning(cubes);

        bool novel = running && GlassSettings.GpuVisualizer && GpuSceneCatalog.Has(look);
        if (novel && _novelLayer is not null && _novelLook != look)
        {
            _novelLayer.Dispose();
            _novelLayer = null;
        }
        if (novel)
        {
            _novelLook = look;
            novel = Layer(ref _novelLayer, () => GpuSceneCatalog.Create(look));
        }
        _novelLayer?.SetRunning(novel);
        // without the GPU these looks play as stardust
        if (GpuSceneCatalog.Has(look) && !novel) look = VisualizerStyle.Stardust;

        _oled?.SetRunning(oled);

        // without a GPU the OLED panel falls back to stardust
        if (look == VisualizerStyle.Oled && !oled) look = VisualizerStyle.Stardust;

        // a GPU layer carries its look by itself; this window only fades out what it drew before
        running &= !(dust || oled || holo || cubes || novel);
        _running = running;
        _glow.IsActive = running && look == VisualizerStyle.Glow;
        _hologram.IsActive = running && look == VisualizerStyle.Hologram;
        _cubes.IsActive = running && look == VisualizerStyle.Cubes;
        _stardust.IsActive = running && look == VisualizerStyle.Stardust;
        _dustWave.IsActive = running && look == VisualizerStyle.DustWave;
        _silkContour.IsActive = running && look == VisualizerStyle.SilkContour;
        bool sculpted = look is VisualizerStyle.Prism or VisualizerStyle.Wheat or VisualizerStyle.Ribbon;
        if (sculpted)
        {
            _sculpted.Look = look switch
            {
                VisualizerStyle.Wheat => SculptedLook.Wheat,
                VisualizerStyle.Ribbon => SculptedLook.Ribbon,
                _ => SculptedLook.Prism,
            };
        }
        _sculpted.IsActive = running && sculpted;
        ApplyLedScreen();

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

    /// <summary>What plays, for the OLED panel: whether it plays, and the song for its left wing.</summary>
    public void SetMusic(bool playing, MediaInfo? media, double sourceX = 0.5)
    {
        if (media is not null && !ReferenceEquals(media.Artwork, _coverSource))
        {
            _coverSource = media.Artwork;
            _cover = CoverPixels(media.Artwork as BitmapSource);
        }

        if (_novelLayer is not null)
        {
            _novelLayer.SetCover(media is null ? null : _cover, OledOverlay.CoverPixels);
            _novelLayer.SourceX = (float)Math.Clamp(sourceX, 0, 1);
        }

        if (_panel is null) return;
        _panel.MusicPlaying = playing;
        if (media is null || string.IsNullOrWhiteSpace(media.Title))
        {
            _panel.SetMedia(string.Empty, string.Empty, 0, 0, false, null);
            return;
        }

        _panel.SetMedia(media.Title, media.Artist, media.Position, media.Duration, media.Playing, _cover);
    }

    // the cover, squared and scaled down to what the panel converts it from, as BGRA bytes
    private static byte[]? CoverPixels(BitmapSource? source)
    {
        if (source is null || source.PixelWidth <= 0 || source.PixelHeight <= 0) return null;
        try
        {
            int size = OledOverlay.CoverPixels;
            int side = Math.Min(source.PixelWidth, source.PixelHeight);
            var square = new CroppedBitmap(source, new Int32Rect((source.PixelWidth - side) / 2, (source.PixelHeight - side) / 2, side, side));
            var scaled = new TransformedBitmap(square, new ScaleTransform(size / (double)side, size / (double)side));
            var bgra = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
            if (bgra.PixelWidth != size || bgra.PixelHeight != size) return null;
            var pixels = new byte[size * size * 4];
            bgra.CopyPixels(pixels, size * 4, 0);
            return pixels;
        }
        catch
        {
            return null;
        }
    }

    private void PlacePanel(Rect bounds)
    {
        if (_panel is not null && bounds.Width > 0 && !_dockRect.IsEmpty)
        {
            _panel.SetDock((float)((_dockRect.Left - bounds.Left) / bounds.Width), (float)((_dockRect.Right - bounds.Left) / bounds.Width));
        }
    }

    // The panel reads its own copy of the theme on the render thread; a settings change hands it the new one.
    private void OnGlassSettingsChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(OnGlassSettingsChanged));
            return;
        }

        ApplyPanelSettings();
        ApplyLedScreen();
    }

    private void ApplyPanelSettings()
    {
        if (_panel is null) return;
        _panel.Theme = GlassSettings.Theme;
        _panel.ForcedEffect = GlassSettings.OledEffect >= 0 ? GlassSettings.OledEffect : null;
        bool led = GlassSettings.VisualizerLook == VisualizerStyle.Led;
        _panel.Colour = led || GlassSettings.OledColour;
        _panel.Lens = led || GlassSettings.OledColour;
        _panel.Subpixels = led;
    }

    // The LED screen glass over every other look: the GPU layers run it as a last pass, the WPF looks as an effect.
    private void ApplyLedScreen()
    {
        bool on = GlassSettings.LedOverlay;
        if (_gpu is not null) _gpu.LedScreen = on;
        if (_holo is not null) _holo.LedScreen = on;
        if (_cubeLayer is not null) _cubeLayer.LedScreen = on;
        if (_novelLayer is not null) _novelLayer.LedScreen = on;
        if (!on || !LedScreenEffect.IsAvailable)
        {
            _root.Effect = null;
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var size = new Point(_root.ActualWidth * dpi.DpiScaleX, _root.ActualHeight * dpi.DpiScaleY);
        if (_root.Effect is not LedScreenEffect effect) _root.Effect = effect = new LedScreenEffect();
        effect.Size = size;
        effect.Pitch = 3 * dpi.DpiScaleX; // 3 DIP, as the GPU layers draw it
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
        _holo?.Place(pixels, _scale, _dock);
        _cubeLayer?.Place(pixels, _scale, _dock);
        _novelLayer?.Place(pixels, _scale, _dock);
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
        _sculpted.Capsule = strip;
    }
}
