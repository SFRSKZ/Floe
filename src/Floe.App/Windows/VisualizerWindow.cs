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
using Floe.Plugins;

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
    private IThemedGpuScene? _themed;
    private GpuLayerWindow? _idleLayer;
    private FeedbackGpuScene? _idleScene;
    private GpuLayerWindow? _oled;
    private WebVisualizerHost? _web;
    private bool _webFailed;
    private string? _failedPluginId;
    private double _webClock;
    private OledGpuScene? _panel;
    private CubeGpuScene? _cubeScene;
    private ImageSource? _coverSource;
    private byte[]? _cover;
    private Rect _dockRect;
    private bool _gpuFailed;
    private int _gpuRebuilds;
    private DateTime _gpuFailedAt;
    private IntPtr _hwnd;
    private IntPtr _dock;
    private Rect _bounds = Rect.Empty;
    private double _scale = 1;
    private DispatcherTimer? _hideTimer;
    private bool _placed;
    private bool _running;
    // on the taskbar: the spans of the strip kept clear for the icons and the bar's ends (see GpuLayerWindow.SetClear)
    private float[] _clear = Array.Empty<float>();
    // the screen edge the strip lies on (Floe.Core.Models.DockEdge); the looks draw it foot down and it is turned on
    private int _edge;

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
        root.SizeChanged += (_, _) => { ApplyLedScreen(); UpdateCapsule(); };
        _root = root;
        Content = root;
        SizeChanged += (_, _) => UpdateCapsule();
        GlassSettings.Changed += OnGlassSettingsChanged;
    }

    public bool IsRunning => _glow.IsActive || _hologram.IsActive || _cubes.IsActive || _stardust.IsActive || _dustWave.IsActive || _silkContour.IsActive || _sculpted.IsActive;

    public IntPtr Handle => _hwnd;

    /// <summary>The window the light is drawn in right now: the GPU layer when it carries the look.</summary>
    public IntPtr TopHandle => _web is { IsRunning: true } web ? web.Handle : _oled is { IsRunning: true } oled ? oled.Handle : _gpu is { IsRunning: true } gpu ? gpu.Handle : _holo is { IsRunning: true } holo ? holo.Handle
        : _cubeLayer is { IsRunning: true } cubes ? cubes.Handle : _novelLayer is { IsRunning: true } novel ? novel.Handle : _idleLayer is { IsRunning: true } idle ? idle.Handle : _hwnd;

    /// <summary>The OLED panel runs under the dock: its glass has to keep up with what moves behind it.</summary>
    public bool PanelBehindDock => _oled is { IsRunning: true };

    public bool Showing => IsVisible || _gpu is { IsRunning: true } || _holo is { IsRunning: true } || _oled is { IsRunning: true } || _cubeLayer is { IsRunning: true } || _novelLayer is { IsRunning: true } || _idleLayer is { IsRunning: true };

    /// <summary>Whether the cyber feedback look is on the GPU layer now; the dock reserves the extra strip height only then.</summary>
    public bool CyberGpuPlaying => (_novelLayer is { IsRunning: true } && GlassSettings.IsCyberLook(_novelLook)) || _idleLayer is { IsRunning: true };

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
        _themed = null;
        _idleLayer?.Dispose();
        _idleLayer = null;
        _idleScene = null;
        _oled?.Dispose();
        _oled = null;
        _web?.Dispose();
        _web = null;
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
        ApplyEdge();
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
        PlaceWeb();
    }

    /// <summary>Spans of the strip (0..1 pairs) the GPU looks leave empty; an empty array clears nothing.</summary>
    public void SetClear(float[] spans)
    {
        // a crowded bar gives overlapping spans: merged, the mask never fades back in where two of them meet
        spans = Floe.Core.Services.ClearSpans.Merge(spans);
        _clear = spans;
        // the WPF looks (no GPU) keep the same spans empty through a mask over the whole strip, its ends as soft
        // as the GPU layers' (ClearFeatherPass)
        // the icons move while they animate and set the spans every frame: the mask is only redrawn when it changes
        var maskKey = (string.Join(',', spans), _bounds.Width, _bounds.Height);
        if (maskKey != _clearMaskKey)
        {
            _clearMaskKey = maskKey;
            _root.OpacityMask = _bounds.Width >= 1 && _bounds.Height >= 1
                ? RoundClearMask(spans, _bounds.Width, _bounds.Height)
                : ClearMask(spans, _bounds.Width > 0 ? ClearFeatherDip / _bounds.Width : 0);
        }

        _gpu?.SetClear(spans);
        _holo?.SetClear(spans);
        _cubeLayer?.SetClear(spans);
        _novelLayer?.SetClear(spans);
        _idleLayer?.SetClear(spans);
        _oled?.SetClear(spans);
    }

    private (string, double, double) _clearMaskKey;

    // how far the light thins out outside a cleared span, in DIP (the GPU layers use the same)
    private const double ClearFeatherDip = 36;

    /// <summary>
    /// Keeps every icon/group span completely clear (alpha = 0) and feathers smoothly outside each open boundary
    /// into the free glass, matching ClearFeatherPass on the GPU.
    /// </summary>
    private static Brush? RoundClearMask(float[] spans, double widthDip, double heightDip)
    {
        if (spans.Length < 2) return null;
        int width = Math.Clamp((int)Math.Round(widthDip), 1, 8192), height = Math.Clamp((int)Math.Round(heightDip), 1, 512);
        double feather = ClearFeatherDip * width / widthDip;
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double px = x + .5, keep = 1;
                for (int i = 0; i + 1 < spans.Length; i += 2)
                {
                    double a = spans[i] * width, b = spans[i + 1] * width;
                    if (px >= a && px <= b)
                    {
                        keep = 0;
                        break;
                    }

                    if (px < a && a > .5) keep *= Smooth(0, feather, a - px);
                    else if (px > b && b < width - .5) keep *= Smooth(0, feather, px - b);
                }

                // premultiplied white: only the alpha counts in a mask
                byte alpha = (byte)Math.Round(Math.Clamp(keep, 0, 1) * 255);
                int at = ((y * width) + x) * 4;
                pixels[at] = pixels[at + 1] = pixels[at + 2] = pixels[at + 3] = alpha;
            }
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        bitmap.Freeze();
        var mask = new ImageBrush(bitmap) { Stretch = Stretch.Fill };
        mask.Freeze();
        return mask;

        static double Smooth(double from, double to, double x)
        {
            double t = Math.Clamp((x - from) / (to - from), 0, 1);
            return t * t * (3 - (2 * t));
        }
    }

    /// <summary>
    /// Opaque along the strip except inside each merged 0..1 span pair, ramping over <paramref name="feather"/>
    /// (a fraction of the strip) before and after each; null when nothing is cleared.
    /// </summary>
    private static Brush? ClearMask(float[] spans, double feather)
    {
        if (spans.Length < 2) return null;
        var stops = new GradientStopCollection();
        for (int i = 0; i + 1 < spans.Length; i += 2)
        {
            double from = spans[i], to = spans[i + 1];
            double rampIn = i >= 2 ? Math.Min(feather, (from - spans[i - 1]) / 2) : Math.Min(feather, from);
            double rampOut = i + 2 < spans.Length ? Math.Min(feather, (spans[i + 2] - to) / 2) : Math.Min(feather, 1 - to);
            if (from > 0) stops.Add(new GradientStop(Colors.Black, Math.Max(0, from - rampIn)));
            stops.Add(new GradientStop(Colors.Transparent, from));
            stops.Add(new GradientStop(Colors.Transparent, to));
            if (to < 1) stops.Add(new GradientStop(Colors.Black, Math.Min(1, to + rampOut)));
        }

        if (stops.Count == 0) return null;
        var mask = new LinearGradientBrush(stops, new Point(0, 0.5), new Point(1, 0.5));
        mask.Freeze();
        return mask;
    }

    /// <summary>
    /// The looks that keep to the 48 DIP taskbar: drawn as light over the bar, with no backdrop of their own that
    /// would paint the glass over, and nothing that needs the dock strip's height.
    /// </summary>
    internal static bool FitsTaskbar(VisualizerStyle look) => look is VisualizerStyle.CyberSignal or VisualizerStyle.CyberBlackwall
        or VisualizerStyle.CyberGlyph or VisualizerStyle.CyberRain or VisualizerStyle.CyberSamurai or VisualizerStyle.CyberPulse
        or VisualizerStyle.CyberRift or VisualizerStyle.CyberZiggurat or VisualizerStyle.CyberNetdive
        or VisualizerStyle.CyberRedline
        or VisualizerStyle.CyberJaws
        or VisualizerStyle.CyberBlackwallCinder or VisualizerStyle.CyberArasakaOs or VisualizerStyle.CyberBraindanceSuite
        or VisualizerStyle.CyberApogee or VisualizerStyle.CyberCantoDaemon or VisualizerStyle.CyberNightCity
        or VisualizerStyle.CyberTraumaTeam or VisualizerStyle.CyberLiquidChrome or VisualizerStyle.CyberDogtownFlir
        or VisualizerStyle.CyberMonowire
        or VisualizerStyle.Chladni or VisualizerStyle.Fireflies or VisualizerStyle.Ferrofluid
        or VisualizerStyle.Stardust or VisualizerStyle.DustWave or VisualizerStyle.DustMilk;

    public void PlaceBelowDock(IntPtr dock)
    {
        _dock = dock;
        WindowStyling.PlaceBelow(_hwnd, dock);
        _gpu?.PlaceBelowDock(dock);
        _holo?.PlaceBelowDock(dock);
        _cubeLayer?.PlaceBelowDock(dock);
        _novelLayer?.PlaceBelowDock(dock);
        _idleLayer?.PlaceBelowDock(dock);
        _oled?.PlaceBelowDock(dock);
        _web?.PlaceBelowDock(dock);
    }

    public void SetRunning(bool running, bool idle = false)
    {
        var look = GlassSettings.VisualizerLook;
        // a cyber theme plays its own strips, whatever the glass themes were left on
        if (GlassSettings.Cyber && !GlassSettings.IsCyberLook(look)) look = VisualizerStyle.CyberBlackwall;
        // on the taskbar a look that does not fit gives way to one that does
        if (DockSettings.TaskbarOn && !FitsTaskbar(look)) look = GlassSettings.Cyber ? VisualizerStyle.CyberSignal : VisualizerStyle.Stardust;
        // the OLED panel only exists on the GPU; the dust looks move there when the setting asks for it
        // the LED look is the OLED panel's show on a colour LED wall: the same layer, other flags
        if (look == VisualizerStyle.Led) look = VisualizerStyle.Oled;
        bool web = running && look == VisualizerStyle.Plugin && WebLayer();
        if (web)
        {
            PlaceWeb();
            ApplyWebAppearance();
        }

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

        bool wantIdle = running && idle && GlassSettings.Cyber && GlassSettings.GpuVisualizer;
        bool idleOn = wantIdle && Layer(ref _idleLayer, () =>
        {
            var scene = new Effects.Gpu.Cp77.CyberIdleHudGpuScene();
            _idleScene = scene;
            return scene;
        }, needAudio: false);
        if (_idleLayer is not null)
        {
            if (idleOn) _idleLayer.Fps = 24;
            _idleLayer.SetRunning(idleOn);
        }

        bool novel = running && !wantIdle && GlassSettings.GpuVisualizer && GpuSceneCatalog.Has(look);
        if (novel && _novelLayer is not null && _novelLook != look)
        {
            _novelLayer.Dispose();
            _novelLayer = null;
            _themed = null;
        }
        if (novel)
        {
            _novelLook = look;
            novel = Layer(ref _novelLayer, () =>
            {
                var scene = GpuSceneCatalog.Create(look);
                _themed = scene as IThemedGpuScene;
                return scene;
            });
            if (!novel) _themed = null;
        }
        _novelLayer?.SetRunning(novel);
        ApplySceneColours();
        // without the GPU these looks play as stardust; the cyber looks have nothing to fall back on
        if (GpuSceneCatalog.Has(look) && !novel && !wantIdle && !GlassSettings.Cyber) look = VisualizerStyle.Stardust;

        _oled?.SetRunning(oled);
        _web?.SetRunning(web);

        // without a GPU (or a working plugin) the OLED panel and the plugin fall back to stardust
        if ((look == VisualizerStyle.Oled && !oled) || (look == VisualizerStyle.Plugin && !web)) look = VisualizerStyle.Stardust;

        // a GPU layer carries its look by itself; this window only fades out what it drew before
        running &= !(dust || oled || holo || cubes || novel || idleOn || web);
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

    // the span the GPU looks keep clear, in the strip's DIPs; empty follows the gap Place was handed
    private Rect? _gpuDock;
    // the same span handed over as 0..1 along the strip; both null leaves the say to the rect above (or the gap)
    private float? _gpuLeft;
    private float? _gpuRight;

    /// <summary>
    /// The span the GPU looks keep clear, 0..1 along the strip as the looks draw it (foot down, left to right); null
    /// follows the gap <see cref="Place"/> hands over. The WPF looks keep their own mask (<see cref="SetClear"/>), so
    /// this never moves them.
    /// </summary>
    public void SetGpuDock(float? left, float? right)
    {
        if (left == _gpuLeft && right == _gpuRight && _gpuDock is null) return;
        _gpuLeft = left;
        _gpuRight = right;
        _gpuDock = null;
        PlacePanel(_bounds);
    }

    /// <summary>
    /// The dock's span the GPU looks keep clear, in the strip's DIPs (the space <see cref="Place"/> hands the gap over
    /// in); null follows that gap. The WPF looks keep their own mask (<see cref="SetClear"/>), so this never moves them.
    /// </summary>
    public void SetGpuDock(Rect? dock)
    {
        if (dock == _gpuDock && _gpuLeft is null) return;
        _gpuDock = dock;
        _gpuLeft = _gpuRight = null;
        PlacePanel(_bounds);
    }

    private void PlacePanel(Rect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        // a span handed over straight (the taskbar) is already where the looks draw it
        if (_gpuLeft is { } spanLeft && _gpuRight is { } spanRight)
        {
            _novelLayer?.SetDock(spanLeft, spanRight);
            _idleLayer?.SetDock(spanLeft, spanRight);
            _panel?.SetDock(spanLeft, spanRight);
            return;
        }

        var dock = _gpuDock ?? _dockRect;
        if (dock.IsEmpty) return;
        var (left, right) = AlongStrip(bounds, dock);
        _novelLayer?.SetDock(left, right);
        _idleLayer?.SetDock(left, right);
        _panel?.SetDock(left, right);
    }

    /// <summary>Where <paramref name="rect"/> stands along the strip, 0..1 as the looks draw it (foot down, left to right).</summary>
    private (float Left, float Right) AlongStrip(Rect bounds, Rect rect) => _edge switch
    {
        // down either side the strip runs from the top, as the dock's icons do
        2 or 3 => ((float)((rect.Top - bounds.Top) / bounds.Height), (float)((rect.Bottom - bounds.Top) / bounds.Height)),
        _ => ((float)((rect.Left - bounds.Left) / bounds.Width), (float)((rect.Right - bounds.Left) / bounds.Width)),
    };

    private void ApplyEdge()
    {
        var edge = (int)DockSettings.Edge;
        if (edge != _edge)
        {
            _edge = edge;
            // the WPF looks are laid out as the bottom strip and turned with it, as the dock is
            _root.LayoutTransform = edge switch
            {
                1 => new ScaleTransform(1, -1),
                2 => new RotateTransform(90),
                3 => new MatrixTransform(0, 1, 1, 0, 0, 0),
                _ => Transform.Identity,
            };
        }

        _gpu?.SetEdge(edge);
        _holo?.SetEdge(edge);
        _cubeLayer?.SetEdge(edge);
        _novelLayer?.SetEdge(edge);
        _idleLayer?.SetEdge(edge);
        _oled?.SetEdge(edge);
    }

    // The GPU scenes read their own copy of the theme on the render thread; a settings change hands them the new one.
    private void OnGlassSettingsChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(OnGlassSettingsChanged));
            return;
        }

        HangWatch.Mark("visualizer glass");
        ApplyPanelSettings();
        ApplySceneColours();
        ApplyLedScreen();
        HangWatch.Mark("visualizer glass done");
    }

    // A feedback scene reads its colours on the render thread; this hands it the current ones from the UI thread.
    private void ApplySceneColours()
    {
        var theme = GlassSettings.VisualizerThemeColours ? CyberLook.Current : null;
        if (_themed is not null) _themed.Theme = theme;
        if (_idleScene is not null) _idleScene.Theme = theme;
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
        if (_idleLayer is not null) _idleLayer.LedScreen = on;
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

    private bool Layer(ref GpuLayerWindow? layer, Func<IGpuScene> scene, bool capturable = false, bool needAudio = true)
    {
        if (_gpuFailed)
        {
            // a fullscreen game or a display change can take the GPU away for a while: try again later
            if (DateTime.UtcNow - _gpuFailedAt < TimeSpan.FromSeconds(10)) return false;
            _gpuFailed = false;
            _gpuRebuilds = 0;
            GpuLog("gpu retry");
        }

        // a layer that stopped on an error is rebuilt with a fresh scene; one that keeps failing sends the looks
        // to the WPF path for good instead of a rebuild every poll
        if (layer is { Halted: true })
        {
            layer.Dispose();
            layer = null;
            if (++_gpuRebuilds > 3)
            {
                GpuFail("gpu layer keeps halting");
                return false;
            }
        }

        if (layer is null)
        {
            try
            {
                layer = new GpuLayerWindow(scene(), capturable, needAudio);
            }
            catch (Exception ex)
            {
                GpuLog($"gpu layer: {ex.Message}");
            }

            // no window means no device or no composition: stay on the WPF path for good
            if (layer is null || layer.Handle == IntPtr.Zero)
            {
                layer?.Dispose();
                layer = null;
                GpuFail("gpu layer has no window");
                return false;
            }

            layer.SetClear(_clear);
            layer.SetEdge(_edge);

            PlaceGpu();
        }

        return true;
    }

    private void GpuFail(string why)
    {
        _gpuFailed = true;
        _gpuFailedAt = DateTime.UtcNow;
        GpuLog(why);
    }

    private static void GpuLog(string line)
    {
        try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "floe.log"), $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}"); }
        catch { }
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
        _idleLayer?.Place(pixels, _scale, _dock);
        _oled?.Place(pixels, _scale, _dock);
    }

    /// <summary>Creates the plugin host on first use; a failure falls back to a built-in look.</summary>
    private bool WebLayer()
    {
        var plugin = VisualizerPlugins.Selected;
        if (plugin is null)
        {
            return false;
        }

        // a plugin that failed to start is retried only when the selection actually changes
        if (_webFailed && _failedPluginId == plugin.Id)
        {
            return false;
        }

        _webFailed = false;
        _failedPluginId = null;

        if (_web is not null && _web.PluginId != plugin.Id)
        {
            _web.Dispose();
            _web = null;
        }

        if (_web is not null)
        {
            return true;
        }

        try
        {
            _web = new WebVisualizerHost(plugin);
            if (_web.Handle == IntPtr.Zero || _web.Failure is not null)
            {
                _web.Dispose();
                _web = null;
                _webFailed = true;
                _failedPluginId = plugin.Id;
                return false;
            }

            // The strip is meant to be seen through the dock's glass, so it stays in the backdrop
            // capture (unlike the WPF glow, which would feed its own light back into the blur).
            WindowAffinity.IncludeInCapture(_web.Handle);
            if (_dock != IntPtr.Zero)
            {
                _web.PlaceBelowDock(_dock);
            }

            PlaceWeb();
            ApplyWebAppearance();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Floe.Plugin] host: {ex.Message}");
            _web?.Dispose();
            _web = null;
            _webFailed = true;
            _failedPluginId = plugin.Id;
            return false;
        }
    }

    private void PlaceWeb()
    {
        if (_web is null || _bounds.IsEmpty) return;

        var pixels = new PluginRect(
            (int)Math.Round(_bounds.Left * _scale),
            (int)Math.Round(_bounds.Top * _scale),
            (int)Math.Round(_bounds.Width * _scale),
            (int)Math.Round(_bounds.Height * _scale));

        PluginRect? dock = null;
        if (!_dockRect.IsEmpty)
        {
            dock = new PluginRect(
                (int)Math.Round(_dockRect.Left * _scale),
                (int)Math.Round(_dockRect.Top * _scale),
                (int)Math.Round(_dockRect.Width * _scale),
                (int)Math.Round(_dockRect.Height * _scale));
        }

        _web.Place(pixels, _scale, dock);
        if (_dock != IntPtr.Zero)
        {
            _web.PlaceBelowDock(_dock);
        }

        // the palette follows the dock's colours while the settings window is open
        if (_web.IsRunning)
        {
            ApplyWebAppearance();
        }
    }

    private void ApplyWebAppearance()
    {
        if (_web is null) return;
        var plugin = VisualizerPlugins.Selected;
        if (plugin is null) return;

        _webClock = Environment.TickCount64 / 1000.0;
        var primary = Color.FromRgb(0x5C, 0xA8, 0xFF);
        var (core, second, accent, deep) = VisualizerPlugins.Palette(_webClock, primary);
        double fps = Performance.VisualizerFps;
        _web.SetAppearance(core, second, accent, deep, VisualizerPlugins.OptionsJson(plugin), (int)(fps > 0 ? fps : 60));
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
        // The whole strip is the "capsule": the light rises from the strip's foot, the screen's edge. The root is
        // laid out as the bottom strip on every edge, so its own size is the strip's.
        var strip = new Rect(0, 0, Math.Max(1, _root.ActualWidth), Math.Max(1, _root.ActualHeight));
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
