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

    private static BitmapSource MakeNoiseBitmap()
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

    private static Brush BuildGlassBrush()
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

    private static LinearGradientBrush BuildSheenBrush()
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

    private static Brush BuildRimBrush()
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

    private void InitializeGlass()
    {
        _glassPreview = Environment.GetCommandLineArgs().Any(a => a.StartsWith("--render=", StringComparison.OrdinalIgnoreCase));
        Root.Clip = _windowGlassBounds;
        _shadow.Data = _shapeGeometry;
        _shadow.Fill = Brushes.Black;
        _shadow.Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.35, Color = Colors.Black, RenderingBias = RenderingBias.Quality };
        var exterior = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(-4096, -4096, 16384, 8192)), _shapeGeometry);
        _shadowHost.Clip = exterior;
        _shadowHost.Children.Add(_shadow);
        ShapeLayer.Children.Add(_shadowHost);
        _outerRim.Clip = exterior;
        _outerRim.Data = _shapeGeometry;
        _outerRim.Stroke = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0));
        _smallBackdrop.Effect = _smallBlur;
        RenderOptions.SetBitmapScalingMode(_backdrop, BitmapScalingMode.HighQuality);
        if (RenderCapability.IsPixelShaderVersionSupported(3, 0)) _backdrop.Effect = new VibrancyEffect();
        _metalHost.Children.Add(_metalSurface);
        _metalHost.Children.Add(_metalPreview);
        Canvas.SetLeft(_metalSurface, 24);
        Canvas.SetTop(_metalSurface, MaxBulge + 24);
        DpiChanged += (_, _) => UpdateGlassShape();
    }

    private void OnGlassSettingsChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(OnGlassSettingsChanged)); return; }
        ApplyGlassSettings();
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
        if (_metalEffect is { IsAvailable: false } || (!_glassPreview && !RenderCapability.IsPixelShaderVersionSupported(3, 0))) _metalEffect = null;
        _metalHost.Visibility = _metalEffect is null ? Visibility.Collapsed : Visibility.Visible;
        _rim.Visibility = _outerRim.Visibility = _metalEffect is null ? Visibility.Visible : Visibility.Collapsed;
        _metalSurface.Effect = _metalEffect;
        if (_metalEffect is not null)
        {
            _metalEffect.Thickness = 1.8;
            _metalEffect.GlowGain = 0.45;
            _rimWarp ??= new RimWarpEffect();
            _metalHost.Effect = _glassPreview ? null : _rimWarp;
        }
        UpdateGlassShape();
        if (_metalEffect is not null && IsMouseOver) EnsureAnimation();
    }

    private void UpdateGlassShape()
    {
        _windowGlassBounds.Rect = new Rect(0, 0, _windowWidth, WindowHeight);
        // Account for the root's compact/hover scale as well as monitor DPI.
        var pixel = 1 / (VisualTreeHelper.GetDpi(this).DpiScaleX * Math.Max(0.1, _dockScale));
        _rim.StrokeThickness = 2 * pixel; // clipping discards the outer half: a 1px inner hairline
        _outerRim.StrokeThickness = 2 * pixel; // exterior clip discards the inner half
        UpdateVisualizerCapsule();
        if (_metalEffect is null) return;
        var width = Math.Max(1, _capsuleWidth);
        var hover = _currentHoverX - _capsuleLeft;
        var strength = Math.Clamp(_currentBulge / MaxBulge, 0, 1);
        _metalHost.Width = width + 48;
        _metalHost.Height = RestBarHeight + MaxBulge + 48;
        Canvas.SetLeft(_metalHost, _capsuleLeft - 24);
        Canvas.SetTop(_metalHost, RestTop - MaxBulge - 24);
        _metalSurface.Width = width;
        _metalSurface.Height = RestBarHeight;
        _metalEffect.Size = new Size(width, RestBarHeight);
        _metalEffect.CornerRadius = CornerRadius;
        _metalEffect.CursorX = Math.Clamp(hover / width, 0, 1);
        _metalEffect.CursorY = IsMouseOver ? Math.Clamp((Mouse.GetPosition(Root).Y - RestTop) / RestBarHeight, 0, 1) : 0;
        _metalEffect.BendStrength = strength * 0.12;
        _metalEffect.Time = _metalTime;
        var stretch = new Point(MaxSideStretch * Gaussian(0, hover) * strength, MaxSideStretch * Gaussian(width, hover) * strength);
        if (_rimWarp is not null)
        {
            _rimWarp.Size = new Point(width, RestBarHeight);
            _rimWarp.Hover = hover;
            _rimWarp.Bulge = _currentBulge;
            _rimWarp.Stretch = stretch;
        }
        if (_glassPreview)
        {
            _metalSurface.Visibility = Visibility.Collapsed;
            _metalSurface.Effect = null;
            _metalHost.Effect = null;
            using var renderer = new MetalRimPreview.OffscreenRenderer((int)Math.Ceiling(width), (int)RestBarHeight);
            var rawRim = renderer.Render(_metalEffect);
            _metalPreview.Source = GlassPreview.WarpRim(rawRim, width, RestBarHeight, hover, _currentBulge, stretch);
            _metalPreview.Width = _metalHost.Width;
            _metalPreview.Height = _metalHost.Height;
        }
    }

    private void PresentBackdrop(byte[] pixels, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0) return;
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
