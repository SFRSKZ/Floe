using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Floe.App.Effects.MetalRim;

namespace Floe.App.Effects.Chromatic;

/// <summary>
/// The chromatic button face. One fixed tint (no hue cycling) over a slowly rotating
/// light field, projected through a rounded contour. Draw it behind the label and let
/// the border's rounded rectangle supply the mask. Animate <see cref="Time"/> in
/// seconds externally and drive <see cref="Hover"/> 0..1.
/// ps_2_0: the surface must also appear in RenderTargetBitmap diagnostics.
/// </summary>
public sealed class ChromaticEffect : ShaderEffect
{
    private static readonly Lazy<PixelShader?> Shader = new(() => ShaderCompiler.LoadShader("Floe.Chromatic.hlsl", "ps_2_0"));

    public static bool IsAvailable => Shader.Value is not null;

    private static readonly Lazy<Brush> FieldBrush = new(() => Frozen(new ImageBrush(ChromaticField.Texture) { Stretch = Stretch.Fill }));

    private static readonly Lazy<Brush> GrainBrush = new(() => Frozen(new ImageBrush(ChromaticNoise.Texture)
    {
        Stretch = Stretch.Fill,
        TileMode = TileMode.Tile,
        ViewportUnits = BrushMappingMode.Absolute,
        Viewport = new Rect(0, 0, 48, 48),
    }));

    public static readonly DependencyProperty InputProperty =
        RegisterPixelShaderSamplerProperty(nameof(Input), typeof(ChromaticEffect), 0);
    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }

    public static readonly DependencyProperty FieldProperty =
        RegisterPixelShaderSamplerProperty(nameof(Field), typeof(ChromaticEffect), 1, SamplingMode.Bilinear);
    public Brush Field { get => (Brush)GetValue(FieldProperty); set => SetValue(FieldProperty, value); }

    public static readonly DependencyProperty GrainProperty =
        RegisterPixelShaderSamplerProperty(nameof(Grain), typeof(ChromaticEffect), 2, SamplingMode.Bilinear);
    public Brush Grain { get => (Brush)GetValue(GrainProperty); set => SetValue(GrainProperty, value); }

    // WPF cannot upload System.Windows.Size directly; its Point equivalent can.
    private static readonly DependencyProperty PixelSizeProperty = Register("PixelSize", new Point(96, 32), 0);
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(nameof(Size), typeof(Size), typeof(ChromaticEffect),
        new UIPropertyMetadata(new Size(96, 32), (d, e) =>
        {
            var size = (Size)e.NewValue;
            d.SetValue(PixelSizeProperty, new Point(size.Width, size.Height));
        }));
    public Size Size { get => (Size)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    // sin/cos of the spin angle; the shader has no trig instructions in ps_2_0.
    private static readonly DependencyProperty SpinProperty = Register("Spin", new Point(0, 1), 6);

    public static readonly DependencyProperty TimeProperty = DependencyProperty.Register(nameof(Time), typeof(double), typeof(ChromaticEffect),
        new UIPropertyMetadata(0.0, (d, _) =>
        {
            var angle = ((ChromaticEffect)d).Time * 0.11;
            d.SetValue(SpinProperty, new Point(Math.Sin(angle), Math.Cos(angle)));
        }));
    public double Time { get => (double)GetValue(TimeProperty); set => SetValue(TimeProperty, value); }

    public static readonly DependencyProperty HoverProperty = Register(nameof(Hover), 0.0, 1);
    /// <summary>0 = rest, 1 = hovered: the light is raised, matching the reference's brighten.</summary>
    public double Hover { get => (double)GetValue(HoverProperty); set => SetValue(HoverProperty, value); }

    public static readonly DependencyProperty IntensityProperty = Register(nameof(Intensity), 1.4, 2);
    public double Intensity { get => (double)GetValue(IntensityProperty); set => SetValue(IntensityProperty, value); }

    public static readonly DependencyProperty GrainStrengthProperty = Register(nameof(GrainStrength), 0.1, 3);
    public double GrainStrength { get => (double)GetValue(GrainStrengthProperty); set => SetValue(GrainStrengthProperty, value); }

    public static readonly DependencyProperty FeatherProperty = Register(nameof(Feather), 2.0, 4);
    /// <summary>Softness of the last few DIP at the contour.</summary>
    public double Feather { get => (double)GetValue(FeatherProperty); set => SetValue(FeatherProperty, value); }

    public static readonly DependencyProperty TintProperty = Register(nameof(Tint), Color.FromRgb(0x5B, 0xC8, 0xFF), 5);
    /// <summary>The one fixed material tint; the reflections only change its brightness.</summary>
    public Color Tint { get => (Color)GetValue(TintProperty); set => SetValue(TintProperty, value); }

    // Ordered to match HLSL c0..c6.
    private static readonly DependencyProperty[] Constants =
    [
        PixelSizeProperty, HoverProperty, IntensityProperty, GrainStrengthProperty,
        FeatherProperty, TintProperty, SpinProperty,
    ];

    public ChromaticEffect()
    {
        PixelShader = Shader.Value;
        Field = FieldBrush.Value;
        Grain = GrainBrush.Value;
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(FieldProperty);
        UpdateShaderValue(GrainProperty);
        foreach (var property in Constants)
        {
            UpdateShaderValue(property);
        }
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private static DependencyProperty Register<T>(string name, T value, int register) =>
        DependencyProperty.Register(name, typeof(T), typeof(ChromaticEffect),
            new UIPropertyMetadata(value, PixelShaderConstantCallback(register)));
}
