using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Floe.App.Services;
using Floe.App.Effects.Glass;
using Floe.App.Effects.MetalRim;
using Floe.App.ViewModels;
using Floe.Core.Interop;
using Floe.Core.Models;
using Floe.Core.Services;

namespace Floe.App.Windows;

public partial class DockWindow
{

    internal static BitmapSource MakeNoiseBitmap()
    {
        const int size = 64;
        var random = new Random(20260921);
        var pixels = new byte[size * size * 4];

        for (var i = 0; i < size * size; i++)
        {
            var value = (byte)random.Next(0, 256);
            pixels[(i * 4) + 0] = value;
            pixels[(i * 4) + 1] = value;
            pixels[(i * 4) + 2] = value;
            pixels[(i * 4) + 3] = 255;
        }

        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bitmap.Freeze();
        return bitmap;
    }

    internal static Brush BuildGlassBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
        };
        if (GlassSettings.Tron)
        {
            // glossy black glass, with the grid's dim light just showing at the bottom
            var dim = ThemePalette.DimColor;
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xD8, 0x03, 0x05, 0x07), 0.0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xDA, 0x04, 0x06, 0x09), 0.55));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xDC, dim.R, dim.G, dim.B), 1.0));
            brush.Freeze();
            return brush;
        }

        if (GlassSettings.Cyber)
        {
            var body = new SolidColorBrush(CyberLook.Current.Body);
            body.Freeze();
            return body;
        }

        if (GlassSettings.DockColor is { } tint)
        {
            // the chosen colour over the surface: enough of it to read as the dock's colour, the desktop still behind
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xB4, tint.R, tint.G, tint.B), 0.0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xC4, tint.R, tint.G, tint.B), 1.0));
            brush.Freeze();
            return brush;
        }

        switch (GlassSettings.Surface)
        {
            case DockSurface.Mica:
                // one even, cool dark tint over the blur: no top-to-bottom shading to read as gloss
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x9C, 0x20, 0x21, 0x28), 0.0));
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xA4, 0x1C, 0x1D, 0x24), 1.0));
                brush.Freeze();
                return brush;
            case DockSurface.Frost:
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x42, 0xFF, 0xFF, 0xFF), 0.0));
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x34, 0xFF, 0xFF, 0xFF), 1.0));
                brush.Freeze();
                return brush;
            case DockSurface.Graphite:
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xF0, 0x1D, 0x1E, 0x23), 0.0));
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xF4, 0x19, 0x1A, 0x1F), 1.0));
                brush.Freeze();
                return brush;
            case DockSurface.Floating:
                return Brushes.Transparent;
        }

        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x52, 0x25, 0x25, 0x25), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x58, 0x1E, 0x1E, 0x1E), 0.08));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x59, 0x1E, 0x1E, 0x1E), 0.42));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x63, 0x1A, 0x1A, 0x1A), 1.0));
        brush.Freeze();
        return brush;
    }

    internal static LinearGradientBrush BuildSheenBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x09, 0xFF, 0xFF, 0xFF), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.4));
        return brush;
    }

    internal static Brush BuildRimBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
        };
        if (GlassSettings.Tron)
        {
            // one neon hairline: at its brightest along the top edge, still lit at the bottom
            var line = ThemePalette.LineColor;
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xE6, line.R, line.G, line.B), 0.0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x8C, line.R, line.G, line.B), 0.45));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xB4, line.R, line.G, line.B), 1.0));
            brush.Freeze();
            return brush;
        }

        if (GlassSettings.Cyber) return Brushes.Transparent;

        // the calmer surfaces keep only a faint inner line; their edge is the metal outline
        var (top, middle, bottom) = GlassSettings.Surface switch
        {
            DockSurface.Mica => ((byte)0x22, (byte)0x14, (byte)0x0A),
            DockSurface.Frost => ((byte)0x40, (byte)0x2A, (byte)0x1C),
            DockSurface.Graphite => ((byte)0x16, (byte)0x0E, (byte)0x08),
            DockSurface.Floating => ((byte)0x00, (byte)0x00, (byte)0x00),
            _ => ((byte)0x40, (byte)0x22, (byte)0x14),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(top, 0xFF, 0xFF, 0xFF), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(middle, 0xFF, 0xFF, 0xFF), 0.35));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(bottom, 0xFF, 0xFF, 0xFF), 1.0));
        brush.Freeze();
        return brush;
    }

    private readonly Path _outerRim = new() { IsHitTestVisible = false };
    private readonly Canvas _shadowHost = new() { IsHitTestVisible = false };
    private readonly Path _shadow = new() { IsHitTestVisible = false };
    private readonly Image _smallBackdrop = new() { Stretch = Stretch.Fill };
    private readonly BlurEffect _smallBlur = new() { KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality };
    private readonly Canvas _metalHost = new() { IsHitTestVisible = false, Background = Brushes.Transparent, Visibility = Visibility.Collapsed };
    private readonly Border _metalSurface = new() { Background = Brushes.Transparent, CornerRadius = new CornerRadius(CornerRadius) };
    private readonly Image _metalPreview = new();
    private MetalRimEffect? _metalEffect;
    private RimWarpEffect? _rimWarp;
    private LiquidGlassEffect? _liquid;
    private Point4D _liquidCapsule;
    private Point _liquidSize;
    private double _liquidHover = double.NaN, _liquidBulge = double.NaN, _liquidPlateau = double.NaN, _liquidSigma = double.NaN, _liquidRadius = double.NaN;
    private bool _glassPreview;
    private RimStyle _appliedRim = (RimStyle)(-1);
    private double _metalTime;
    private double _backdropLogicalWidth;
    private RenderTargetBitmap? _backdropBlurred;
    private readonly RectangleGeometry _windowGlassBounds = new();
    private readonly RectangleGeometry _shadowClip = new();
    private double _dpi = 1;
    private bool _metalWarping;
    // last values pushed to the shaders; NaN never equals a real value, so the first write always lands
    private Size _metalSize;
    private double _metalCorner = double.NaN;
    private double _metalCursorX = double.NaN;
    private double _metalCursorY = double.NaN;
    private double _metalBend = double.NaN;
    private double _metalTimeApplied = double.NegativeInfinity;
    private Point _rimSize;
    private double _rimHover = double.NaN;
    private double _rimBulge = double.NaN;
    private double _rimPlateau = double.NaN;
    private double _rimSigma = double.NaN;
    private Point _rimStretch;

    // Diagnostics: FLOE_NOFX=shadow,vibrancy,metal switches single effects off to find render costs.
    private static readonly string NoFx = Environment.GetEnvironmentVariable("FLOE_NOFX") ?? "";

    private DropShadowEffect? _shadowEffect;

    // the shadow re-blurs with every bulge change, which a software-rendered dock cannot afford
    private void ApplyShadowLevel() => _shadow.Effect = Performance.Level == PerformanceLevel.Lite ? null : _shadowEffect;

    private void InitializeGlass()
    {
        _glassPreview = Environment.GetCommandLineArgs().Any(a => a.StartsWith("--render=", StringComparison.OrdinalIgnoreCase));
        Root.Clip = _windowGlassBounds;
        _shadow.Data = _shapeGeometry;
        _shadow.Fill = Brushes.Black;
        if (!NoFx.Contains("shadow")) _shadowEffect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.35, Color = Colors.Black, RenderingBias = RenderingBias.Performance };
        ApplyShadowLevel();
        // A 16k x 8k rect made the exclude combine walk 134M px whenever the capsule changed; the shadow
        // only reaches ~30 px past the capsule, so a padded capsule rect clips the same pixels for free.
        _shadowClip.Rect = ShadowClipRect();
        var exterior = new CombinedGeometry(GeometryCombineMode.Exclude, _shadowClip, _shapeGeometry);
        _shadowHost.Clip = exterior;
        _shadowHost.Children.Add(_shadow);
        ShapeLayer.Children.Add(_shadowHost);
        _outerRim.Clip = exterior;
        _outerRim.Data = _shapeGeometry;
        _outerRim.Stroke = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0));
        _smallBackdrop.Effect = _smallBlur;
        RenderOptions.SetBitmapScalingMode(_backdrop, BitmapScalingMode.Linear);
        if (RenderCapability.IsPixelShaderVersionSupported(3, 0) && !NoFx.Contains("vibrancy"))
        {
            // the liquid pass carries the same vibrancy, plus the refracting rim
            if (!NoFx.Contains("liquid") && LiquidGlassEffect.IsAvailable) _backdrop.Effect = _liquid = new LiquidGlassEffect();
            else _backdrop.Effect = new VibrancyEffect();
        }
        _metalHost.Children.Add(_metalSurface);
        _metalHost.Children.Add(_metalPreview);
        Canvas.SetLeft(_metalSurface, 24);
        Canvas.SetTop(_metalSurface, MaxBulge + 24);
        _dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        // the visual tree only reports the real monitor DPI once the window is connected
        Loaded += (_, _) => RefreshDpi();
        DpiChanged += (_, _) => RefreshDpi();
    }

    private void RefreshDpi()
    {
        _dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        UpdateGlassShape();
    }

    private void OnGlassSettingsChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(OnGlassSettingsChanged)); return; }
        ApplyGlassSettings();
        if (!IsMouseOver && !_menuOpen)
        {
            _targetDockScale = CompactScale; // shrinking may have been turned on or off
            EnsureAnimation();
        }
        if (_appliedShrink != GlassSettings.Shrink)
        {
            _appliedShrink = GlassSettings.Shrink;
            ApplyAppBarPosition(); // the reserved strip follows the dock's resting size
        }
        _shapeHover = double.NaN; // bulge style may have changed
        UpdateShape();
    }

    private void ApplyGlassSettings()
    {
        if (_appliedTheme != GlassSettings.Theme || _appliedSurface != GlassSettings.Surface || _appliedDockColor != GlassSettings.DockColor)
        {
            _appliedTheme = GlassSettings.Theme;
            _appliedSurface = GlassSettings.Surface;
            _appliedDockColor = GlassSettings.DockColor;
            // the Cyber themes need room for the decals, and only a rebuild sizes the window
            if (_roomForDecals != GlassSettings.Cyber)
            {
                _roomForDecals = GlassSettings.Cyber;
                RebuildIcons();
            }
            ApplySurface();
            ApplyCyberInk();
            _glass.Fill = BuildGlassBrush();
            _rim.Stroke = BuildRimBrush();
            // in Tron the sheen catches the neon line instead of white
            var sheen = GlassSettings.Tron ? ThemePalette.LineColor : Colors.White;
            foreach (var stop in _sheenBrush.GradientStops) stop.Color = Color.FromArgb(stop.Color.A, sheen.R, sheen.G, sheen.B);
            _spectralItem = null;
            UpdateAura();
            _appliedRim = (RimStyle)(-1);
        }

        var style = GlassSettings.Rim;
        if (_glassPreview)
        {
            var argument = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--renderrim=", StringComparison.OrdinalIgnoreCase));
            if (argument is not null && Enum.TryParse<RimStyle>(argument[12..], true, out var previewStyle)) style = previewStyle;
        }
        // the loose cubes only exist in Tron, and follow the shape every frame
        _tronCubes.Visibility = GlassSettings.Tron ? Visibility.Visible : Visibility.Collapsed;
        if (_appliedRim == style) return;
        _appliedRim = style;
        _metalEffect = _appliedRim switch
        {
            // the metal shader cannot follow cube steps: Tron keeps the plain neon hairline
            _ when GlassSettings.Tron || GlassSettings.Cyber => null,
            RimStyle.MetalSilver => MetalRimEffect.ApplyPreset("silver", true),
            RimStyle.MetalGold => MetalRimEffect.ApplyPreset("gold", true),
            RimStyle.MetalChromatic => MetalRimEffect.ApplyPreset("chromatic", true),
            // the newer surfaces always wear a metal outline; "plain" picks the one that suits the surface
            _ when GlassSettings.Surface == DockSurface.Graphite => GunmetalPreset(),
            _ when GlassSettings.Surface != DockSurface.Classic => MetalRimEffect.ApplyPreset("silver", true),
            _ => null,
        };
        if (NoFx.Contains("metal") || _metalEffect is { IsAvailable: false } || (!_glassPreview && !RenderCapability.IsPixelShaderVersionSupported(3, 0))) _metalEffect = null;
        // the stepped neon hairline gets a soft bloom of its own colour
        _rim.Effect = GlassSettings.Tron && !NoFx.Contains("glow")
            ? new DropShadowEffect { ShadowDepth = 0, BlurRadius = 8, Color = ThemePalette.LineColor, Opacity = .8, RenderingBias = RenderingBias.Performance }
            : null;
        _metalHost.Visibility = _metalEffect is null ? Visibility.Collapsed : Visibility.Visible;
        _rim.Visibility = _outerRim.Visibility = _metalEffect is null ? Visibility.Visible : Visibility.Collapsed;
        _metalSurface.Effect = _metalEffect;
        _metalHost.Effect = null; // UpdateGlassShape swaps the warp back in once there is a bulge to bend
        _metalWarping = false;
        if (_metalEffect is not null)
        {
            _metalEffect.Thickness = 1.8;
            _metalEffect.GlowGain = 0.45;
            if (!GlassSettings.Tron && GlassSettings.Surface != DockSurface.Classic)
            {
                // the metal fades to suit the surface: brightest on light frost, faintest with no plate behind it
                var (fade, glow, thickness) = GlassSettings.Surface switch
                {
                    DockSurface.Mica => (.62, .08, 1.4),
                    DockSurface.Frost => (.85, .12, 1.4),
                    DockSurface.Graphite => (.7, .05, 1.3),
                    _ => (.42, .04, 1.2),
                };
                _metalEffect.ShaderOpacity *= fade;
                _metalEffect.GlowGain = glow;
                _metalEffect.Thickness = thickness;
            }
            _rimWarp ??= new RimWarpEffect();
            ResetMetalParams();
        }
        UpdateGlassShape();
        if (_metalEffect is not null && IsMouseOver) EnsureAnimation();
    }

    private void ResetMetalParams()
    {
        _metalSize = default;
        _metalCorner = _metalCursorX = _metalCursorY = _metalBend = double.NaN;
        _metalTimeApplied = double.NegativeInfinity;
        _rimSize = default;
        _rimHover = _rimBulge = _rimPlateau = _rimSigma = double.NaN;
        _rimStretch = default;
    }

    // Dependency-property writes re-upload pixel-shader constants, so only push parameters that moved.
    // The backdrop is a picture of the screen behind the window, so it must not shrink with the dock, or the
    // compact glass shows what lies above it (a dark window edge, say) long after the dock left it. It gets the
    // root's scale undone round the same origin, and the liquid pass maps the capsule into its pixels.
    private void UpdateBackdropUnscale()
    {
        var scale = Math.Max(0.05, _rootScale.ScaleX);
        _backdropUnscale.CenterX = _windowWidth / 2;
        _backdropUnscale.CenterY = WindowHeight;
        _backdropUnscale.ScaleX = _backdropUnscale.ScaleY = 1 / scale;
        UpdateLiquidGlass();
    }

    private void UpdateLiquidGlass()
    {
        if (_liquid is null) return;
        var size = new Point(_windowWidth, WindowHeight);
        if (_liquidSize != size) { _liquidSize = size; _liquid.Size = size; }
        // root coordinates into the unscaled backdrop's pixels
        double scale = _rootScale.ScaleX, centerX = _windowWidth / 2, centerY = WindowHeight;
        var capsule = new Point4D(centerX + ((_capsuleLeft - centerX) * scale), centerY + ((RestTop - centerY) * scale),
            Math.Max(1, _capsuleWidth * scale), RestBarHeight * scale);
        if (_liquidCapsule != capsule) { _liquidCapsule = capsule; _liquid.Capsule = capsule; }
        if (Changed(ref _liquidHover, centerX + ((_currentHoverX - centerX) * scale))) _liquid.Hover = _liquidHover;
        if (Changed(ref _liquidBulge, EffectiveBulge * scale)) _liquid.Bulge = _liquidBulge;
        if (Changed(ref _liquidPlateau, BulgeShape.Plateau * scale)) _liquid.Plateau = _liquidPlateau;
        if (Changed(ref _liquidSigma, BulgeShape.Sigma * scale)) _liquid.Sigma = _liquidSigma;
        if (Changed(ref _liquidRadius, CornerRadius * scale)) _liquid.Radius = _liquidRadius;
    }

    private static bool Changed(ref double cache, double value)
    {
        if (cache.Equals(value)) return false;
        cache = value;
        return true;
    }

    // the shadow blur (24) plus its downward offset (6), with room for the rim hairline and side stretch
    private Rect ShadowClipRect()
    {
        const double reach = 48 + MaxSideStretch;
        return new Rect(_capsuleLeft - reach, RestTop - MaxBulge - reach, _capsuleWidth + (2 * reach), RestBarHeight + MaxBulge + (2 * reach));
    }

    private void UpdateGlassShape()
    {
        _windowGlassBounds.Rect = new Rect(0, 0, _windowWidth, WindowHeight);
        // Account for the root's compact/hover scale as well as monitor DPI.
        var pixel = 1 / (_dpi * Math.Max(0.1, _dockScale));
        _rim.StrokeThickness = 2 * pixel; // clipping discards the outer half: a 1px inner hairline
        _outerRim.StrokeThickness = 2 * pixel; // exterior clip discards the inner half
        _shadowClip.Rect = ShadowClipRect();
        // the loose cubes draw over the whole canvas, in window coordinates
        _tronCubes.Width = _windowWidth;
        _tronCubes.Height = WindowHeight;
        UpdateLiquidGlass();
        if (_metalEffect is null) return;
        var width = Math.Max(1, _capsuleWidth);
        var hover = _currentHoverX - _capsuleLeft;
        var strength = Math.Clamp(EffectiveBulge / MaxBulge, 0, 1);
        _metalHost.Width = width + 48;
        _metalHost.Height = RestBarHeight + MaxBulge + 48;
        Canvas.SetLeft(_metalHost, _capsuleLeft - 24);
        Canvas.SetTop(_metalHost, RestTop - MaxBulge - 24);
        _metalSurface.Width = width;
        _metalSurface.Height = RestBarHeight;
        var size = new Size(width, RestBarHeight);
        if (_metalSize != size) { _metalSize = size; _metalEffect.Size = size; }
        if (Changed(ref _metalCorner, CornerRadius)) _metalEffect.CornerRadius = CornerRadius;
        var cursorX = Math.Clamp(hover / width, 0, 1);
        if (Changed(ref _metalCursorX, cursorX)) _metalEffect.CursorX = cursorX;
        var cursorY = IsMouseOver ? Math.Clamp((Mouse.GetPosition(Root).Y - RestTop) / RestBarHeight, 0, 1) : 0;
        if (Changed(ref _metalCursorY, cursorY)) _metalEffect.CursorY = cursorY;
        var bend = strength * 0.12;
        if (Changed(ref _metalBend, bend)) _metalEffect.BendStrength = bend;
        if (Changed(ref _metalTimeApplied, _metalTime)) _metalEffect.Time = _metalTime;
        var stretch = new Point(MaxSideStretch * Gaussian(0, hover) * strength, MaxSideStretch * Gaussian(width, hover) * strength);
        if (_rimWarp is not null)
        {
            var rimSize = new Point(width, RestBarHeight);
            if (_rimSize != rimSize) { _rimSize = rimSize; _rimWarp.Size = rimSize; }
            if (Changed(ref _rimHover, hover)) _rimWarp.Hover = hover;
            if (Changed(ref _rimBulge, EffectiveBulge)) _rimWarp.Bulge = EffectiveBulge;
            if (Changed(ref _rimPlateau, BulgeShape.Plateau)) _rimWarp.Plateau = BulgeShape.Plateau;
            if (Changed(ref _rimSigma, BulgeShape.Sigma)) _rimWarp.Sigma = BulgeShape.Sigma;
            if (_rimStretch != stretch) { _rimStretch = stretch; _rimWarp.Stretch = stretch; }
        }
        UpdateRimWarp();
        if (_glassPreview)
        {
            _metalSurface.Visibility = Visibility.Collapsed;
            _metalSurface.Effect = null;
            _metalHost.Effect = null;
            using var renderer = new MetalRimPreview.OffscreenRenderer((int)Math.Ceiling(width), (int)RestBarHeight);
            var rawRim = renderer.Render(_metalEffect);
            _metalPreview.Source = GlassPreview.WarpRim(rawRim, width, RestBarHeight, hover, EffectiveBulge, stretch, BulgeShape.Plateau, BulgeShape.Sigma);
            _metalPreview.Width = _metalHost.Width;
            _metalPreview.Height = _metalHost.Height;
        }
    }

    // RimWarp offsets by bulge * profile, so it is the identity with no bulge and only costs a
    // full-surface re-render; detach it while the dock rests and put it back on the first bulge.
    private void UpdateRimWarp()
    {
        var warped = _rimWarp is not null && !_glassPreview && EffectiveBulge >= 0.05;
        if (warped == _metalWarping) return;
        _metalWarping = warped;
        _metalHost.Effect = warped ? _rimWarp : null;
    }

    private void PresentBackdrop(byte[] pixels, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0) return;
        PerfBackdrops++;
        if (_backdropBitmap is null || _backdropBitmap.PixelWidth != width || _backdropBitmap.PixelHeight != height)
        {
            _backdropBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
            _smallBackdrop.Source = _backdropBitmap;
            if (_gpuBackdrop) _backdrop.Source = _backdropBitmap;
        }
        if (_gpuBackdrop)
        {
            _backdropBitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
            return;
        }
        // Blur in the small visual's coordinate system, then upscale the result.
        if (_smallBackdrop.Width != width || _smallBackdrop.Height != height || _backdropLogicalWidth != _windowWidth)
        {
            _backdropLogicalWidth = _windowWidth;
            _smallBackdrop.Width = width;
            _smallBackdrop.Height = height;
            _smallBlur.Radius = BlurSettings.Radius * width / Math.Max(1, _windowWidth);
            _smallBackdrop.Measure(new Size(width, height));
            _smallBackdrop.Arrange(new Rect(0, 0, width, height));
            _backdropBlurred = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            _backdrop.Source = _backdropBlurred;
        }
        _backdropBitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        RenderBackdrop();
    }

    // A VisualBrush would re-run the blur on every dock frame; the backdrop only changes when a capture
    // arrives, so the blur runs once per capture into a bitmap the frames just scale.
    private void RenderBackdrop()
    {
        if (_backdropBlurred is null) return;
        _backdropBlurred.Clear();
        _backdropBlurred.Render(_smallBackdrop);
    }

    private void PrepareGlassPreview()
    {
        var args = Environment.GetCommandLineArgs();
        _glassPreview = args.Any(a => a.StartsWith("--render=", StringComparison.OrdinalIgnoreCase));
        if (!_glassPreview) return;
        _dockScale = _targetDockScale = 1;
        _rootScale.ScaleX = _rootScale.ScaleY = 1;
        var background = args.FirstOrDefault(a => a.StartsWith("--renderbg=", StringComparison.OrdinalIgnoreCase))?[11..];
        if (!args.Any(a => a.Equals("--no-blur", StringComparison.OrdinalIgnoreCase))
            && background is not null && System.IO.File.Exists(background))
        {
            var pixels = GlassPreview.Backdrop(background, Left, Top, _windowWidth, WindowHeight, out int width, out int height);
            _backdrop.Effect = null; // RenderTargetBitmap cannot run ps_3_0; same vibrancy math is baked into this diagnostic frame.
            PresentBackdrop(pixels, width, height, width * 4);
            _smallBlur.Radius = 0;
            RenderBackdrop();
        }
        _shapeHover = double.NaN;
    }

    private DockTheme _appliedTheme = (DockTheme)(-1);
    private DockSurface _appliedSurface = (DockSurface)(-1);
    private bool _appliedShrink = GlassSettings.Shrink;

    // brushed dark steel for the graphite card: silver with a cool grey tint and a dimmer highlight
    private static MetalRimEffect GunmetalPreset()
    {
        var effect = MetalRimEffect.ApplyPreset("silver", true);
        effect.TintColor = Color.FromArgb(0x70, 0x8C, 0x93, 0xA3);
        effect.HighlightColor = Color.FromRgb(0xC8, 0xCE, 0xD8);
        return effect;
    }

    // What the surface does to the layers under the icons: how much of the blurred desktop shows, whether the
    // sheen catches light, and how far the shadow reaches. The brushes themselves come from Build*Brush.
    private void ApplySurface()
    {
        // a cyber plate is opaque: graphite's settings leave the desktop out from under it
        var surface = GlassSettings.Cyber ? DockSurface.Graphite : GlassSettings.Tron ? DockSurface.Classic : GlassSettings.Surface;
        // the vibrancy pass saturates the desktop; the calm surfaces let less of it through
        _backdrop.Opacity = surface switch
        {
            DockSurface.Mica => .75,
            DockSurface.Frost => .8,
            DockSurface.Graphite or DockSurface.Floating => 0,
            _ => 1,
        };
        // the clock's white halo is part of the shiny look
        foreach (var text in new[] { _clockTime, _clockDate })
        {
            if (text.Effect is DropShadowEffect halo) halo.Opacity = surface == DockSurface.Classic ? .6 : .15;
        }
        _sheen.Opacity = surface switch
        {
            DockSurface.Classic => 1,
            DockSurface.Frost => .6,
            _ => 0,
        };
        _outerRim.Opacity = surface is DockSurface.Floating || GlassSettings.Cyber ? 0 : 1;
        if (_shadowEffect is not null)
        {
            (_shadowEffect.BlurRadius, _shadowEffect.ShadowDepth, _shadowEffect.Opacity) = surface switch
            {
                DockSurface.Mica => (40.0, 10.0, .28),
                DockSurface.Frost => (32.0, 8.0, .18),
                DockSurface.Graphite => (36.0, 12.0, .42),
                _ => (24.0, 6.0, .35),
            };
        }
        _shadow.Visibility = surface is DockSurface.Floating ? Visibility.Hidden : Visibility.Visible;
    }

    private void DisposeGlass() => GlassSettings.Changed -= OnGlassSettingsChanged;
}
