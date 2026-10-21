using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using MacDock.App.Effects.MetalRim;

namespace MacDock.App.Effects.Glass;

internal sealed class DropLensEffect : ShaderEffect
{
    private static readonly Lazy<PixelShader?> Shader = new(() => ShaderCompiler.LoadShader("MacDock.DropLens.hlsl"));
    internal static bool IsAvailable => Shader.Value is not null;
    internal static bool IsEnabled => !(Environment.GetEnvironmentVariable("MACDOCK_NOFX") ?? "").Contains("drop", StringComparison.OrdinalIgnoreCase)
        && RenderCapability.IsPixelShaderVersionSupported(3, 0) && IsAvailable;

    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty(nameof(Input), typeof(DropLensEffect), 0, SamplingMode.Bilinear);
    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    public static readonly DependencyProperty FieldProperty = RegisterPixelShaderSamplerProperty(nameof(Field), typeof(DropLensEffect), 1, SamplingMode.Bilinear);
    public Brush Field { get => (Brush)GetValue(FieldProperty); set => SetValue(FieldProperty, value); }
    public static readonly DependencyProperty BoxProperty = Register(nameof(Box), new Point(560, 290), 0);
    public Point Box { get => (Point)GetValue(BoxProperty); set => SetValue(BoxProperty, value); }
    public static readonly DependencyProperty CenterProperty = Register(nameof(Center), new Point(280, 33), 1);
    public Point Center { get => (Point)GetValue(CenterProperty); set => SetValue(CenterProperty, value); }
    public static readonly DependencyProperty RadiusProperty = Register(nameof(Radius), 11.0, 2);
    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }

    internal DropLensEffect(Brush backdrop)
    {
        PixelShader = Shader.Value;
        Field = backdrop;
        foreach (var property in new[] { InputProperty, FieldProperty, BoxProperty, CenterProperty, RadiusProperty })
            UpdateShaderValue(property);
    }

    private static DependencyProperty Register<T>(string name, T value, int register) =>
        DependencyProperty.Register(name, typeof(T), typeof(DropLensEffect), new UIPropertyMetadata(value, PixelShaderConstantCallback(register)));
}
