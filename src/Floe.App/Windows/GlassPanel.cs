using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Floe.App.Effects.Glass;
using Floe.App.Effects.MetalRim;
using Floe.App.Services;
using Floe.Core.Interop;

namespace Floe.App.Windows;

/// <summary>
/// The dock's glass for popups: the live, blurred screen behind it with the dock's vibrancy,
/// tint, sheen and grain, edged with the rim chosen for the dock (metal or a hairline).
/// Children go on top; layers are inserted underneath them.
/// </summary>
public sealed class GlassPanel : Grid
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(GlassPanel), new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsArrange));

    private static readonly string NoFx = Environment.GetEnvironmentVariable("FLOE_NOFX") ?? "";

    /// <summary>Extra shade over the dock's tint: text needs more contrast than icons do.</summary>
    private static readonly Brush ReadingShade = Frozen(new SolidColorBrush(Color.FromArgb(0x8C, 0x08, 0x09, 0x0C)));

    private readonly GlassBackdrop _backdrop = new();
    private readonly Rectangle _tint = new() { Fill = DockWindow.BuildGlassBrush(), IsHitTestVisible = false };
    private readonly Rectangle _shade = new() { Fill = ReadingShade, IsHitTestVisible = false };
    private readonly Rectangle _sheen = new() { Fill = Frozen(DockWindow.BuildSheenBrush()), IsHitTestVisible = false };
    private readonly Rectangle _grain = new() { IsHitTestVisible = false };
    private readonly Rectangle _hairline = new() { Stroke = DockWindow.BuildRimBrush(), StrokeThickness = 1, IsHitTestVisible = false };
    private readonly Border _metalSurface = new() { Background = Brushes.Transparent, IsHitTestVisible = false };
    private readonly RectangleGeometry _clip = new();
    private MetalRimEffect? _metal;

    public GlassPanel()
    {
        _grain.Fill = new ImageBrush(DockWindow.MakeNoiseBitmap())
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 32, 32),
            Stretch = Stretch.None,
        };
        _grain.Opacity = BlurSettings.GrainOpacity;
        _backdrop.Clip = _clip;
        _tint.Clip = _clip;
        _shade.Clip = _clip;
        _sheen.Clip = _clip;
        _grain.Clip = _clip;

        Loaded += (_, _) =>
        {
            Children.Insert(0, _backdrop);
            Children.Insert(1, _tint);
            Children.Insert(2, _shade);
            Children.Insert(3, _sheen);
            Children.Insert(4, _grain);
            Children.Add(_hairline);
            Children.Add(_metalSurface);
            ApplyRim();
            GlassSettings.Changed += OnGlassSettingsChanged;
        };
        Unloaded += (_, _) =>
        {
            GlassSettings.Changed -= OnGlassSettingsChanged;
            foreach (var layer in new UIElement[] { _backdrop, _tint, _shade, _sheen, _grain, _hairline, _metalSurface })
            {
                Children.Remove(layer);
            }
        };
        MouseMove += (_, e) =>
        {
            // the metal's highlight follows the pointer, as it does on the dock
            if (_metal is not null && ActualWidth > 0 && ActualHeight > 0)
            {
                var p = e.GetPosition(this);
                _metal.CursorX = Math.Clamp(p.X / ActualWidth, 0, 1);
                _metal.CursorY = Math.Clamp(p.Y / ActualHeight, 0, 1);
            }
        };
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var size = base.ArrangeOverride(arrangeSize);
        var radius = CornerRadius;
        _clip.Rect = new Rect(size);
        _clip.RadiusX = _clip.RadiusY = radius;
        _hairline.RadiusX = _hairline.RadiusY = radius;
        _metalSurface.CornerRadius = new System.Windows.CornerRadius(radius);
        if (_metal is not null && size.Width > 0 && size.Height > 0)
        {
            _metal.Size = size;
            _metal.CornerRadius = radius;
        }

        return size;
    }

    private void OnGlassSettingsChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(OnGlassSettingsChanged));
            return;
        }

        _tint.Fill = DockWindow.BuildGlassBrush();
        _hairline.Stroke = DockWindow.BuildRimBrush();
        ApplyRim();
    }

    private void ApplyRim()
    {
        _metal = GlassSettings.Rim switch
        {
            _ when GlassSettings.Tron => MetalRimEffect.ApplyPreset("tron", true),
            RimStyle.MetalSilver => MetalRimEffect.ApplyPreset("silver", true),
            RimStyle.MetalGold => MetalRimEffect.ApplyPreset("gold", true),
            RimStyle.MetalChromatic => MetalRimEffect.ApplyPreset("chromatic", true),
            _ => null,
        };
        if (_metal is { IsAvailable: false } || NoFx.Contains("metal") || !RenderCapability.IsPixelShaderVersionSupported(3, 0))
        {
            _metal = null;
        }

        if (_metal is not null)
        {
            // the dock's settings for its rim
            _metal.Thickness = 1.8;
            _metal.GlowGain = 0.45;
            // before the first layout RenderSize is 0;0, which the effect rejects; ArrangeOverride sets it then
            if (RenderSize.Width > 0 && RenderSize.Height > 0) _metal.Size = RenderSize;
            _metal.CornerRadius = CornerRadius;
        }

        _metalSurface.Effect = _metal;
        _metalSurface.Visibility = _metal is null ? Visibility.Collapsed : Visibility.Visible;
        _hairline.Visibility = _metal is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

/// <summary>
/// The screen behind the host window or popup, blurred at quarter resolution and lifted with
/// the dock's vibrancy shader. Live while the host is excluded from capture; with exclusion
/// off (FLOE_NOFX=exclude, for screenshots) a single grab taken as it opens.
/// </summary>
internal sealed class GlassBackdrop : FrameworkElement
{
    private static readonly string NoFx = Environment.GetEnvironmentVariable("FLOE_NOFX") ?? "";

    private readonly Image _small = new() { Stretch = Stretch.Fill };
    private readonly BlurEffect _blur = new() { KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality };
    private readonly Image _image = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private BackdropCapture? _capture;
    private WriteableBitmap? _bitmap;
    private HwndSource? _source;

    public GlassBackdrop()
    {
        IsHitTestVisible = false;
        _small.Effect = _blur;
        // the source is blurred, so linear upscaling is indistinguishable and much cheaper
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.Linear);
        if (RenderCapability.IsPixelShaderVersionSupported(3, 0) && !NoFx.Contains("vibrancy"))
        {
            _image.Effect = new VibrancyEffect();
        }

        AddVisualChild(_image);
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _image;

    protected override Size MeasureOverride(Size availableSize) => default;

    protected override Size ArrangeOverride(Size finalSize)
    {
        // the capture covers the whole host window; line it up with the screen behind
        if (_source?.RootVisual is FrameworkElement root && root.IsAncestorOf(this))
        {
            var origin = TransformToAncestor(root).Transform(default);
            _image.Arrange(new Rect(-origin.X, -origin.Y, root.ActualWidth, root.ActualHeight));
        }
        else
        {
            _image.Arrange(new Rect(finalSize));
        }

        return finalSize;
    }

    private void Attach()
    {
        _source = PresentationSource.FromVisual(this) as HwndSource;
        if (_source is null)
        {
            return;
        }

        if (!NoFx.Contains("exclude"))
        {
            CaptureReveal.Exclude(_source.Handle);
            _capture ??= new BackdropCapture(this, _source.Handle, Present);
        }
        else
        {
            // callers place their window after showing it; grab once that has happened
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(GrabOnce));
        }

        InvalidateArrange();
    }

    private void Detach()
    {
        _capture?.Dispose();
        _capture = null;
        if (_source is not null) CaptureReveal.Forget(_source.Handle);
        _source = null;
    }

    private void GrabOnce()
    {
        if (_source is null || !ScreenInput.TryGetWindowRect(_source.Handle, out var rect))
        {
            return;
        }

        var sourceWidth = rect.Right - rect.Left;
        var sourceHeight = rect.Bottom - rect.Top;
        var width = Math.Max(1, (sourceWidth + 3) / 4);
        var height = Math.Max(1, (sourceHeight + 3) / 4);
        var pixels = ScreenCapture.CaptureScaled(rect.Left, rect.Top, sourceWidth, sourceHeight, width, height, out var stride);
        if (pixels is not null)
        {
            Present(pixels, width, height, stride);
        }
    }

    private void Present(byte[] pixels, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var logicalWidth = (_source?.RootVisual as FrameworkElement)?.ActualWidth ?? ActualWidth;
        if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
        {
            // blur at capture size, then scale the result up (as the dock does)
            _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
            _small.Source = _bitmap;
            _small.Width = width;
            _small.Height = height;
            _blur.Radius = BlurSettings.Radius * width / Math.Max(1, logicalWidth);
            _small.Measure(new Size(width, height));
            _small.Arrange(new Rect(0, 0, width, height));
            var drawing = new GeometryDrawing(
                new VisualBrush(_small) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, width, height) },
                null,
                new RectangleGeometry(new Rect(0, 0, width, height)));
            _image.Source = new DrawingImage(drawing);
            InvalidateArrange();
        }

        _bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
    }
}
