using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using MacDock.App.Effects.MetalRim;

namespace MacDock.App.Effects.ActiveIcon;

public sealed class ClickGlassEffect : ShaderEffect
{
    /// <summary>Room the effect may draw in around the icon (DIP); icons sit 8 DIP apart.</summary>
    public const double Pad = ActiveIconEffect.Pad;

    private static readonly Lazy<PixelShader?> Shader = new(() => ShaderCompiler.LoadShader("MacDock.ClickGlass.hlsl"));

    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty(nameof(Input), typeof(ClickGlassEffect), 0);
    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }

    public static readonly DependencyProperty FieldProperty = RegisterPixelShaderSamplerProperty(nameof(Field), typeof(ClickGlassEffect), 1, SamplingMode.Bilinear);
    public Brush Field { get => (Brush)GetValue(FieldProperty); set => SetValue(FieldProperty, value); }

    public static readonly DependencyProperty ProgressProperty = Register(nameof(Progress), 0.0, 0);
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    public static readonly DependencyProperty ToneProperty = Register(nameof(Tone), 0.0, 1);
    public double Tone { get => (double)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }

    public static readonly DependencyProperty BoxProperty = Register(nameof(Box), new Point(1, 0), 2);
    public Point Box { get => (Point)GetValue(BoxProperty); set => SetValue(BoxProperty, value); }

    public ClickGlassEffect(double iconSize)
    {
        PixelShader = Shader.Value;
        PaddingLeft = PaddingTop = PaddingRight = PaddingBottom = Pad;
        Box = new Point(iconSize + (2 * Pad), 0);
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(FieldProperty);
        foreach (var property in new[] { ProgressProperty, ToneProperty, BoxProperty })
        {
            UpdateShaderValue(property);
        }
    }

    public static bool IsAvailable => Shader.Value is not null;

    private static DependencyProperty Register<T>(string name, T value, int register) =>
        DependencyProperty.Register(name, typeof(T), typeof(ClickGlassEffect), new UIPropertyMetadata(value, PixelShaderConstantCallback(register)));
}
