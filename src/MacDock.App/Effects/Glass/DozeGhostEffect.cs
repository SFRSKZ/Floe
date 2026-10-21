using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using MacDock.App.Effects.MetalRim;

namespace MacDock.App.Effects.Glass;

internal sealed class DozeGhostEffect : ShaderEffect
{
    private static readonly Lazy<PixelShader?> Shader = new(() => ShaderCompiler.LoadShader("MacDock.DozeGhost.hlsl"));
    private static readonly Lazy<PixelShader> SoftwareShader = new(() =>
    {
        // WPF software rendering (RenderTargetBitmap) supports ps_2_0 only.
        // Compile the identical embedded source with the shared compiler.
        using var resource = typeof(DozeGhostEffect).Assembly.GetManifestResourceStream("MacDock.DozeGhost.hlsl")!;
        using var source = new MemoryStream();
        resource.CopyTo(source);
        using var code = new MemoryStream(ShaderCompiler.CompileBytes(source.ToArray(), "ps_2_0"));
        var shader = new PixelShader();
        shader.SetStreamSource(code);
        shader.Freeze();
        return shader;
    });

    internal static bool IsAvailable => Shader.Value is not null;
    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty(nameof(Input), typeof(DozeGhostEffect), 0);
    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    public static readonly DependencyProperty LineProperty = Register(nameof(Line), 0.0, 0);
    public double Line { get => (double)GetValue(LineProperty); set => SetValue(LineProperty, value); }
    public static readonly DependencyProperty TimeProperty = Register(nameof(Time), 0.0, 1);
    public double Time { get => (double)GetValue(TimeProperty); set => SetValue(TimeProperty, value); }
    public static readonly DependencyProperty SizeProperty = Register(nameof(Size), new Point(1, 1), 2);
    public Point Size { get => (Point)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    internal DozeGhostEffect(bool software = false)
    {
        PixelShader = software || !RenderCapability.IsPixelShaderVersionSupported(3, 0)
            ? SoftwareShader.Value : Shader.Value ?? SoftwareShader.Value;
        foreach (var property in new[] { InputProperty, LineProperty, TimeProperty, SizeProperty })
            UpdateShaderValue(property);
    }

    private static DependencyProperty Register<T>(string name, T value, int register) =>
        DependencyProperty.Register(name, typeof(T), typeof(DozeGhostEffect), new UIPropertyMetadata(value, PixelShaderConstantCallback(register)));
}
