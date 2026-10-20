using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace MacDock.App.Effects.Glass;

internal sealed class VibrancyEffect : ShaderEffect
{
    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty(nameof(Input), typeof(VibrancyEffect), 0);
    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    public VibrancyEffect()
    {
        PixelShader = LoadShader(GlassShaderCode.Vibrancy);
        UpdateShaderValue(InputProperty);
    }

    internal static PixelShader LoadShader(string code)
    {
        var shader = new PixelShader();
        using var stream = new MemoryStream(Convert.FromBase64String(code), false);
        shader.SetStreamSource(stream);
        shader.Freeze();
        return shader;
    }
}

internal sealed class RimWarpEffect : ShaderEffect
{
    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty(nameof(Input), typeof(RimWarpEffect), 0);
    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    public static readonly DependencyProperty SizeProperty = Register(nameof(Size), new Point(640, 68), 0);
    public Point Size { get => (Point)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public static readonly DependencyProperty HoverProperty = Register(nameof(Hover), 320.0, 1);
    public double Hover { get => (double)GetValue(HoverProperty); set => SetValue(HoverProperty, value); }
    public static readonly DependencyProperty BulgeProperty = Register(nameof(Bulge), 0.0, 2);
    public double Bulge { get => (double)GetValue(BulgeProperty); set => SetValue(BulgeProperty, value); }
    public static readonly DependencyProperty StretchProperty = Register(nameof(Stretch), new Point(), 3);
    public Point Stretch { get => (Point)GetValue(StretchProperty); set => SetValue(StretchProperty, value); }
    public static readonly DependencyProperty PlateauProperty = Register(nameof(Plateau), 0.0, 4);
    public double Plateau { get => (double)GetValue(PlateauProperty); set => SetValue(PlateauProperty, value); }
    public static readonly DependencyProperty SigmaProperty = Register(nameof(Sigma), 34.0, 5);
    public double Sigma { get => (double)GetValue(SigmaProperty); set => SetValue(SigmaProperty, value); }
    public RimWarpEffect()
    {
        PixelShader = VibrancyEffect.LoadShader(GlassShaderCode.RimWarp);
        foreach (var property in new[] { InputProperty, SizeProperty, HoverProperty, BulgeProperty, StretchProperty, PlateauProperty, SigmaProperty }) UpdateShaderValue(property);
    }
    private static DependencyProperty Register<T>(string name, T value, int register) =>
        DependencyProperty.Register(name, typeof(T), typeof(RimWarpEffect), new UIPropertyMetadata(value, PixelShaderConstantCallback(register)));
}
