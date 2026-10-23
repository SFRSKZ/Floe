using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Floe.App.Effects.MetalRim;
using Floe.App.Effects.Visualizer;
using Floe.App.Services;

namespace Floe.App.Effects.ActiveIcon;

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

    private static readonly Lazy<PixelShader?> Shader = new(() => ShaderCompiler.LoadShader("Floe.ActiveIcon.hlsl"));

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

    private readonly PacedClock _clock;
    private double _lastTime = double.NegativeInfinity;

    public ActiveIconEffect(double iconSize)
    {
        // inside the dock window: every other visualizer frame is plenty for a slow colour flow
        _clock = new PacedClock(now =>
        {
            // the shader only flows its colour slowly: the ambient rate is indistinguishable and saves constant writes
            double fps = Performance.AmbientFps;
            if (!VisualizerPacing.DockAnimating && (fps <= 0 || now - _lastTime < 1.0 / fps))
            {
                return;
            }

            _lastTime = now;
            Time = now;
        }, 2, everyFrameWhileDockAnimates: true);
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
        _clock.Start();
        BeginAnimation(StrengthProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)) { EasingFunction = new QuadraticEase() });
    }

    /// <summary>Stops the clock so a detached effect costs no frames.</summary>
    public void Stop()
    {
        _clock.Stop();
        BeginAnimation(StrengthProperty, null);
    }

    public void SetVisible(bool visible)
    {
        if (visible) _clock.Start();
        else _clock.Stop();
    }

    private static DependencyProperty Register<T>(string name, T value, int register) =>
        DependencyProperty.Register(name, typeof(T), typeof(ActiveIconEffect), new UIPropertyMetadata(value, PixelShaderConstantCallback(register)));
}
