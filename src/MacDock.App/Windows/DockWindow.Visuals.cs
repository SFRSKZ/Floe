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
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using MacDock.App.Services;
using MacDock.App.Effects.Glass;
using MacDock.App.Effects.MetalRim;
using MacDock.App.ViewModels;
using MacDock.Core.Interop;
using MacDock.Core.Models;
using MacDock.Core.Services;

namespace MacDock.App.Windows;

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
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF), 0.35));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 1.0));
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
    private bool _glassPreview;
    private RimStyle _appliedRim = (RimStyle)(-1);
    private double _metalTime;
    private double _backdropLogicalWidth;
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

    // Diagnostics: MACDOCK_NOFX=shadow,vibrancy,metal switches single effects off to find render costs.
    private static readonly string NoFx = Environment.GetEnvironmentVariable("MACDOCK_NOFX") ?? "";

    private void InitializeGlass()
    {
        _glassPreview = Environment.GetCommandLineArgs().Any(a => a.StartsWith("--render=", StringComparison.OrdinalIgnoreCase));
        Root.Clip = _windowGlassBounds;
        _shadow.Data = _shapeGeometry;
        _shadow.Fill = Brushes.Black;
        if (!NoFx.Contains("shadow")) _shadow.Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.35, Color = Colors.Black, RenderingBias = RenderingBias.Performance };
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
        if (RenderCapability.IsPixelShaderVersionSupported(3, 0) && !NoFx.Contains("vibrancy")) _backdrop.Effect = new VibrancyEffect();
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
        _shapeHover = double.NaN; // bulge style may have changed
        UpdateShape();
    }

    private void ApplyGlassSettings()
    {
        var style = GlassSettings.Rim;
        if (_glassPreview)
        {
            var argument = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--renderrim=", StringComparison.OrdinalIgnoreCase));
            if (argument is not null && Enum.TryParse<RimStyle>(argument[12..], true, out var previewStyle)) style = previewStyle;
        }
        if (_appliedRim == style) return;
        _appliedRim = style;
        _metalEffect = _appliedRim switch
        {
            RimStyle.MetalSilver => MetalRimEffect.ApplyPreset("silver", true),
            RimStyle.MetalGold => MetalRimEffect.ApplyPreset("gold", true),
            RimStyle.MetalChromatic => MetalRimEffect.ApplyPreset("chromatic", true),
            _ => null,
        };
        if (NoFx.Contains("metal") || _metalEffect is { IsAvailable: false } || (!_glassPreview && !RenderCapability.IsPixelShaderVersionSupported(3, 0))) _metalEffect = null;
        _metalHost.Visibility = _metalEffect is null ? Visibility.Collapsed : Visibility.Visible;
        _rim.Visibility = _outerRim.Visibility = _metalEffect is null ? Visibility.Visible : Visibility.Collapsed;
        _metalSurface.Effect = _metalEffect;
        _metalHost.Effect = null; // UpdateGlassShape swaps the warp back in once there is a bulge to bend
        _metalWarping = false;
        if (_metalEffect is not null)
        {
            _metalEffect.Thickness = 1.8;
            _metalEffect.GlowGain = 0.45;
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
            var drawing = new GeometryDrawing(new VisualBrush(_smallBackdrop) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, width, height) }, null, new RectangleGeometry(new Rect(0, 0, width, height)));
            _backdrop.Source = new DrawingImage(drawing);
        }
        _backdropBitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
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
        }
        _shapeHover = double.NaN;
    }

    private void DisposeGlass() => GlassSettings.Changed -= OnGlassSettingsChanged;
}
