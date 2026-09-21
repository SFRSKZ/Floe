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
using MacDock.App.ViewModels;
using MacDock.Core.Interop;
using MacDock.Core.Models;
using MacDock.Core.Services;

namespace MacDock.App.Windows;

public partial class DockWindow : Window
{
    private const double IconBox = 54;
    private const double Spacing = 8;
    private const double PadX = 14;
    private const double SeparatorExtra = 16;
    private const double PadY = 9;
    private const double ClockWidth = 104;
    private const double ClockGap = 14;
    private const double ClockPad = 12;
    private const double RestBarHeight = 68;
    private const double MaxBulge = 32;
    private const double Headroom = 20;
    private const double WindowHeight = RestBarHeight + MaxBulge + Headroom;
    private const double EdgeMargin = 6;
    private const double MaxSideStretch = 12;
    private const double CompactScale = 0.62;
    private const double HoverMaxScale = 1.4;
    private const double FalloffRadius = 115;
    private const double GlowRadius = 92;
    private const double BulgeSigma = 34;

    private const double CornerRadius = 22;
    private const double BottomMargin = 8;
    private const int TriggerShow = 6;
    private const int TriggerPx = 4;

    private static readonly double RestTop = WindowHeight - RestBarHeight;
    private static readonly double Bottom = WindowHeight;
    private static readonly double IconBottom = WindowHeight - PadY;

