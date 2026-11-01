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
using Floe.App.Services;
using Floe.App.ViewModels;
using Floe.Core.Interop;
using Floe.Core.Models;
using Floe.Core.Services;

namespace Floe.App.Windows;

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
    // Transparent footer contains the shadow; AppBar still reserves RestBarHeight + BottomMargin.
    private const double ShadowRoom = 24;
    private const double WindowHeight = RestBarHeight + MaxBulge + Headroom + ShadowRoom;
    private const double EdgeMargin = 6;
    private const double MaxSideStretch = 12;
    // the size the dock rests at with the cursor away: smaller, unless shrinking is turned off
    private static double CompactScale => GlassSettings.Shrink ? 0.62 : 1;
    private const double HoverMaxScale = 1.4;
    private const double FalloffRadius = 115;
    private const double GlowRadius = 92;
    private const double BulgeSigma = 34;

    private const double CornerRadius = 22;
    private const double BottomMargin = 8;
    // the shown dock sits this much lower than the reserved strip, its shadow running off the screen's edge
    private const double Sink = 8;
    private const int TriggerShow = 6;
    private const int TriggerPx = 4;

    private static readonly double RestTop = WindowHeight - ShadowRoom - RestBarHeight;
    private static readonly double Bottom = WindowHeight - ShadowRoom;
    private static readonly double IconBottom = Bottom - PadY;

    private readonly List<IconSlot> _slots = new();
    private IconSlot? _dragSlot;
    private Point _dragStart;
    private bool _isDragging;
    // while an icon is held: the cursor x it follows, and where the others open a gap for it (-1: none)
    private double _dragX;
    private double _dragY;
    private int _dragGap = -1;
    private readonly Dictionary<string, DockItemViewModel> _pinned = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DockItemViewModel> _pinnedList = new();
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _runningTimer = new() { Interval = TimeSpan.FromMilliseconds(2500) };
    private readonly DispatcherTimer _foregroundTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _foregroundDebounce = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private string _pendingForeground = string.Empty;
    private string _pendingTitle = string.Empty;
    private string _pendingPath = string.Empty;
    private readonly DispatcherTimer _runningDebounce = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly DispatcherTimer _fullscreenTimer = new() { Interval = TimeSpan.FromMilliseconds(1000) };
    private bool _fullscreenHidden;
    private bool _appbarFullscreen;
    private AppBar? _appBar;
    private IntPtr _monitor;
    private ScreenInput.MonitorInfo _monitorInfo;
    private int _taskbarCreatedMessage;
    private EventHooks.Hook? _foregroundHook;
    private EventHooks.Hook? _fullscreenHook;
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
    private readonly SolidColorBrush _runningDotBrush = new(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
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
    private readonly DispatcherTimer _tooltipTimer = new() { Interval = TimeSpan.FromMilliseconds(420) };

    private readonly Path _glass = new() { IsHitTestVisible = false };
    private readonly Path _sheen = new() { IsHitTestVisible = false };
    private readonly Path _specular = new() { IsHitTestVisible = false };
    private readonly Rectangle _grain = new() { IsHitTestVisible = false };
    private readonly Path _rim = new() { IsHitTestVisible = false };
    private readonly TronCubes _tronCubes = new();
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
    private readonly Canvas _backdropHost = new() { IsHitTestVisible = false };
    private readonly ScaleTransform _backdropUnscale = new(1, 1);
    private WriteableBitmap? _backdropBitmap;
    private IDisposable? _backdropCapture;
    // the live GPU capture hands over frames it already blurred
    private bool _gpuBackdrop;
    private readonly StackPanel _clock = new();
    private readonly TextBlock _clockTime = new();
    private readonly TextBlock _clockDate = new();
    private readonly Rectangle _clockDivider = new();
    private readonly SolidColorBrush _lineBrush = new(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
    private PreviewWindow? _preview;
    private IconSlot? _previewSlot;
    private HwndSource? _source;
    private IntPtr _trayIcon;
    private DockEdge _lastDockEdge = DockEdge.Bottom;
    private DockEdge? _orientedEdge;

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

    private FontFamily _clockFont = new("Segoe UI");
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
    private DispatcherTimer? _springTimer;
    private double _targetHoverX;
    private double _targetBulge;
    private int _hoveredIndex = -1;
    private List<string> _itemKeys = new();

    private double _shapeHover = double.NaN;
    private double _shapeBulge = double.NaN;
    private double _shapeLeft = double.NaN;
    private double _shapeWidth = double.NaN;
    private double _shapeScale = double.NaN;
    private int _shapeCount = -1;
    private IconMenuWindow? _iconMenu;

    public bool AutoHide { get; set; }

    public bool BlurBackdrop { get; set; } = true;

    // Off for --render runs so a screenshot never changes the user's work area.
    public bool ReserveScreenSpace { get; set; } = true;

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
        Title = Product.Name;
        // the settings are read again further down; the window kind has to be known before it is shown
        GlassSettings.Load();
        DwmDock = WantsDwmDock();
        if (DwmDock) AllowsTransparency = false;

        Root.RenderTransformOrigin = new Point(0.5, 1.0);
        Root.RenderTransform = _rootScale;
        _rootScale.Changed += (_, _) => UpdateBackdropUnscale();
        _lastDockEdge = DockSettings.Edge;
        ApplyDockOrientation();
        DockSettings.Changed += OnDockSettingsChanged;
        Closed += (_, _) => DockSettings.Changed -= OnDockSettingsChanged;
        AllowDrop = true;
        Root.AllowDrop = true;
        IconLayer.AllowDrop = true;

        _clockFont = new FontFamily(new Uri($"pack://application:,,,/{Product.Exe};component/"), "./Assets/Fonts/#Doto");

        var clockGlow = new DropShadowEffect { Color = Colors.White, ShadowDepth = 0, BlurRadius = 10, Opacity = 0.6 };

        _clockTime.FontFamily = _clockFont;
        _clockTime.FontSize = 26;
        _clockTime.FontWeight = FontWeights.Bold;
        _clockTime.Foreground = Brushes.White;
        _clockTime.TextAlignment = TextAlignment.Center;
        _clockTime.Effect = clockGlow;

        _clockDate.FontFamily = _clockFont;
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

        _clockDivider.Width = 1;
        _clockDivider.Height = 34;
        _clockDivider.Fill = _lineBrush;
        _clockDivider.RadiusX = 0.5;
        _clockDivider.RadiusY = 0.5;

        _clockLayer.Children.Add(_clock);
        _clockLayer.Children.Add(_clockDivider);
        InitializeStartButton();
        Root.Children.Add(_clockLayer);

        UpdateClock();
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();

        // the EVENT_SYSTEM_FOREGROUND hook does the real work; this poll is only a safety net
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
        _specular.Visibility = Visibility.Collapsed;
        _rim.Stroke = BuildRimBrush();
        _rim.Clip = _shapeGeometry;
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
        InitializeGlass();
        _backdrop.Width = _windowWidth;
        _backdrop.Height = WindowHeight;
        Canvas.SetLeft(_backdrop, 0);
        Canvas.SetTop(_backdrop, 0);
        _backdrop.RenderTransform = _backdropUnscale;
        _backdropHost.Children.Add(_backdrop);
        ShapeLayer.Children.Insert(0, _backdropHost);

        _shapeFigure.Segments.Add(_shapeSegment);
        _shapeGeometry.Figures.Add(_shapeFigure);
        _glass.Data = _shapeGeometry;
        _rim.Data = _shapeGeometry;
        _sheen.Data = _shapeGeometry;
        _specular.Data = _shapeGeometry;
        _glowLayer.Clip = _shapeGeometry;
        _backdropHost.Clip = _shapeGeometry;

        ShapeLayer.Children.Add(_outerRim);
        ShapeLayer.Children.Add(_glass);
        ShapeLayer.Children.Add(_rim);
        ShapeLayer.Children.Add(_sheen);
        ShapeLayer.Children.Add(_specular);
        // more diagnostics: glass hides the whole surface, combine drops the exterior clips, stroke the hairlines
        if (NoFx.Contains("glass")) ShapeLayer.Visibility = Visibility.Collapsed;
        if (NoFx.Contains("icons")) IconLayer.Visibility = Visibility.Collapsed;
        if (NoFx.Contains("combine")) _shadowHost.Clip = _outerRim.Clip = null;
        if (NoFx.Contains("stroke")) _rim.Data = _outerRim.Data = null;

        _grain.Fill = new ImageBrush(MakeNoiseBitmap())
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 32, 32),
            Stretch = Stretch.None,
        };
        _grain.Opacity = Floe.App.Services.BlurSettings.GrainOpacity;
        _grain.Clip = _shapeGeometry;
        ShapeLayer.Children.Add(_grain);
        InitializeCyber();
        ShapeLayer.Children.Add(_metalHost);
        ShapeLayer.Children.Add(_tronCubes);

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
        if (NoFx.Contains("aura")) _glowLayer.Visibility = _spectralLayer.Visibility = Visibility.Collapsed;
        if (NoFx.Contains("clock")) _clockLayer.Visibility = Visibility.Collapsed;



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
        if (AuraSettings.ClickSound) ThreadPool.QueueUserWorkItem(_ => ClickSound.Warm());
        GlassSettings.Load();
        ApplyGlassSettings();
        GlassSettings.Changed += OnGlassSettingsChanged;
        InitializeVisualizer();
        InitializeDoze();
        Closed += (_, _) => DisposeGlass();
        AuraSettings.Changed += OnAuraSettingsChanged;
        DesktopButtonSettings.Load();

        _hideTimer.Tick += HideTimer_Tick;
        _runningTimer.Tick += (_, _) => RefreshRunning();
        _tooltipTimer.Tick += (_, _) => ShowTooltip();

        Loaded += (_, _) =>
        {
            Reposition();
            PrepareGlassPreview();
            UpdateShape();
            if (AutoHide)
            {
                _hideTimer.Start();
            }

            RefreshRunning();
            _runningTimer.Start();

            if (BlurBackdrop && !NoFx.Contains("capture"))
            {
                Func<bool> active = () => _renderingHooked || _menuOpen || _visualizerWindow is { PanelBehindDock: true };
                // WPF's radius is about three sigmas, the GPU pass takes two, at half the physical resolution
                var gpu = NoFx.Contains("gpubackdrop") ? null : GpuBackdropCapture.TryCreate(this, PresentBackdrop, active, () => BlurSettings.Radius * _dpi / 3);
                _gpuBackdrop = gpu is not null;
                _backdropCapture = gpu ?? (IDisposable)new BackdropCapture(this, PresentBackdrop, active);
            }

            InitializeSections();
            ApplyCyberInk(); // the section glyphs exist only now

            _fullscreenTimer.Tick += (_, _) => UpdateFullscreenState();
            _fullscreenTimer.Start();
            _ambientTimer.Start();

            _trayIcon = TrayNotification.LoadAppIcon(Environment.ProcessPath ?? string.Empty);
            TrayNotification.Add(_hwnd, Product.Name, _trayIcon);
        };
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

        public SolidColorBrush LineBrush { get; } = new(Colors.Black);

        /// <summary>The Cyberpunk card as it reads on the dock, and the grey one for the dark frame.</summary>
        public (ImageSource OnDock, ImageSource OnFrame)? Card { get; set; }

        public Ellipse Dot { get; }

        public DockItemViewModel Item { get; }

        public double CenterLocal { get; }

        public double Center { get; set; }

        public ScaleTransform Scale { get; }

        public TranslateTransform Translate { get; }

        // eased x offset while another icon is held over the dock, so the gap slides open
        public double Shift { get; set; }
    }
}
