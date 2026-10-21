using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using MacDock.App.Effects.MetalRim;
using MacDock.App.Effects.Visualizer;

namespace MacDock.App.Effects.ActiveIcon;

public enum ActiveIconMode
{
    Neon = 0,
    Glass = 1,
}

/// <summary>Neon tube or liquid-glass frame drawn straight onto the active dock icon.</summary>
public sealed class ActiveIconEffect : ShaderEffect
{
    /// <summary>Room the effect may draw in around the icon (DIP); icons sit 8 DIP apart.</summary>
    public const double Pad = 12;

    private static readonly Lazy<PixelShader?> Shader = new(() => ShaderCompiler.LoadShader("MacDock.ActiveIcon.hlsl"));

    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty(nameof(Input), typeof(ActiveIconEffect), 0);
    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }

    public static readonly DependencyProperty FieldProperty = RegisterPixelShaderSamplerProperty(nameof(Field), typeof(ActiveIconEffect), 1, SamplingMode.Bilinear);
    public Brush Field { get => (Brush)GetValue(FieldProperty); set => SetValue(FieldProperty, value); }

    public static readonly DependencyProperty TimeProperty = Register(nameof(Time), 0.0, 0);
    public double Time { get => (double)GetValue(TimeProperty); set => SetValue(TimeProperty, value); }

    public static readonly DependencyProperty ModeProperty = Register(nameof(Mode), 0.0, 1);
    public double Mode { get => (double)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }

    public static readonly DependencyProperty ColorAProperty = Register(nameof(ColorA), Color.FromRgb(255, 51, 178), 2);
    public Color ColorA { get => (Color)GetValue(ColorAProperty); set => SetValue(ColorAProperty, value); }

    public static readonly DependencyProperty ColorBProperty = Register(nameof(ColorB), Color.FromRgb(51, 230, 255), 3);
    public Color ColorB { get => (Color)GetValue(ColorBProperty); set => SetValue(ColorBProperty, value); }

    public static readonly DependencyProperty StrengthProperty = Register(nameof(Strength), 0.0, 4);
    public double Strength { get => (double)GetValue(StrengthProperty); set => SetValue(StrengthProperty, value); }

    public static readonly DependencyProperty BoxProperty = Register(nameof(Box), new Point(1, 0), 5);
    public Point Box { get => (Point)GetValue(BoxProperty); set => SetValue(BoxProperty, value); }

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastTick = double.NegativeInfinity;
    private double _lastVisualizerRepaint = double.NegativeInfinity;
    private bool _skipRepaint;
    private bool _running;

    public ActiveIconEffect(double iconSize)
    {
        PixelShader = Shader.Value;
        PaddingLeft = PaddingTop = PaddingRight = PaddingBottom = Pad;
        Box = new Point(iconSize + (2 * Pad), 0);
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(FieldProperty);
        foreach (var property in new[] { TimeProperty, ModeProperty, ColorAProperty, ColorBProperty, StrengthProperty, BoxProperty })
        {
            UpdateShaderValue(property);
        }
    }

    public static bool IsAvailable => Shader.Value is not null;

    /// <summary>Starts the clock and fades in; call when the effect goes onto an icon.</summary>
    public void Start()
    {
        if (!_running)
        {
            CompositionTarget.Rendering += OnRendering;
            VisualizerPacing.Repainted += OnVisualizerRepainted;
            _running = true;
        }

        BeginAnimation(StrengthProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)) { EasingFunction = new QuadraticEase() });
    }

    /// <summary>Stops the clock so a detached effect costs no frames.</summary>
    public void Stop()
    {
        if (_running)
        {
            CompositionTarget.Rendering -= OnRendering;
            VisualizerPacing.Repainted -= OnVisualizerRepainted;
            _running = false;
        }

        BeginAnimation(StrengthProperty, null);
    }

    // Every Time change re-presents the whole dock window, and the dock shares its render thread
    // with the visualizer. Ticking on our own beat would fill the frames between the visualizer's
    // repaints, so while it plays the icon ticks on every other visualizer frame instead.
    private void OnRendering(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        if (VisualizerPacing.DockAnimating)
        {
            // the dock repaints every frame anyway
            Tick(now);
        }
        else if (now - _lastVisualizerRepaint > 0.25 && now - _lastTick >= 1.0 / 30)
        {
            Tick(now);
        }
    }

    private void OnVisualizerRepainted()
    {
        _lastVisualizerRepaint = _clock.Elapsed.TotalSeconds;
        _skipRepaint = !_skipRepaint;
        if (!_skipRepaint && !VisualizerPacing.DockAnimating)
        {
            Tick(_lastVisualizerRepaint);
        }
    }

    private void Tick(double now)
    {
        _lastTick = now;
        Time = now;
    }

    private static DependencyProperty Register<T>(string name, T value, int register) =>
        DependencyProperty.Register(name, typeof(T), typeof(ActiveIconEffect), new UIPropertyMetadata(value, PixelShaderConstantCallback(register)));
}