    private readonly List<IconSlot> _slots = new();
    private IconSlot? _dragSlot;
    private Point _dragStart;
    private bool _isDragging;
    private readonly Dictionary<string, DockItemViewModel> _pinned = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DockItemViewModel> _pinnedList = new();
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _runningTimer = new() { Interval = TimeSpan.FromMilliseconds(2500) };
    private readonly DispatcherTimer _foregroundTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private readonly DispatcherTimer _foregroundDebounce = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private string _pendingForeground = string.Empty;
    private string _pendingTitle = string.Empty;
    private string _pendingPath = string.Empty;
    private readonly DispatcherTimer _runningDebounce = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly DispatcherTimer _fullscreenTimer = new() { Interval = TimeSpan.FromMilliseconds(1000) };
    private bool _fullscreenHidden;
    private EventHooks.Hook? _foregroundHook;
    private EventHooks.Hook? _windowHook;
    private EventHooks.Hook? _alertHook;
    private DockItemViewModel? _attentionItem;
    private bool _auraAttention;
    private readonly DispatcherTimer _ambientTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private double _ambientLevel;
    private readonly DoubleAnimation _spectralBreathFast = new(0.88, 1.0, TimeSpan.FromSeconds(1.3))
    {
        AutoReverse = true,
        RepeatBehavior = RepeatBehavior.Forever,
    };
    private readonly SolidColorBrush _runningDotBrush = new(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
    private readonly Grid _auraGrid = new();
    private readonly Image[] _auraImages = new Image[3];
    private readonly DropShadowEffect[] _auraEffects = new DropShadowEffect[3];
    private readonly DoubleAnimation[] _auraDirAnims = new DoubleAnimation[3];
    private readonly Canvas _spectralLayer = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Ellipse _spectralHalo = new() { IsHitTestVisible = false };
    private readonly RadialGradientBrush _spectralHaloBrush = new() { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5) };
    private readonly Ellipse _spectralRim = new() { IsHitTestVisible = false };
    private readonly LinearGradientBrush _spectralRimBrush = new() { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
    private readonly Ellipse _spectralWave = new() { IsHitTestVisible = false };
    private readonly LinearGradientBrush _spectralWaveBrush = new() { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
    private readonly RotateTransform _spectralWaveSpin = new(0, 0.5, 0.5);
    private readonly DoubleAnimation _spectralWaveAnim = new(0, 360, TimeSpan.FromSeconds(12))
    {
        RepeatBehavior = RepeatBehavior.Forever,
    };

    private readonly List<(Ellipse Wisp, RotateTransform Spin)> _spectralWisps = new();
    private readonly List<DoubleAnimation> _spectralWispSpins = new();
    private readonly DoubleAnimation _spectralBreath = new(0.74, 1.0, TimeSpan.FromSeconds(3.6))
    {
        AutoReverse = true,
        RepeatBehavior = RepeatBehavior.Forever,
    };

    private readonly DoubleAnimation _spectralDash = new(0, 6, TimeSpan.FromSeconds(2.8))
    {
        RepeatBehavior = RepeatBehavior.Forever,
    };
    private string _activeProcess = string.Empty;
    private string _activeTitle = string.Empty;
    private string _activePath = string.Empty;
    private int _activeIndex = -1;
    private int _separatorIndex = -1;
    private readonly Rectangle _separator = new()
    {
        Width = 2,
        Height = 26,
        Fill = new SolidColorBrush(Color.FromArgb(0x6E, 0xFF, 0xFF, 0xFF)),
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };

    private bool HasSeparator => _separatorIndex > 0 && _separatorIndex < Items.Count;
    private readonly ScaleTransform _rootScale = new(1, 1);
    private bool _renderingHooked;
    private long _lastTick;
    private double _hoverVel;
    private double _bulgeVel;
    private double _dockScale = 1;
    private double _dockScaleVel;
    private double _targetDockScale = CompactScale;
    private bool _menuOpen;
    private bool _settingsOpen;
    private SettingsWindow? _settings;
    private VeilWindow? _veil;
    private DesktopButtonWindow? _desktopButton;
    private readonly DispatcherTimer _tooltipTimer = new() { Interval = TimeSpan.FromMilliseconds(420) };

    private readonly Path _glass = new() { IsHitTestVisible = false };
    private readonly Path _sheen = new() { IsHitTestVisible = false };
    private readonly Path _specular = new() { IsHitTestVisible = false };
    private readonly Rectangle _grain = new() { IsHitTestVisible = false };
    private readonly Path _rim = new() { IsHitTestVisible = false };
    private readonly Ellipse _glow = new() { IsHitTestVisible = false, Width = 220, Height = 86 };

    private readonly Ellipse _dropRing = new()
    {
        IsHitTestVisible = false,
        Width = IconBox + 12,
        Height = IconBox + 12,
        StrokeThickness = 2,
        Stroke = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
        Visibility = Visibility.Collapsed,
    };
    private int _dropIndex = -1;
    private readonly LinearGradientBrush _glowBrush = new() { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
    private readonly List<(double Offset, Color Color)> _glowPairs = new();
    private readonly PathGeometry _shapeGeometry = new();
    private readonly PathFigure _shapeFigure = new() { IsClosed = true, IsFilled = true };
    private readonly PolyLineSegment _shapeSegment = new();
    private readonly List<Point> _shapePoints = new();
    private readonly Canvas _glowLayer = new() { IsHitTestVisible = false };
    private readonly Canvas _clockLayer = new();
    private readonly Image _backdrop = new() { IsHitTestVisible = false, Stretch = Stretch.Fill };
    private WriteableBitmap? _backdropBitmap;
    private BackdropCapture? _backdropCapture;
    private readonly StackPanel _clock = new();
    private readonly TextBlock _clockTime = new();
    private readonly TextBlock _clockDate = new();
    private readonly Rectangle _clockDivider = new();
    private WeatherWidgetWindow? _weatherWidget;
    private MediaWidgetWindow? _mediaWidget;
    private PreviewWindow? _preview;
    private HwndSource? _source;
    private IntPtr _trayIcon;

    private bool _widgetRenderingHooked;
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private Color _currentGlowColor = Colors.White;
    private Color _targetGlowColor = Colors.White;
    private readonly LinearGradientBrush _sheenBrush;

    private readonly Popup _tooltip = new()
    {
        AllowsTransparency = true,
        Placement = PlacementMode.Top,
        StaysOpen = true,
        IsHitTestVisible = false,
        VerticalOffset = -10,
    };
    private readonly TextBlock _tooltipText = new()
    {
        Foreground = Brushes.White,
        FontSize = 12,
        FontFamily = new FontFamily("Segoe UI"),
    };

    private double _windowWidth = 200;
    private double _capsuleWidth = 160;
    private double _capsuleLeft = 20;
    private double _restCapsuleLeft = 20;
    private double _restCapsuleWidth = 20;
    private IntPtr _hwnd;
    private bool _shown = true;
    private int _outsideTicks;
    private double _currentHoverX;
    private double _currentBulge;
    private double _targetHoverX;
    private double _targetBulge;
    private int _hoveredIndex = -1;
    private List<string> _itemKeys = new();

    public bool AutoHide { get; set; }

    public bool BlurBackdrop { get; set; } = true;

    public string DebugInfo
    {
        get
        {
            var bounds = _glass.Data?.Bounds ?? Rect.Empty;
            return $"window={_windowWidth:F0} capsule={_capsuleWidth:F0} left={_capsuleLeft:F0} count={_slots.Count} root={Root.ActualWidth:F0}x{Root.ActualHeight:F0} shape={bounds.X:F0},{bounds.Y:F0} {bounds.Width:F0}x{bounds.Height:F0} stretch={_glass.Stretch}";
        }
    }

    public System.Collections.ObjectModel.ObservableCollection<DockItemViewModel> Items { get; } = new();

    public DockWindow()
    {
        InitializeComponent();

        Root.RenderTransformOrigin = new Point(0.5, 1.0);
        Root.RenderTransform = _rootScale;
        AllowDrop = true;
        Root.AllowDrop = true;
        IconLayer.AllowDrop = true;

        var clockFont = FontService.LoadFromFile(
            @"C:\Users\dev\Documents\Codex\2026-09-09\referenced-chatgpt-conversation-this-is-an\outputs\NothingRoundUI\fonts\Doto-Variable.ttf")
            ?? FontService.LoadFromFile(
            @"C:\Users\dev\Documents\Codex\2026-09-09\referenced-chatgpt-conversation-this-is-an\outputs\NothingRoundUI\fonts\Doto.ttf")
            ?? new FontFamily(FontService.Resolve("Ndot 55", "Ndot-55", "Ndot", "Nothing Font", "NothingFont", "Manrope", "Segoe UI"));

        var clockGlow = new DropShadowEffect { Color = Colors.White, ShadowDepth = 0, BlurRadius = 10, Opacity = 0.6 };

        _clockTime.FontFamily = clockFont;
        _clockTime.FontSize = 26;
        _clockTime.FontWeight = FontWeights.Bold;
        _clockTime.Foreground = Brushes.White;
        _clockTime.TextAlignment = TextAlignment.Center;
        _clockTime.Effect = clockGlow;

        _clockDate.FontFamily = clockFont;
        _clockDate.FontSize = 14;
        _clockDate.FontWeight = FontWeights.SemiBold;
        _clockDate.Foreground = Brushes.White;
        _clockDate.Opacity = 0.9;
        _clockDate.TextAlignment = TextAlignment.Center;
        _clockDate.Margin = new Thickness(0, 2, 0, 0);
        _clockDate.Effect = (DropShadowEffect)clockGlow.Clone();

        _clock.Width = ClockWidth;
        _clock.Children.Add(_clockTime);
        _clock.Children.Add(_clockDate);
        _clock.Cursor = Cursors.Hand;
        _clock.Background = Brushes.Transparent;
        _clock.MouseLeftButtonUp += (_, _) => ToggleWidgets();
        _clock.ToolTip = "Widget'lar";

        _clockDivider.Width = 1;
        _clockDivider.Height = 34;
        _clockDivider.Fill = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
        _clockDivider.RadiusX = 0.5;
        _clockDivider.RadiusY = 0.5;

        _clockLayer.Children.Add(_clock);
        _clockLayer.Children.Add(_clockDivider);
        Root.Children.Add(_clockLayer);

        UpdateClock();
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();

        _foregroundTimer.Tick += (_, _) => UpdateForeground();
        _foregroundTimer.Start();
        _foregroundDebounce.Tick += (_, _) => ApplyForeground();

        _runningDebounce.Tick += (_, _) =>
        {
            _runningDebounce.Stop();
            RefreshRunning();
        };

        _foregroundHook = new EventHooks.Hook(EventHooks.EventSystemForeground, EventHooks.EventSystemForeground, _ => UpdateForeground());
        _windowHook = new EventHooks.Hook(EventHooks.EventObjectCreate, EventHooks.EventObjectDestroy, _ => ScheduleRunningRefresh());
        _alertHook = new EventHooks.Hook(EventHooks.EventSystemAlert, EventHooks.EventSystemAlert, OnSystemAlert);
        _ambientTimer.Tick += (_, _) => UpdateAmbient();
        _dockScale = CompactScale;
        _rootScale.ScaleX = CompactScale;
        _rootScale.ScaleY = CompactScale;

        _glass.Fill = BuildGlassBrush();
        _sheenBrush = BuildSheenBrush();
        _sheen.Fill = _sheenBrush;
        _specular.Stroke = BuildSpecularBrush();
        _specular.StrokeThickness = 1.2;
        _specular.Effect = new BlurEffect { Radius = 1.2, RenderingBias = RenderingBias.Performance };
        _rim.Stroke = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
        _rim.StrokeThickness = 1;
        var mask = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.0),
            GradientOrigin = new Point(0.5, 0.0),
            RadiusX = 0.5,
            RadiusY = 1.0,
        };
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), 0));
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1));
        mask.Freeze();
        _glow.OpacityMask = mask;
        for (var i = 0; i < 14; i++)
        {
            _glowBrush.GradientStops.Add(new GradientStop(Colors.Transparent, i / 13.0));
        }

        _glow.Fill = _glowBrush;
        _glow.Visibility = Visibility.Collapsed;
        _backdrop.Effect = new BlurEffect { Radius = MacDock.App.Services.BlurSettings.Radius, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
        _backdrop.Width = _windowWidth;
        _backdrop.Height = WindowHeight;
        Canvas.SetLeft(_backdrop, 0);
        Canvas.SetTop(_backdrop, 0);
        ShapeLayer.Children.Insert(0, _backdrop);

        _shapeFigure.Segments.Add(_shapeSegment);
        _shapeGeometry.Figures.Add(_shapeFigure);
        _glass.Data = _shapeGeometry;
        _rim.Data = _shapeGeometry;
        _sheen.Data = _shapeGeometry;
        _specular.Data = _shapeGeometry;
        _glowLayer.Clip = _shapeGeometry;
        _backdrop.Clip = _shapeGeometry;

        ShapeLayer.Children.Add(_glass);
        ShapeLayer.Children.Add(_rim);
        ShapeLayer.Children.Add(_sheen);
        ShapeLayer.Children.Add(_specular);

        _grain.Fill = new ImageBrush(MakeNoiseBitmap())
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 64, 64),
            Stretch = Stretch.None,
        };
        _grain.Opacity = MacDock.App.Services.BlurSettings.GrainOpacity;
        _grain.Clip = _shapeGeometry;
        ShapeLayer.Children.Add(_grain);

        _glowLayer.Children.Add(_glow);
        _glowLayer.Children.Add(_dropRing);

        var auraColors = new[]
        {
            Color.FromRgb(0x5B, 0xC8, 0xFF),
            Color.FromRgb(0xA8, 0x55, 0xF7),
            Color.FromRgb(0x34, 0xE0, 0x7A),
        };

        for (var k = 0; k < auraColors.Length; k++)
        {
            var start = k * 120.0;
            var effect = new DropShadowEffect
            {
                Color = auraColors[k],
                ShadowDepth = 9,
                Direction = start,
                BlurRadius = 16,
                Opacity = 0.9,
            };

            _auraEffects[k] = effect;
            _auraDirAnims[k] = new DoubleAnimation(start, start + 360, TimeSpan.FromSeconds(3.4))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            };

            var image = new Image
            {
                Width = IconBox,
                Height = IconBox,
                Stretch = Stretch.Uniform,
                Effect = effect,
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            _auraImages[k] = image;
            _auraGrid.Children.Add(image);
        }

        _auraGrid.Width = IconBox;
        _auraGrid.Height = IconBox;
        _auraGrid.IsHitTestVisible = false;
        _auraGrid.RenderTransformOrigin = new Point(0.5, 1.0);
        _auraGrid.RenderTransform = new ScaleTransform(1, 1);
        _auraGrid.Visibility = Visibility.Collapsed;
        _auraGrid.Opacity = 0.95;
        _glowLayer.Children.Add(_auraGrid);

        Root.Children.Insert(1, _glowLayer);

        _spectralHaloBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.0));
        _spectralHaloBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.62));
        _spectralHaloBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.0));
        _spectralHalo.Width = IconBox * 2.0;
        _spectralHalo.Height = IconBox * 2.0;
        _spectralHalo.Fill = _spectralHaloBrush;
        _spectralHalo.Effect = new BlurEffect { Radius = 12, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
        _spectralLayer.Children.Add(_spectralHalo);

        _spectralRim.Width = IconBox * 1.24;
        _spectralRim.Height = IconBox * 1.24;
        _spectralRim.StrokeThickness = 1.6;
        _spectralRim.Stroke = _spectralRimBrush;
        _spectralRim.StrokeDashArray = new DoubleCollection { 0.8, 1.6 };
        _spectralRim.Effect = new BlurEffect { Radius = 2.2, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
        _spectralLayer.Children.Add(_spectralRim);

        _spectralWave.Width = IconBox * 1.62;
        _spectralWave.Height = IconBox * 1.62;
        _spectralWave.StrokeThickness = 3.2;
        _spectralWave.Stroke = _spectralWaveBrush;
        _spectralWave.Effect = new BlurEffect { Radius = 5, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
        _spectralWaveBrush.RelativeTransform = _spectralWaveSpin;
        _spectralLayer.Children.Add(_spectralWave);

        for (var i = 0; i < 3; i++)
        {
            var wisp = new Ellipse
            {
                Width = 34,
                Height = 8,
                IsHitTestVisible = false,
                RenderTransform = new RotateTransform(i * 120.0, 17, 34),
            };
            wisp.Effect = new BlurEffect { Radius = 6, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
            _spectralWisps.Add((wisp, (RotateTransform)wisp.RenderTransform));
            _spectralWispSpins.Add(new DoubleAnimation(i * 120.0, (i * 120.0) + 360, TimeSpan.FromSeconds(19))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            });
            _spectralLayer.Children.Add(wisp);
        }

        Root.Children.Insert(2, _spectralLayer);



        _tooltip.Child = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD9, 0x1B, 0x1C, 0x22)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 5, 10, 5),
            Child = _tooltipText,
        };

        LoadPinned();

        AuraSettings.Load();
        AuraSettings.Changed += OnAuraSettingsChanged;
        DesktopButtonSettings.Load();
        DesktopButtonSettings.Changed += PositionDesktopButton;

        LocationChanged += (_, _) => PositionDesktopButton();
        _hideTimer.Tick += HideTimer_Tick;
        _runningTimer.Tick += (_, _) => RefreshRunning();
        _tooltipTimer.Tick += (_, _) => ShowTooltip();

        Loaded += (_, _) =>
        {
            UpdateShape();
            Reposition();
            if (AutoHide)
            {
                _hideTimer.Start();
            }

            RefreshRunning();
            _runningTimer.Start();

            if (BlurBackdrop)
            {
                _backdropCapture = new BackdropCapture(this, PresentBackdrop, () => _renderingHooked || IsMouseOver || _menuOpen);
            }

            EnsureWidgets();

            _fullscreenTimer.Tick += (_, _) => UpdateFullscreenState();
            _fullscreenTimer.Start();
            _ambientTimer.Start();

            _trayIcon = TrayNotification.LoadAppIcon(Environment.ProcessPath ?? string.Empty);
            TrayNotification.Add(_hwnd, "MacDock", _trayIcon);
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;
        WindowStyling.MakeNonActivatingToolWindow(_hwnd);
        DesktopController.RegisterHotKey(_hwnd, DesktopController.HotkeyIdVeil, DesktopController.ModControl | DesktopController.ModAlt, DesktopController.VkV);
        if (BlurBackdrop)
        {
            WindowAffinity.ExcludeFromCapture(_hwnd);
        }

        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && wParam.ToInt32() == DesktopController.HotkeyIdVeil)
        {
            ToggleVeil();
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == TrayNotification.CallbackMessage)
        {
            var evt = lParam.ToInt32() & 0xFFFF;
            if (evt == TrayNotification.WmLeftButtonUp)
            {
                ToggleDockVisible();
                handled = true;
            }
            else if (evt == TrayNotification.WmRightButtonUp)
            {
                ShowTrayMenu();
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    private void ToggleVeil()
    {
        _veil ??= new VeilWindow();
        _veil.Toggle();

        if (_veil.IsVisible)
        {
            WindowStyling.BringToTop(_hwnd);
        }
    }

    private void ToggleDockVisible()
    {
        if (IsVisible)
        {
            Hide();
            _weatherWidget?.Hide();
            _mediaWidget?.Hide();
            _desktopButton?.Hide();
        }
        else
        {
            Show();
            Reposition();
            _weatherWidget?.Show();
            _mediaWidget?.Show();
            ArrangeWidgets();
        }
    }

    private void ShowTrayMenu()
    {
        var (x, y) = ScreenInput.GetCursorPosition();
        var actions = new List<(string Label, Action Run)>
        {
            (IsVisible ? "Dock'u gizle" : "Dock'u göster", ToggleDockVisible),
            ("Masaüstünü göster", DesktopController.ShowDesktop),
            (_veil?.IsVisible == true ? "Perde modunu kapat" : "Perde modu (Ctrl+Alt+V)", ToggleVeil),
            (AutoStart.IsEnabled() ? "Açılışta başlatmayı kapat" : "Açılışta başlat", AutoStart.Toggle),
            ("-", () => { }),
            ("Çıkış", () => Application.Current.Shutdown()),
        };

        var index = TrayNotification.ShowMenu(_hwnd, x, y, actions.Select(action => action.Label).ToList());
        if (index >= 0 && index < actions.Count && actions[index].Label != "-")
        {
            actions[index].Run();
        }
    }

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
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x3D, 0xFF, 0xFF, 0xFF), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF), 0.08));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x1C, 0x1A, 0x1C, 0x24), 0.42));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x42, 0x0A, 0x0C, 0x14), 1.0));
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
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.4));
        return brush;
    }

    private static Brush BuildSpecularBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF), 0.35));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 1.0));
        brush.Freeze();
        return brush;
    }

    private void LoadPinned()
    {
        foreach (var app in PinnedAppsProvider.Load())
        {
            var vm = new DockItemViewModel(app);
            _pinnedList.Add(vm);
            _pinned[vm.ProcessName] = vm;
            Items.Add(vm);
        }

        RebuildIcons();
    }

    private void RefreshRunning()
    {
        var self = Process.GetCurrentProcess().Id;
        var windows = AppWindows.Enumerate(self);

        var remaining = windows.Where(window => !IsHelperProcess(window.ProcessName)).ToList();

        foreach (var pinned in _pinnedList)
        {
            var matched = remaining.Where(window => AppMatcher.WindowMatchesItem(pinned.Name, pinned.ProcessName, pinned.App.Target, window)).ToList();
            if (matched.Count > 0)
            {
                pinned.IsRunning = true;
                pinned.Windows = matched.Select(window => window.Hwnd).ToList();
                var consumed = matched.Select(window => window.Hwnd).ToHashSet();
                remaining.RemoveAll(window => consumed.Contains(window.Hwnd));
            }
            else
            {
                pinned.IsRunning = false;
                pinned.Windows = Array.Empty<IntPtr>();
            }
        }

        var grouped = new Dictionary<string, (string Path, List<IntPtr> Windows)>(StringComparer.OrdinalIgnoreCase);
        foreach (var window in remaining)
        {
            if (!grouped.TryGetValue(window.ProcessName, out var entry))
            {
                entry = (window.ProcessPath, new List<IntPtr>());
                grouped[window.ProcessName] = entry;
            }

            entry.Windows.Add(window.Hwnd);
        }

        var unpinned = new List<DockItemViewModel>();
        foreach (var name in grouped.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            var entry = grouped[name];
            var vm = new DockItemViewModel(
                new DockApp { Name = name, Target = entry.Path },
                isPinned: false,
                window: entry.Windows.Count > 0 ? entry.Windows[0] : IntPtr.Zero);

            vm.Windows = entry.Windows;

            if (vm.Icon is not null)
            {
                unpinned.Add(vm);
            }
        }

        var desired = _pinnedList.Select(vm => "P:" + vm.ProcessName)
            .Concat(unpinned.Select(vm => "R:" + vm.ProcessName))
            .ToList();

        if (!desired.SequenceEqual(_itemKeys, StringComparer.OrdinalIgnoreCase))
        {
            _itemKeys = desired;
            Items.Clear();
            foreach (var vm in _pinnedList)
            {
                Items.Add(vm);
            }

            foreach (var vm in unpinned)
            {
                Items.Add(vm);
            }

            RebuildIcons();
        }

        UpdateDots();
    }

    private void RebuildIcons()
    {
        _slots.Clear();
        IconLayer.Children.Clear();
        IconLayer.Children.Add(_separator);

        var count = Items.Count;

        _separatorIndex = -1;
        for (var i = 1; i < count; i++)
        {
            if (!Items[i].IsPinned)
            {
                _separatorIndex = i;
                break;
            }
        }

        var extra = HasSeparator ? SeparatorExtra : 0;

        var restContent = count == 0 ? 0 : (count * IconBox) + ((count - 1) * Spacing) + extra;
        _capsuleWidth = PadX + restContent + ClockGap + ClockWidth + ClockPad;

        var maxContent = count == 0 ? 0 : (count * IconBox * HoverMaxScale) + ((count - 1) * Spacing) + extra;
        _windowWidth = PadX + maxContent + ClockGap + ClockWidth + ClockPad + (2 * EdgeMargin);
        _restCapsuleLeft = (_windowWidth - _capsuleWidth) / 2;
        _restCapsuleWidth = _capsuleWidth;
        _capsuleLeft = _restCapsuleLeft;
        _backdrop.Width = _windowWidth;
        _backdrop.Height = WindowHeight;

        Width = _windowWidth;
        Height = WindowHeight;

        for (var i = 0; i < count; i++)
        {
            var item = Items[i];
            var localExtra = HasSeparator && i >= _separatorIndex ? SeparatorExtra : 0;
            var centerLocal = PadX + (i * (IconBox + Spacing)) + localExtra + (IconBox / 2);

            var image = new Image
            {
                Source = item.Icon,
                Width = IconBox,
                Height = IconBox,
                Stretch = Stretch.Uniform,
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

            var dot = new Ellipse
            {
                Width = 4,
                Height = 4,
                Fill = _runningDotBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 2),
                Visibility = Visibility.Collapsed,
            };

            var grid = new Grid { Width = IconBox, Height = IconBox };
            grid.Children.Add(image);
            grid.Children.Add(dot);

            var scale = new ScaleTransform(1, 1);
            var translate = new TranslateTransform(0, 0);
            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(scale);
            transformGroup.Children.Add(translate);

            var button = new Button
            {
                Width = IconBox,
                Height = IconBox,
                Content = grid,
                DataContext = item,
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Template = CreateButtonTemplate(),
                RenderTransformOrigin = new Point(0.5, 1.0),
                RenderTransform = transformGroup,
            };
            var slot = new IconSlot(button, dot, item, centerLocal, scale, translate);
            _slots.Add(slot);
            IconLayer.Children.Add(button);

            if (item.IsPinned)
            {
                button.PreviewMouseLeftButtonDown += (_, e) => DragStart(slot, e);
                button.PreviewMouseMove += (_, e) => DragMove(e);
                button.PreviewMouseLeftButtonUp += (_, e) => DragEnd(e);
            }
            else
            {
                button.Click += DockIcon_Click;
            }
        }

        var scales = ComputeScales(_currentHoverX);
        UpdateCapsule(scales);
        ApplyLayout(scales);
        UpdateShape();
        UpdateAura();
        PositionDesktopButton();
    }

    private static ControlTemplate CreateButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        return template;
    }

    private double[] ComputeScales(double hoverWindow)
    {
        var count = _slots.Count;
        var result = new double[count];
        if (count == 0)
        {
            return result;
        }

        var falloffs = new double[count];
        var max = 0.0;
        for (var i = 0; i < count; i++)
        {
            var distance = Math.Abs(hoverWindow - (_restCapsuleLeft + _slots[i].CenterLocal));
            falloffs[i] = Falloff(distance, FalloffRadius);
            max = Math.Max(max, falloffs[i]);
        }

        var norm = max > 0.001 ? 1.0 / max : 0.0;
        var intensity = MaxBulge > 0 ? Math.Max(0, Math.Min(1, _currentBulge / MaxBulge)) : 0.0;

        for (var i = 0; i < count; i++)
        {
            result[i] = 1 + ((HoverMaxScale - 1) * Math.Min(1.0, falloffs[i] * norm) * intensity);
        }

        return result;
    }

    private void ApplyLayout(IReadOnlyList<double> scales)
    {
        var count = _slots.Count;
        if (count == 0)
        {
            return;
        }

        var total = Spacing * (count - 1) + (HasSeparator ? SeparatorExtra : 0);
        for (var i = 0; i < count; i++)
        {
            total += IconBox * scales[i];
        }

        var startX = _capsuleLeft + PadX;
        var x = startX;
        var separatorX = double.NaN;
        for (var i = 0; i < count; i++)
        {
            if (HasSeparator && i == _separatorIndex)
            {
                separatorX = x + (SeparatorExtra / 2);
                x += SeparatorExtra;
            }

            var width = IconBox * scales[i];
            var center = x + (width / 2);
            var slot = _slots[i];

            slot.Center = center;
            Canvas.SetLeft(slot.Button, center - (IconBox / 2));
            Canvas.SetTop(slot.Button, IconBottom - IconBox);

            slot.Scale.ScaleX = scales[i];
            slot.Scale.ScaleY = scales[i];

            x += width + Spacing;
        }

        if (HasSeparator && !double.IsNaN(separatorX))
        {
            _separator.Visibility = Visibility.Visible;
            Canvas.SetLeft(_separator, separatorX - (_separator.Width / 2));
            Canvas.SetTop(_separator, IconBottom - (IconBox / 2) - (_separator.Height / 2));
        }
        else
        {
            _separator.Visibility = Visibility.Collapsed;
        }

        var clockLeft = startX + total + ClockGap;
        Canvas.SetLeft(_clockDivider, clockLeft - (ClockGap / 2));
        Canvas.SetTop(_clockDivider, RestTop + ((RestBarHeight - _clockDivider.Height) / 2));
        Canvas.SetLeft(_clock, clockLeft);
        Canvas.SetTop(_clock, RestTop + 6);

        if (_activeIndex >= 0 && _activeIndex < count && _auraGrid.Visibility == Visibility.Visible)
        {
            PositionAura(_slots[_activeIndex]);
        }

        if (_activeIndex >= 0 && _activeIndex < count && _spectralLayer.Visibility == Visibility.Visible)
        {
            PositionSpectral(_slots[_activeIndex]);
        }
    }

    private void UpdateCapsule(IReadOnlyList<double> scales)
    {
        var count = _slots.Count;
        if (count == 0)
        {
            _capsuleWidth = 1;
            _capsuleLeft = (_windowWidth - 1) / 2;
            return;
        }

        var total = Spacing * (count - 1) + (HasSeparator ? SeparatorExtra : 0);
        for (var i = 0; i < count; i++)
        {
            total += IconBox * scales[i];
        }

        _capsuleWidth = PadX + total + ClockGap + ClockWidth + ClockPad;
        _capsuleLeft = (_windowWidth - _capsuleWidth) / 2;
    }

    private void EnsureAnimation()
    {
        if (!_renderingHooked)
        {
            CompositionTarget.Rendering += OnRendering;
            _renderingHooked = true;
            _lastTick = Stopwatch.GetTimestamp();
        }
    }

    private void StopAnimation()
    {
        if (_renderingHooked)
        {
            CompositionTarget.Rendering -= OnRendering;
            _renderingHooked = false;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        var dt = (now - _lastTick) / (double)Stopwatch.Frequency;
        _lastTick = now;
        if (dt <= 0 || dt > 0.1)
        {
            dt = 1.0 / 60.0;
        }

        _currentHoverX = Spring(_currentHoverX, ref _hoverVel, _targetHoverX, dt);
        _currentBulge = Spring(_currentBulge, ref _bulgeVel, _targetBulge, dt);
        _dockScale = Spring(_dockScale, ref _dockScaleVel, _targetDockScale, dt, 180, 24);
        _currentGlowColor = Lerp(_currentGlowColor, _targetGlowColor, Math.Min(1.0, dt * 9));
        _rootScale.ScaleX = _dockScale;
        _rootScale.ScaleY = _dockScale;

        var scales = ComputeScales(_currentHoverX);
        UpdateCapsule(scales);
        ApplyLayout(scales);
        UpdateShape();
        UpdateGlow();
        EnsureWidgetRendering();

        var settled = _targetBulge == 0
            && _currentBulge < 0.3
            && Math.Abs(_currentHoverX - _targetHoverX) < 0.3
            && Math.Abs(_hoverVel) < 4
            && Math.Abs(_bulgeVel) < 4
            && Math.Abs(_dockScale - _targetDockScale) < 0.002
            && Math.Abs(_dockScaleVel) < 0.02
            && scales.All(s => Math.Abs(s - 1) < 0.0015);

        if (settled)
        {
            _currentHoverX = _targetHoverX;
            _currentBulge = 0;
            _hoverVel = 0;
            _bulgeVel = 0;
            _dockScale = _targetDockScale;
            _dockScaleVel = 0;
            _rootScale.ScaleX = _dockScale;
            _rootScale.ScaleY = _dockScale;
            UpdateShape();
            StopAnimation();
        }
    }

    private static double Spring(double position, ref double velocity, double target, double dt, double stiffness = 260, double damping = 27)
    {
        var accel = (stiffness * (target - position)) - (damping * velocity);
        velocity += accel * dt;
        return position + (velocity * dt);
    }

    private static Color Lerp(Color from, Color to, double t)
    {
        t = Math.Max(0, Math.Min(1, t));
        return Color.FromArgb(
            (byte)(from.A + ((to.A - from.A) * t)),
            (byte)(from.R + ((to.R - from.R) * t)),
            (byte)(from.G + ((to.G - from.G) * t)),
            (byte)(from.B + ((to.B - from.B) * t)));
    }

    private double _shapeHover = double.NaN;
    private double _shapeBulge = double.NaN;
    private double _shapeLeft = double.NaN;
    private double _shapeWidth = double.NaN;
    private double _shapeScale = double.NaN;
    private int _shapeCount = -1;

    private void UpdateShape()
    {
        // skip rebuilding the geometry when nothing meaningful changed (per-frame cost)
        if (Math.Abs(_shapeHover - _currentHoverX) < 0.25
            && Math.Abs(_shapeBulge - _currentBulge) < 0.25
            && Math.Abs(_shapeLeft - _capsuleLeft) < 0.25
            && Math.Abs(_shapeWidth - _capsuleWidth) < 0.25
            && Math.Abs(_shapeScale - _dockScale) < 0.002
            && _shapeCount == _slots.Count)
        {
            return;
        }

        _shapeHover = _currentHoverX;
        _shapeBulge = _currentBulge;
        _shapeLeft = _capsuleLeft;
        _shapeWidth = _capsuleWidth;
        _shapeScale = _dockScale;
        _shapeCount = _slots.Count;

        var points = BuildShape();
        if (points.Count >= 2)
        {
            _shapeFigure.StartPoint = points[0];
            var segmentPoints = _shapeSegment.Points;
            segmentPoints.Clear();
            for (var i = 1; i < points.Count; i++)
            {
                segmentPoints.Add(points[i]);
            }
        }

        var ratio = _windowWidth > 0 ? Math.Min(1, Math.Max(0, _currentHoverX) / _windowWidth) : 0.5;
        var sheen = (LinearGradientBrush)_sheen.Fill;
        if (!sheen.IsFrozen)
        {
            sheen.StartPoint = new Point(ratio, 0);
            sheen.EndPoint = new Point(ratio, 1);
        }
    }

    private void UpdateGlow()
    {
        if (_slots.Count == 0 || _currentBulge <= 0.5)
        {
            _glow.Visibility = Visibility.Collapsed;
            return;
        }

        var intensity = Math.Min(1.0, _currentBulge / MaxBulge);
        const double glowWidth = 230;
        var left = _currentHoverX - (glowWidth / 2);

        _glowPairs.Clear();
        _glowPairs.Add((0.0, Colors.Transparent));
        foreach (var slot in _slots)
        {
            var rel = (slot.Center - left) / glowWidth;
            if (rel <= 0.03 || rel >= 0.97)
            {
                continue;
            }

            var weight = Falloff(Math.Abs(slot.Center - _currentHoverX), GlowRadius);
            if (weight <= 0.03)
            {
                continue;
            }

            var color = slot.Item.Glow;
            var alpha = (byte)(0x6E * weight * intensity);
            _glowPairs.Add((rel, Color.FromArgb(alpha, color.R, color.G, color.B)));
        }

        _glowPairs.Add((1.0, Colors.Transparent));
        _glowPairs.Sort((a, b) => a.Offset.CompareTo(b.Offset));

        var stopCount = _glowBrush.GradientStops.Count;
        for (var i = 0; i < stopCount; i++)
        {
            if (i < _glowPairs.Count)
            {
                _glowBrush.GradientStops[i].Offset = _glowPairs[i].Offset;
                _glowBrush.GradientStops[i].Color = _glowPairs[i].Color;
            }
            else
            {
                _glowBrush.GradientStops[i].Offset = 1;
                _glowBrush.GradientStops[i].Color = Colors.Transparent;
            }
        }

        _glow.Width = glowWidth;
        Canvas.SetLeft(_glow, left);
        Canvas.SetTop(_glow, RestTop - _currentBulge - 2);
        _glow.Visibility = Visibility.Visible;
    }

    private List<Point> BuildShape()
    {
        var offset = _capsuleLeft;
        var width = _capsuleWidth;
        var r = CornerRadius;
        _shapePoints.Clear();
        var points = _shapePoints;

        double TopAt(double localX)
            => Math.Max(0, RestTop - (_currentBulge * Gaussian(localX, _currentHoverX - offset)));

        var y0 = TopAt(r);
        var y1 = TopAt(width - r);

        AddArc(points, offset + r, Bottom - r, r, 180, 90, 6);
        points.Add(new Point(offset + width - r, Bottom));
        AddArc(points, offset + width - r, Bottom - r, r, 90, 0, 6);
        points.Add(new Point(offset + width, y1 + r));
        AddArc(points, offset + width - r, y1 + r, r, 0, -90, 6);

        var steps = Math.Max(16, (int)(width / 3));
        for (var i = 0; i <= steps; i++)
        {
            var localX = (width - r) - ((width - (2 * r)) * i / steps);
            points.Add(new Point(offset + localX, TopAt(localX)));
        }

        AddArc(points, offset + r, y0 + r, r, -90, -180, 6);
        points.Add(new Point(offset, Bottom - r));

        ApplySideStretch(points, offset, width);
        return points;
    }

    private void ApplySideStretch(List<Point> points, double offset, double width)
    {
        var strength = MaxBulge > 0 ? Math.Max(0, Math.Min(1, _currentBulge / MaxBulge)) : 0.0;
        if (strength <= 0.001)
        {
            return;
        }

        var hoverLocal = _currentHoverX - offset;
        var leftInf = Gaussian(0, hoverLocal) * strength;
        var rightInf = Gaussian(width, hoverLocal) * strength;
        var mid = offset + (width / 2);

        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var vertical = Smoothstep(Math.Min(1.0, Math.Max(0, (Bottom - p.Y) / RestBarHeight)));
            if (p.X < mid)
            {
                p.X -= MaxSideStretch * leftInf * vertical;
            }
            else
            {
                p.X += MaxSideStretch * rightInf * vertical;
            }

            points[i] = p;
        }
    }

    private static void AddArc(List<Point> points, double cx, double cy, double radius, double startDeg, double endDeg, int steps)
    {
        var step = (endDeg - startDeg) / steps;
        for (var i = 0; i <= steps; i++)
        {
            var angle = (startDeg + (step * i)) * Math.PI / 180.0;
            points.Add(new Point(cx + (radius * Math.Cos(angle)), cy + (radius * Math.Sin(angle))));
        }
    }

    private static double Gaussian(double x, double center)
    {
        var d = x - center;
        return Math.Exp(-(d * d) / (2 * BulgeSigma * BulgeSigma));
    }

    private static double Smoothstep(double t)
    {
        t = Math.Min(1, Math.Max(0, t));
        return t * t * (3 - (2 * t));
    }

    private static double Falloff(double distance, double radius)
    {
        if (distance >= radius)
        {
            return 0;
        }

        return 0.5 * (1 + Math.Cos(Math.PI * (distance / radius)));
    }

    private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            return;
        }

        var position = e.GetPosition(this);
        _targetHoverX = Math.Min(_windowWidth, Math.Max(0, position.X));
        _targetBulge = MaxBulge;
        _targetDockScale = 1;
        EnsureAnimation();
        UpdateHoveredIcon();
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_menuOpen)
        {
            return;
        }

        _targetBulge = 0;
        _targetDockScale = CompactScale;
        _hoveredIndex = -1;
        _tooltipTimer.Stop();
        _tooltip.IsOpen = false;
        HidePreview();
        EnsureAnimation();
    }

    private int FindHoveredIndex()
    {
        var index = -1;
        var best = double.MaxValue;
        for (var i = 0; i < _slots.Count; i++)
        {
            var distance = Math.Abs(_currentHoverX - (_restCapsuleLeft + _slots[i].CenterLocal));
            if (distance < best && distance <= IconBox)
            {
                best = distance;
                index = i;
            }
        }

        return index;
    }

    private void UpdateHoveredIcon()
    {
        var index = FindHoveredIndex();
        if (index == _hoveredIndex)
        {
            return;
        }

        _hoveredIndex = index;
        _tooltip.IsOpen = false;
        HidePreview();
        _tooltipTimer.Stop();

        if (index >= 0 && _radial?.IsVisible != true)
        {
            _tooltipTimer.Start();
        }
    }

    private void HidePreview()
    {
        _preview?.Close3();
    }

    private void ShowPreview(IconSlot slot)
    {
        _preview ??= new PreviewWindow();
        _preview.Show();
        _preview.ShowFor(slot.Item.Windows);

        var left = ToScreenX(slot.Center) - (_preview.Width / 2);
        left = Math.Max(8, Math.Min(left, SystemParameters.PrimaryScreenWidth - _preview.Width - 8));
        _preview.Left = left;
        _preview.Top = Top + RestTop - _preview.Height - 10;
    }

    private void ShowTooltip()
    {
        _tooltipTimer.Stop();
        if (_hoveredIndex < 0 || _hoveredIndex >= _slots.Count || _radial?.IsVisible == true)
        {
            return;
        }

        var slot = _slots[_hoveredIndex];

        if (slot.Item.Windows.Count > 0)
        {
            ShowPreview(slot);
            return;
        }

        _tooltipText.Text = slot.Item.Name;
        _tooltip.PlacementTarget = slot.Button;
        _tooltip.IsOpen = true;

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (_tooltip.Child is FrameworkElement child)
            {
                _tooltip.HorizontalOffset = -(child.ActualWidth - slot.Button.ActualWidth) / 2;
            }
        });
    }

    public void PreviewHover(double capsuleX)
    {
        _dockScale = _targetDockScale = 1;
        _rootScale.ScaleX = 1;
        _rootScale.ScaleY = 1;
        _currentHoverX = _targetHoverX = _restCapsuleLeft + Math.Min(_capsuleWidth, Math.Max(0, capsuleX));
        _currentBulge = _targetBulge = MaxBulge;
        var scales = ComputeScales(_currentHoverX);
        UpdateCapsule(scales);
        ApplyLayout(scales);
        UpdateShape();
        _hoveredIndex = FindHoveredIndex();
        if (_hoveredIndex >= 0)
        {
            _currentGlowColor = _targetGlowColor = _slots[_hoveredIndex].Item.Glow;
        }

        UpdateGlow();
    }

    public void PreviewAura(string processName)
    {
        _activeProcess = processName;
        UpdateAura();
        Log($"preview aura idx={_activeIndex} effect={AuraSettings.Effect}");
    }

    private void DockIcon_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DockItemViewModel item)
        {
            Log("click (no item)");
            return;
        }

        ActivateItem(item);
    }

    private void ActivateItem(DockItemViewModel item)
    {
        // group: cycle through the windows of the same app
        if (item.Windows.Count > 0)
        {
            var hwnd = item.NextWindow();
            var activated = WindowActivator.Activate(hwnd);

            // elevated windows (e.g. Task Manager) can't be restored by a non-elevated process;
            // relaunching a single-instance app brings its existing window back
            if (!activated || ScreenInput.IsWindowMinimized(hwnd))
            {
                var relaunch = AppLauncher.LaunchFresh(item.App);
                Log($"click {item.Name} -> activate failed (elevated?), relaunch: {relaunch.Message}");
                return;
            }

            Log($"click {item.Name} -> cycle window {hwnd} ok={activated} count={item.Windows.Count}");
            return;
        }

        var result = AppLauncher.LaunchOrActivate(item.App);
        Log($"click {item.Name} -> {result.Message}");

        if (result.Started && _slots.FirstOrDefault(s => ReferenceEquals(s.Item, item)) is { } slot)
        {
            Bounce(slot);
        }
    }

    private void Bounce(IconSlot slot)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(700),
        };
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(-IconBox * 0.42, KeyTime.FromPercent(0.28)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0.6)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(-IconBox * 0.2, KeyTime.FromPercent(0.78)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1.0)));
        slot.Translate.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    private void DragStart(IconSlot slot, MouseButtonEventArgs e)
    {
        if (!slot.Item.IsPinned)
        {
            return;
        }

        _dragSlot = slot;
        _dragStart = e.GetPosition(this);
        _isDragging = false;
        Log($"dragStart {slot.Item.Name}");
        _tooltip.IsOpen = false;
        slot.Button.CaptureMouse();
    }

    private void DragMove(MouseEventArgs e)
    {
        if (_dragSlot is null)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (!_isDragging)
        {
            if (Math.Abs(position.X - _dragStart.X) < 6 && Math.Abs(position.Y - _dragStart.Y) < 6)
            {
                return;
            }

            _isDragging = true;
        }

        e.Handled = true;
        Canvas.SetLeft(_dragSlot.Button, position.X - (IconBox / 2));
        Canvas.SetZIndex(_dragSlot.Button, 1000);
    }

    private void DragEnd(MouseButtonEventArgs e)
    {
        if (_dragSlot is null)
        {
            return;
        }

        var slot = _dragSlot;
        _dragSlot = null;
        slot.Button.ReleaseMouseCapture();
        Canvas.SetZIndex(slot.Button, 0);

        if (!_isDragging)
        {
            ActivateItem(slot.Item);
            return;
        }

        _isDragging = false;
        var position = e.GetPosition(this);

        if (position.Y > IconBottom + (IconBox * 0.5))
        {
            Unpin(slot.Item);
            return;
        }

        var current = _pinnedList.IndexOf(slot.Item);
        var target = ComputeDropIndex(position.X);
        if (current >= 0 && target != current && target != current + 1)
        {
            _pinnedList.RemoveAt(current);
            if (target > current)
            {
                target--;
            }

            target = Math.Clamp(target, 0, _pinnedList.Count);
            _pinnedList.Insert(target, slot.Item);
            SavePinned();
        }

        RefreshRunning();
    }

    private int ComputeDropIndex(double x)
    {
        var index = 0;
        var pinnedCount = Math.Min(_pinnedList.Count, _slots.Count);
        for (var i = 0; i < pinnedCount; i++)
        {
            if (x > _restCapsuleLeft + _slots[i].CenterLocal)
            {
                index = i + 1;
            }
        }

        return index;
    }

    private void ApplyMenuStyle(ContextMenu menu)
    {
        menu.Style = (Style)FindResource("GlassMenu");
        menu.Resources[typeof(MenuItem)] = (Style)FindResource("GlassMenuItem");
    }

    private ContextMenu BuildBackgroundMenu()
    {
        var menu = new ContextMenu();
        ApplyMenuStyle(menu);
        menu.Items.Add(MenuAction("Masaüstünü göster", DesktopController.ShowDesktop));
        menu.Items.Add(MenuAction("Perde modu (Ctrl+Alt+V)", ToggleVeil));
        menu.Items.Add(MenuAction("Görev Yöneticisi", () => StartSafe("taskmgr.exe")));
        menu.Items.Add(MenuAction(AutoStart.IsEnabled() ? "Açılışta başlatmayı kapat" : "Açılışta başlat", AutoStart.Toggle));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuAction("MacDock'u kapat", () => Application.Current.Shutdown()));
        return menu;
    }

    private ContextMenu BuildItemMenu(DockItemViewModel item)
    {
        var menu = new ContextMenu();
        ApplyMenuStyle(menu);
        menu.Items.Add(MenuAction("Aç", () => AppLauncher.LaunchOrActivate(item.App)));
        menu.Items.Add(MenuAction(item.IsPinned ? "Dock'tan kaldır" : "Dock'a sabitle", () =>
        {
            if (item.IsPinned)
            {
                Unpin(item);
            }
            else
            {
                Pin(item);
            }
        }));
        menu.Items.Add(MenuAction("Dosya konumunu aç", () => OpenLocation(item.App)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuAction("Pencereyi kapat", () => CloseApp(item)));

        var target = AppLauncher.ResolveTarget(item.App);
        var verbs = ShellVerbs.Get(target);
        if (verbs.Count > 0)
        {
            menu.Items.Add(new Separator());
            foreach (var verb in verbs)
            {
                menu.Items.Add(MenuAction(verb.Name, verb.Invoke));
            }
        }

        return menu;
    }

    private RadialMenuWindow? _radial;

    private void OpenRadialMenu(IconSlot slot)
    {
        var item = slot.Item;
        var actions = new List<(string Label, Action Run)>
        {
            ("Aç", () => AppLauncher.LaunchOrActivate(item.App)),
            (item.IsPinned ? "Kaldır" : "Sabitle", () =>
            {
                if (item.IsPinned)
                {
                    Unpin(item);
                }
                else
                {
                    Pin(item);
                }
            }),
            ("Konum", () => OpenLocation(item.App)),
            ("Kapat", () => CloseApp(item)),
        };

        HidePreview();
        _tooltipTimer.Stop();
        _tooltip.IsOpen = false;

        if (_radial is null)
        {
            _radial = new RadialMenuWindow();
            _radial.Dismissed += () => _menuOpen = false;
        }

        _menuOpen = true;

        var radius = RadialMenuWindow.Radius;
        var hit = RadialMenuWindow.HitRadius + 16;
        var centerX = Math.Clamp(ToScreenX(slot.Center), hit, SystemParameters.PrimaryScreenWidth - hit);
        var centerY = Math.Clamp(Top + IconBottom - (IconBox / 2), hit, SystemParameters.PrimaryScreenHeight - hit);
        _radial.Left = centerX - radius;
        _radial.Top = centerY - radius;
        _radial.ShowFor(item.Icon, actions);
    }

    private static MenuItem MenuAction(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void Pin(DockItemViewModel item)
    {
        if (item.IsPinned)
        {
            return;
        }

        var pinned = new DockItemViewModel(item.App, isPinned: true);
        _pinnedList.Add(pinned);
        _pinned[pinned.ProcessName] = pinned;
        SavePinned();
        RefreshRunning();
    }

    private void Unpin(DockItemViewModel item)
    {
        var target = _pinnedList.FirstOrDefault(v =>
            string.Equals(v.ProcessName, item.ProcessName, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return;
        }

        _pinnedList.Remove(target);
        _pinned.Remove(target.ProcessName);
        SavePinned();
        RefreshRunning();
    }

    private void SavePinned()
        => PinnedAppsProvider.Save(PinnedAppsProvider.DefaultConfigPath, _pinnedList.Select(v => v.App).ToList());

    private static void OpenLocation(DockApp app)
    {
        var target = AppLauncher.ResolveTarget(app);
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        try
        {
            Process.Start("explorer.exe", System.IO.File.Exists(target) ? $"/select,\"{target}\"" : $"\"{target}\"");
        }
        catch
        {
            // ignore
        }
    }

    private static void CloseApp(DockItemViewModel item)
    {
        if (string.IsNullOrWhiteSpace(item.ProcessName))
        {
            return;
        }

        foreach (var process in Process.GetProcessesByName(item.ProcessName))
        {
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    process.CloseMainWindow();
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    private static void StartSafe(string fileName)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = fileName, UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
    }

    private void UpdateDots()
    {
        foreach (var slot in _slots)
        {
            slot.Dot.Visibility = slot.Item.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static bool IsHelperProcess(string name)
    {
        var value = name.ToLowerInvariant();
        return value is "macdock" or "conhost" or "dwm" or "taskhostw"
            || value.Contains("helper")
            || value.Contains("crashpad")
            || value.Contains("runtimebroker")
            || value.Contains("textinputhost")
            || value.Contains("searchhost")
            || value.Contains("startmenuexperiencehost")
            || value.Contains("shellexperiencehost")
            || value.Contains("widgetservice")
            || value.Contains("webview");
    }

    private void HideTimer_Tick(object? sender, EventArgs e)
    {
        if (!AutoHide || _menuOpen || _settingsOpen || _hwnd == IntPtr.Zero)
        {
            return;
        }

        if (!ScreenInput.TryGetWindowRect(_hwnd, out var rect))
        {
            return;
        }

        var (cursorX, cursorY) = ScreenInput.GetCursorPosition();
        var screenHeight = ScreenInput.PrimaryScreenHeight;

        var physicalWidth = rect.Right - rect.Left;
        var pxPerDip = _windowWidth > 0 ? physicalWidth / _windowWidth : 1;
        var centerDip = _capsuleLeft + (_capsuleWidth / 2);
        var halfDip = (_capsuleWidth / 2) * _dockScale;
        var left = rect.Left + ((centerDip - halfDip) * pxPerDip);
        var right = rect.Left + ((centerDip + halfDip) * pxPerDip);

        if (!_shown)
        {
            var inTrigger = cursorY >= screenHeight - TriggerPx && cursorX >= left - 4 && cursorX <= right + 4;
            if (inTrigger)
            {
                ShowDock();
            }

            return;
        }

        var inside = cursorX >= left - 12 && cursorX <= right + 12 && cursorY >= rect.Top - 12 && cursorY <= rect.Bottom + 12;
        if (inside)
        {
            _outsideTicks = 0;
            return;
        }

        _outsideTicks++;
        if (_outsideTicks >= 5)
        {
            HideDock();
        }
    }

    private void ShowDock()
    {
        if (_shown)
        {
            return;
        }

        _shown = true;
        _outsideTicks = 0;
        AnimateTop(ShownTop);
        PositionDesktopButton();
    }

    private void HideDock()
    {
        if (!_shown)
        {
            return;
        }

        _shown = false;
        AnimateTop(HiddenTop);
        PositionDesktopButton();
    }

    private void AnimateTop(double target)
    {
        var animation = new System.Windows.Media.Animation.DoubleAnimation(target, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
        };
        BeginAnimation(TopProperty, animation);
    }

    private double ShownTop => SystemParameters.PrimaryScreenHeight - Height - BottomMargin;

    private double HiddenTop => SystemParameters.PrimaryScreenHeight - TriggerShow - RestTop;

    private bool CursorOverDock()
    {
        if (_hwnd == IntPtr.Zero || !ScreenInput.TryGetWindowRect(_hwnd, out var rect))
        {
            return false;
        }

        var physicalWidth = rect.Right - rect.Left;
        var pxPerDip = _windowWidth > 0 ? physicalWidth / _windowWidth : 1;
        var centerDip = _capsuleLeft + (_capsuleWidth / 2);
        var halfDip = (_capsuleWidth / 2) * _dockScale;
        var left = rect.Left + ((centerDip - halfDip) * pxPerDip);
        var right = rect.Left + ((centerDip + halfDip) * pxPerDip);

        // include the desktop button (separate window) to the right
        right += 100 * pxPerDip;

        var (x, y) = ScreenInput.GetCursorPosition();
        return x >= left - 6 && x <= right + 6 && y >= rect.Top - 10 && y <= rect.Bottom + 6;
    }

    private void Reposition()
    {
        Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
        Top = _shown ? ShownTop : HiddenTop;
        PositionWidgets();
        PositionDesktopButton();
    }

    private double _desktopButtonX = double.NaN;
    private double _desktopButtonY = double.NaN;

    private void PositionDesktopButton()
    {
        _desktopButton ??= CreateDesktopButton();

        // use the rest capsule width so it does not have to follow hover magnification
        var right = ToScreenX(_restCapsuleLeft + _restCapsuleWidth) + 16;
        var centerY = Top + RestTop + (RestBarHeight / 2);
        var top = centerY - (_desktopButton.Height / 2);

        if (double.IsNaN(_desktopButtonX)
            || double.IsNaN(_desktopButtonY)
            || Math.Abs(_desktopButtonX - right) > 0.5
            || Math.Abs(_desktopButtonY - top) > 0.5)
        {
            _desktopButton.Left = right;
            _desktopButton.Top = top;
            _desktopButtonX = right;
            _desktopButtonY = top;
        }

        if (_shown && !_fullscreenHidden && IsVisible)
        {
            if (!_desktopButton.IsVisible)
            {
                _desktopButton.Show();
            }
        }
        else if (_desktopButton.IsVisible)
        {
            _desktopButton.Hide();
        }
    }

    private DesktopButtonWindow CreateDesktopButton()
    {
        var button = new DesktopButtonWindow();
        button.Clicked += DesktopController.ShowDesktop;
        button.Show();
        button.Hide();
        return button;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Window_PreviewMouseMove(this, e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Window_MouseLeave(this, e);
    }

    protected override void OnClosed(EventArgs e)
    {
        StopAnimation();
        StopWidgetRendering();
        AuraSettings.Changed -= OnAuraSettingsChanged;
        DesktopButtonSettings.Changed -= PositionDesktopButton;
        _settings?.Close();
        for (var k = 0; k < _auraEffects.Length; k++)
        {
            _auraEffects[k].BeginAnimation(DropShadowEffect.DirectionProperty, null);
        }

        for (var i = 0; i < _spectralWisps.Count; i++)
        {
            _spectralWisps[i].Spin.BeginAnimation(RotateTransform.AngleProperty, null);
        }

        _spectralWaveSpin.BeginAnimation(RotateTransform.AngleProperty, null);
        _spectralRim.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
        _spectralLayer.BeginAnimation(OpacityProperty, null);

        DesktopController.UnregisterHotKey(_hwnd, DesktopController.HotkeyIdVeil);
        _veil?.HideVeil();
        _desktopButton?.Close();
        _foregroundHook?.Dispose();
        _windowHook?.Dispose();
        _alertHook?.Dispose();
        _ambientTimer.Stop();
        _backdropCapture?.Dispose();
        TrayNotification.Remove(_hwnd, "MacDock", _trayIcon);
        if (_trayIcon != IntPtr.Zero)
        {
            TrayNotification.DestroyAppIcon(_trayIcon);
        }

        _source?.RemoveHook(WndProc);
        base.OnClosed(e);
    }

    private void OnWidgetRendering(object? sender, EventArgs e)
    {
        if (!UpdateWidgetPositions())
        {
            StopWidgetRendering();
        }
    }

    private void EnsureWidgetRendering()
    {
        if (!_widgetRenderingHooked)
        {
            _widgetRenderingHooked = true;
            CompositionTarget.Rendering += OnWidgetRendering;
        }
    }

    private void StopWidgetRendering()
    {
        if (_widgetRenderingHooked)
        {
            _widgetRenderingHooked = false;
            CompositionTarget.Rendering -= OnWidgetRendering;
        }
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);

        if (_clock.IsMouseOver || _clockDivider.IsMouseOver)
        {
            if (e.Delta > 0)
            {
                DesktopController.VolumeUp();
            }
            else
            {
                DesktopController.VolumeDown();
            }

            e.Handled = true;
            return;
        }

        var slot = _slots.FirstOrDefault(s => s.Button.IsMouseOver);
        if (slot is { Item.Windows.Count: > 0 })
        {
            var hwnd = slot.Item.NextWindow();
            if (e.Delta < 0)
            {
                hwnd = slot.Item.NextWindow();
            }

            WindowActivator.Activate(hwnd);
            e.Handled = true;
        }
    }

    protected override void OnPreviewMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseRightButtonUp(e);

        if (_clock.IsMouseOver || _clockDivider.IsMouseOver)
        {
            OpenSettings();
            e.Handled = true;
            return;
        }

        var slot = _slots.FirstOrDefault(s => s.Button.IsMouseOver);
        if (slot is not null)
        {
            OpenRadialMenu(slot);
            e.Handled = true;
            return;
        }

        _menuOpen = true;
        _targetBulge = MaxBulge;
        _targetDockScale = 1;
        EnsureAnimation();

        var menu = BuildBackgroundMenu();
        var previousForeground = ScreenInput.GetForegroundHandle();
        WindowStyling.SetNoActivate(_hwnd, false);
        Activate();

        menu.Closed += (_, _) =>
        {
            WindowStyling.SetNoActivate(_hwnd, true);
            _menuOpen = false;

            if (previousForeground != IntPtr.Zero)
            {
                WindowActivator.Activate(previousForeground);
            }

            if (!CursorOverDock())
            {
                _targetBulge = 0;
                _targetDockScale = CompactScale;
            }

            EnsureAnimation();
        };

        if (slot is not null)
        {
            menu.Placement = PlacementMode.Top;
            menu.PlacementTarget = slot.Button;
            menu.VerticalOffset = -8;
        }
        else
        {
            menu.Placement = PlacementMode.MousePoint;
        }

        menu.IsOpen = true;

        if (slot is not null)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (menu.IsOpen)
                {
                    menu.HorizontalOffset = -(menu.ActualWidth - slot.Button.ActualWidth) / 2;
                }
            });
        }

        e.Handled = true;
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        e.Effects = PreferredEffect(e);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);

        e.Effects = PreferredEffect(e);
        if (e.Effects != DragDropEffects.None)
        {
            _targetBulge = MaxBulge;
            _targetDockScale = 1;
            EnsureAnimation();

            var index = FindIndexAt(e.GetPosition(this).X);
            if (index != _dropIndex)
            {
                _dropIndex = index;
                UpdateDropRing();
            }
        }

        e.Handled = true;
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        _dropIndex = -1;
        _dropRing.Visibility = Visibility.Collapsed;
    }

    private int FindIndexAt(double windowX)
    {
        var index = -1;
        var best = double.MaxValue;
        for (var i = 0; i < _slots.Count; i++)
        {
            var distance = Math.Abs(windowX - (_restCapsuleLeft + _slots[i].CenterLocal));
            if (distance < best && distance <= IconBox)
            {
                best = distance;
                index = i;
            }
        }

        return index;
    }

    private void UpdateDropRing()
    {
        if (_dropIndex < 0 || _dropIndex >= _slots.Count)
        {
            _dropRing.Visibility = Visibility.Collapsed;
            return;
        }

        var slot = _slots[_dropIndex];
        Canvas.SetLeft(_dropRing, slot.Center - (_dropRing.Width / 2));
        Canvas.SetTop(_dropRing, (IconBottom - (IconBox / 2)) - (_dropRing.Height / 2));
        _dropRing.Visibility = Visibility.Visible;
    }

    private static DragDropEffects PreferredEffect(DragEventArgs e)
    {
        if (!HasDroppableData(e.Data))
        {
            return DragDropEffects.None;
        }

        var allowed = e.AllowedEffects & (DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        if (allowed == DragDropEffects.None)
        {
            return DragDropEffects.None;
        }

        return (allowed & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : allowed;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        e.Handled = true;

        var paths = new List<string>();

        if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] fileDrop)
        {
            paths.AddRange(fileDrop);
        }

        if (paths.Count == 0)
        {
            var raw = e.Data.GetData("Shell IDList Array");
            var bytes = raw switch
            {
                byte[] b => b,
                System.IO.MemoryStream ms => ms.ToArray(),
                _ => null,
            };

            if (bytes is not null)
            {
                paths.AddRange(ShellDropHelper.ParseIdListArray(bytes));
            }
        }

        Log($"drop: count={paths.Count} index={_dropIndex} formats={string.Join(",", e.Data.GetFormats())}");

        var dropItem = _dropIndex >= 0 && _dropIndex < _slots.Count ? _slots[_dropIndex].Item : null;
        _dropIndex = -1;
        _dropRing.Visibility = Visibility.Collapsed;

        // dropped on a specific icon: open the files with that app
        if (dropItem is not null && paths.Count > 0)
        {
            var target = AppLauncher.ResolveTarget(dropItem.App);
            if (!string.IsNullOrWhiteSpace(target) && !target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var path in paths)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo { FileName = target, Arguments = $"\"{path}\"", UseShellExecute = true });
                    }
                    catch
                    {
                        // ignore individual failures
                    }
                }

                return;
            }
        }

        foreach (var path in paths)
        {
            PinPath(path);
        }
    }

    private static string DeriveAumidName(string aumid)
    {
        var core = aumid.Split('!')[0];
        var parts = core.Split('.');
        var last = parts.Length > 0 ? parts[^1] : core;
        var underscore = last.IndexOf('_');
        if (underscore > 0)
        {
            last = last[..underscore];
        }

        return string.IsNullOrWhiteSpace(last) ? aumid : last;
    }

    private static bool HasDroppableData(System.Windows.IDataObject data)
        => data.GetDataPresent(DataFormats.FileDrop)
           || data.GetDataPresent("Shell IDList Array")
           || data.GetDataPresent("FileNameW");

    private void PinPath(string path)
    {
        DockApp? app = null;
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();

        if (ext == ".lnk")
        {
            app = ShortcutReader.Read(path);

            // Store apps resolve to a protected WindowsApps exe that cannot be launched/iconed
            // directly; use the shortcut itself instead.
            var resolved = app?.Target ?? string.Empty;
            if (app is null
                || string.IsNullOrWhiteSpace(resolved)
                || resolved.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase))
            {
                app = new DockApp { Name = System.IO.Path.GetFileNameWithoutExtension(path), Target = path, IconPath = path };
            }
        }
        else if (System.IO.File.Exists(path))
        {
            app = new DockApp
            {
                Name = System.IO.Path.GetFileNameWithoutExtension(path),
                Target = path,
                IconPath = path,
            };
        }
        else if (path.Length > 0 && !path.Contains('\\') && !path.Contains('/') && !path.Contains(':'))
        {
            // Start menu / UWP drag yields an AppUserModelID
            var shellTarget = "shell:AppsFolder\\" + path;
            app = new DockApp
            {
                Name = DeriveAumidName(path),
                Target = shellTarget,
                IconPath = shellTarget,
            };
        }

        Log($"pinpath '{path}' target='{app?.Target}'");

        if (app is null || string.IsNullOrWhiteSpace(app.Target))
        {
            return;
        }

        var vm = new DockItemViewModel(app, isPinned: true);
        if (string.IsNullOrWhiteSpace(vm.ProcessName) || _pinned.ContainsKey(vm.ProcessName))
        {
            Log($"pinpath skipped proc='{vm.ProcessName}'");
            return;
        }

        _pinnedList.Add(vm);
        _pinned[vm.ProcessName] = vm;
        SavePinned();
        RefreshRunning();
    }

    private void EnsureWidgets()
    {
        if (_weatherWidget is not null)
        {
            return;
        }

        try
        {
            _weatherWidget = new WeatherWidgetWindow();
            _mediaWidget = new MediaWidgetWindow();
            _weatherWidget.Show();
            _mediaWidget.Show();
            PositionWidgets();
        }
        catch (Exception ex)
        {
            Log($"widgets error: {ex.GetType().Name} {ex.Message}");
        }
    }

    private void ToggleWidgets()
    {
        EnsureWidgets();

        if (_weatherWidget!.IsVisible)
        {
            _weatherWidget.Hide();
            _mediaWidget!.Hide();
            return;
        }

        _weatherWidget.Show();
        _mediaWidget!.Show();
        PositionWidgets();
    }

    private void PositionWidgets() => ArrangeWidgets();

    private void ArrangeWidgets()
    {
        if (_weatherWidget is null || _mediaWidget is null)
        {
            return;
        }

        var geometry = WidgetGeometry();
        const double gap = 12;

        SnapWidget(_weatherWidget, "weather", true, geometry, gap);
        SnapWidget(_mediaWidget, "media", true, geometry, gap);
    }

    private (double Bottom, double CapLeft, double CapRight, double RestLeft, double RestRight) WidgetGeometry()
    {
        var capLeft = Left + _capsuleLeft;
        var capRight = capLeft + _capsuleWidth;
        var restLeft = Left + _restCapsuleLeft;
        return (Top + WindowHeight, capLeft, capRight, restLeft, restLeft + _restCapsuleWidth);
    }

    private static double TargetLeft(string id, bool defaultRight, double width, (double Bottom, double CapLeft, double CapRight, double RestLeft, double RestRight) geometry, double gap)
    {
        var saved = WidgetLayout.Get(id);
        var baseLeft = saved is { } s
            ? s.Left
            : (defaultRight ? geometry.RestRight + gap : geometry.RestLeft - gap - width);

        var restCenter = (geometry.RestLeft + geometry.RestRight) / 2;
        if ((baseLeft + (width / 2)) >= restCenter)
        {
            var expansion = Math.Max(0, geometry.CapRight - geometry.RestRight);
            var overlap = Math.Max(0, (geometry.CapRight + gap) - baseLeft);
            return baseLeft + Math.Min(overlap, expansion);
        }

        var expansionLeft = Math.Max(0, geometry.RestLeft - geometry.CapLeft);
        var overlapLeft = Math.Max(0, (baseLeft + width) - (geometry.CapLeft - gap));
        return baseLeft - Math.Min(overlapLeft, expansionLeft);
    }

    private static void SnapWidget(Window widget, string id, bool defaultRight, (double Bottom, double CapLeft, double CapRight, double RestLeft, double RestRight) geometry, double gap)
    {
        if (!widget.IsVisible)
        {
            return;
        }

        if (widget.ActualWidth <= 0) { widget.UpdateLayout(); }
        widget.Left = TargetLeft(id, defaultRight, widget.ActualWidth, geometry, gap);
        widget.Top = geometry.Bottom - widget.ActualHeight;
    }

    private bool UpdateWidgetPositions()
    {
        if (_weatherWidget is null || _mediaWidget is null)
        {
            return false;
        }

        var geometry = WidgetGeometry();
        const double gap = 12;

        var moved = SpringWidget(_weatherWidget, "weather", true, geometry, gap);
        moved |= SpringWidget(_mediaWidget, "media", true, geometry, gap);
        return moved;
    }

    private static bool SpringWidget(Window widget, string id, bool defaultRight, (double Bottom, double CapLeft, double CapRight, double RestLeft, double RestRight) geometry, double gap)
    {
        if (!widget.IsVisible)
        {
            return false;
        }

        if (widget.ActualWidth <= 0)
        {
            widget.UpdateLayout();
        }

        var target = TargetLeft(id, defaultRight, widget.ActualWidth, geometry, gap);
        var moved = false;

        if (widget is not IBubbleWidget { IsBubbleDragging: true } && Math.Abs(widget.Left - target) > 0.5)
        {
            widget.Left = target;
            moved = true;
        }

        var top = geometry.Bottom - widget.ActualHeight;
        if (Math.Abs(widget.Top - top) > 0.5)
        {
            widget.Top = top;
            moved = true;
        }

        return moved;
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        _clockTime.Text = now.ToString("HH:mm");
        _clockDate.Text = now.ToString("ddd d MMM");
    }

    private void PresentBackdrop(byte[] pixels, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (_backdropBitmap is null || _backdropBitmap.PixelWidth != width || _backdropBitmap.PixelHeight != height)
        {
            _backdropBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            _backdrop.Source = _backdropBitmap;
        }

        _backdropBitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
    }

    private void UpdateFullscreenState()
    {
        var fullscreen = ScreenInput.IsForegroundFullscreen();
        if (fullscreen == _fullscreenHidden)
        {
            return;
        }

        _fullscreenHidden = fullscreen;
        PositionDesktopButton();

        if (fullscreen)
        {
            _widgetsHiddenForFullscreen = _weatherWidget?.IsVisible ?? false;
            _weatherWidget?.Hide();
            _mediaWidget?.Hide();
            Hide();
        }
        else
        {
            Show();
            Reposition();
            if (_widgetsHiddenForFullscreen)
            {
                _weatherWidget?.Show();
                _mediaWidget?.Show();
                ArrangeWidgets();
            }
        }
    }

    private bool _widgetsHiddenForFullscreen;

    private void ScheduleRunningRefresh()
    {
        _runningDebounce.Stop();
        _runningDebounce.Start();
    }

    private void UpdateForeground()
    {
        var info = ScreenInput.GetForegroundInfo();
        if (IsIgnoredForeground(info.ProcessName) || string.Equals(info.ProcessName, _activeProcess, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _pendingForeground = info.ProcessName;
        _pendingTitle = info.Title;
        _pendingPath = info.Path;
        _foregroundDebounce.Stop();
        _foregroundDebounce.Start();
    }

    private void ApplyForeground()
    {
        _foregroundDebounce.Stop();
        if (string.IsNullOrEmpty(_pendingForeground)
            || string.Equals(_pendingForeground, _activeProcess, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _activeProcess = _pendingForeground;
        _activeTitle = _pendingTitle;
        _activePath = _pendingPath;

        if (_attentionItem is not null
            && AppMatcher.Matches(_attentionItem.Name, _attentionItem.ProcessName, _attentionItem.App.Target, _activeProcess, _activeTitle, _activePath))
        {
            _attentionItem = null;
        }

        UpdateAura();

        // a just-launched app may not be marked running yet - refresh immediately so the effect starts now
        if (_activeIndex < 0)
        {
            RefreshRunning();
        }

        Log($"fg='{_activeProcess}' title='{_activeTitle}' path='{_activePath}' auraIndex={_activeIndex} visible={_auraGrid.Visibility}");
    }

    private void OnSystemAlert(IntPtr hwnd)
    {
        var info = ScreenInput.GetWindowInfo(hwnd);
        if (string.IsNullOrEmpty(info.ProcessName))
        {
            return;
        }

        for (var i = 0; i < _slots.Count; i++)
        {
            var item = _slots[i].Item;
            if (item.IsRunning
                && AppMatcher.Matches(item.Name, item.ProcessName, item.App.Target, info.ProcessName, info.Title, info.Path))
            {
                if (!ReferenceEquals(_attentionItem, item))
                {
                    _attentionItem = item;
                    Log($"alert {item.Name}");
                    UpdateAura();
                }

                return;
            }
        }
    }

    private void UpdateAmbient()
    {
        var cpu = SystemMetrics.GetCpuUsage();
        _ambientLevel += (cpu - _ambientLevel) * 0.35;
        _separator.Opacity = 0.5 + (0.5 * (_ambientLevel / 100.0));
    }

    private static bool IsIgnoredForeground(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return true;
        }

        return name.Equals("MacDock", StringComparison.OrdinalIgnoreCase)
            || name.Equals("explorer", StringComparison.OrdinalIgnoreCase)
            || name.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase)
            || name.Equals("SearchHost", StringComparison.OrdinalIgnoreCase)
            || name.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase)
            || name.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateAura()
    {
        _activeIndex = -1;
        if (!string.IsNullOrEmpty(_activeProcess))
        {
            for (var i = 0; i < _slots.Count; i++)
            {
                var candidate = _slots[i].Item;
                if (candidate.IsRunning
                    && AppMatcher.Matches(candidate.Name, candidate.ProcessName, candidate.App.Target, _activeProcess, _activeTitle, _activePath))
                {
                    _activeIndex = i;
                    break;
                }
            }
        }

        _auraAttention = false;
        if (_activeIndex < 0 && _attentionItem is not null)
        {
            for (var i = 0; i < _slots.Count; i++)
            {
                if (ReferenceEquals(_slots[i].Item, _attentionItem))
                {
                    _activeIndex = i;
                    _auraAttention = true;
                    break;
                }
            }
        }

        if (_activeIndex < 0 || _slots[_activeIndex].Item.Icon is not { } icon)
        {
            _auraGrid.Visibility = Visibility.Collapsed;
            SetSpectralVisible(false);
            for (var k = 0; k < _auraEffects.Length; k++)
            {
                _auraEffects[k].BeginAnimation(DropShadowEffect.DirectionProperty, null);
            }

            return;
        }

        var item = _slots[_activeIndex].Item;

        if (AuraSettings.Effect == AuraEffect.Burst)
        {
            _auraGrid.Visibility = Visibility.Collapsed;
            for (var k = 0; k < _auraEffects.Length; k++)
            {
                _auraEffects[k].BeginAnimation(DropShadowEffect.DirectionProperty, null);
            }

            UpdateSpectral(item);
            PositionSpectral(_slots[_activeIndex]);
            SetSpectralVisible(true);
            return;
        }

        SetSpectralVisible(false);

        var colors = AuraSettings.GetColors(item);
        for (var k = 0; k < _auraImages.Length; k++)
        {
            _auraImages[k].Source = icon;
            _auraEffects[k].Color = colors[k % colors.Length];
            _auraEffects[k].BeginAnimation(DropShadowEffect.DirectionProperty, _auraDirAnims[k]);
        }

        _auraGrid.Visibility = Visibility.Visible;
        PositionAura(_slots[_activeIndex]);
    }

    private void PositionAura(IconSlot slot)
    {
        Canvas.SetLeft(_auraGrid, Canvas.GetLeft(slot.Button));
        Canvas.SetTop(_auraGrid, Canvas.GetTop(slot.Button));
        if (_auraGrid.RenderTransform is ScaleTransform aura)
        {
            aura.ScaleX = slot.Scale.ScaleX;
            aura.ScaleY = slot.Scale.ScaleY;
        }
    }

    private void SetSpectralVisible(bool visible)
    {
        if (visible == (_spectralLayer.Visibility == Visibility.Visible))
        {
            return;
        }

        _spectralLayer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        if (visible)
        {
            _spectralLayer.BeginAnimation(OpacityProperty, _auraAttention ? _spectralBreathFast : _spectralBreath);
            _spectralRim.Opacity = _auraAttention ? 1.0 : 0.82;
            _spectralRim.BeginAnimation(Shape.StrokeDashOffsetProperty, _spectralDash);
            _spectralWaveSpin.BeginAnimation(RotateTransform.AngleProperty, _spectralWaveAnim);
            for (var i = 0; i < _spectralWisps.Count; i++)
            {
                _spectralWisps[i].Spin.BeginAnimation(RotateTransform.AngleProperty, _spectralWispSpins[i]);
            }
        }
        else
        {
            _spectralLayer.BeginAnimation(OpacityProperty, null);
            _spectralRim.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            _spectralWaveSpin.BeginAnimation(RotateTransform.AngleProperty, null);
            for (var i = 0; i < _spectralWisps.Count; i++)
            {
                _spectralWisps[i].Spin.BeginAnimation(RotateTransform.AngleProperty, null);
            }
        }
    }

    private static Color Spectral(Color color)
    {
        // ghostly: desaturate toward a pale blue-white (unseen-world tint)
        static byte Mix(byte value, byte target, double amount) => (byte)Math.Round(value + ((target - value) * amount));
        return Color.FromRgb(Mix(color.R, 0xBA, 0.58), Mix(color.G, 0xE8, 0.58), Mix(color.B, 0xF4, 0.58));
    }

    private void UpdateSpectral(DockItemViewModel item)
    {
        var colors = AuraSettings.GetColors(item);
        if (colors.Length == 0)
        {
            return;
        }

        var first = Spectral(colors[0]);
        var second = Spectral(colors[1 % colors.Length]);
        var third = Spectral(colors[2 % colors.Length]);
        var k = AuraSettings.SpectralIntensity;

        // soft radial distortion around the icon edges
        _spectralHaloBrush.GradientStops[0].Color = Color.FromArgb(0x00, first.R, first.G, first.B);
        _spectralHaloBrush.GradientStops[1].Color = Color.FromArgb(Alpha(0x3E, k), first.R, first.G, first.B);
        _spectralHaloBrush.GradientStops[2].Color = Color.FromArgb(0x00, second.R, second.G, second.B);

        // irregular shimmering outline (slight refraction)
        _spectralRimBrush.GradientStops.Clear();
        _spectralRimBrush.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(0xB4, k), first.R, first.G, first.B), 0.0));
        _spectralRimBrush.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(0x44, k), second.R, second.G, second.B), 0.45));
        _spectralRimBrush.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(0xB4, k), third.R, third.G, third.B), 1.0));

        // flowing wave-like energy around the perimeter
        _spectralWaveBrush.GradientStops.Clear();
        _spectralWaveBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, second.R, second.G, second.B), 0.0));
        _spectralWaveBrush.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(0x70, k), third.R, third.G, third.B), 0.5));
        _spectralWaveBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, first.R, first.G, first.B), 1.0));

        // faint smoky wisps
        for (var i = 0; i < _spectralWisps.Count; i++)
        {
            var color = Spectral(colors[i % colors.Length]);
            _spectralWisps[i].Wisp.Fill = new SolidColorBrush(Color.FromArgb(Alpha(0x50, k), color.R, color.G, color.B));
        }
    }

    private static byte Alpha(int alpha, double intensity) => (byte)Math.Clamp(alpha * intensity, 0, 255);

    private void PositionSpectral(IconSlot slot)
    {
        var centerX = slot.Center;
        var centerY = IconBottom - (IconBox / 2);

        Canvas.SetLeft(_spectralHalo, centerX - (_spectralHalo.Width / 2));
        Canvas.SetTop(_spectralHalo, centerY - (_spectralHalo.Height / 2));
        Canvas.SetLeft(_spectralRim, centerX - (_spectralRim.Width / 2));
        Canvas.SetTop(_spectralRim, centerY - (_spectralRim.Height / 2));
        Canvas.SetLeft(_spectralWave, centerX - (_spectralWave.Width / 2));
        Canvas.SetTop(_spectralWave, centerY - (_spectralWave.Height / 2));

        const double orbit = 30;
        foreach (var (wisp, _) in _spectralWisps)
        {
            Canvas.SetLeft(wisp, centerX - (wisp.Width / 2));
            Canvas.SetTop(wisp, centerY - orbit - (wisp.Height / 2));
        }
    }

    private void OnAuraSettingsChanged() => UpdateAura();

    private double ToScreenX(double localX)
        => Left + (ActualWidth / 2) + ((localX - (ActualWidth / 2)) * _dockScale);

    private void OpenSettings()
    {
        if (_settings is { IsLoaded: true })
        {
            _settings.Activate();
            return;
        }

        var window = new SettingsWindow();
        _settings = window;
        _settingsOpen = true;

        window.Closed += (_, _) =>
        {
            _settingsOpen = false;
            _settings = null;

            if (!CursorOverDock())
            {
                _targetBulge = 0;
                _targetDockScale = CompactScale;
            }

            EnsureAnimation();
        };

        window.Show();
        window.UpdateLayout();
        window.Left = Math.Max(8, Math.Min(
            ToScreenX(Canvas.GetLeft(_clock) + (ClockWidth / 2)) - (window.ActualWidth / 2),
            SystemParameters.PrimaryScreenWidth - window.ActualWidth - 8));
        window.Top = Math.Max(8, Top + RestTop - window.ActualHeight - 12);
        window.Activate();
    }

    private static void Log(string message)
    {
        try
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "macdock.log");
            System.IO.File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
            // diagnostics must never crash the dock
        }
    }

    private sealed class IconSlot
    {
        public IconSlot(Button button, Ellipse dot, DockItemViewModel item, double centerLocal, ScaleTransform scale, TranslateTransform translate)
        {
            Button = button;
            Dot = dot;
            Item = item;
            CenterLocal = centerLocal;
            Scale = scale;
            Translate = translate;
        }

        public Button Button { get; }

        public Ellipse Dot { get; }

        public DockItemViewModel Item { get; }

        public double CenterLocal { get; }

        public double Center { get; set; }

        public ScaleTransform Scale { get; }

        public TranslateTransform Translate { get; }
    }
}










