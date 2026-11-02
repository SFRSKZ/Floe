using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Floe.App.Effects.MetalRim;

namespace Floe.App.Effects.Visualizer;

/// <summary>
/// Shows whatever lies under it as a colour LED screen: every <see cref="Pitch"/>-pixel cell becomes a
/// red, green and blue triad, so the picture reads as a panel of emitters rather than smooth pixels.
/// </summary>
public sealed class LedScreenEffect : ShaderEffect
{
    private static readonly Lazy<PixelShader?> Shader = new(() => ShaderCompiler.LoadShader("Floe.LedScreen.hlsl"));
    // RenderTargetBitmap runs ps_2_0 only, so diagnostics build the same source for the software rasterizer.
    private static readonly Lazy<PixelShader?> SoftwareShader = new(() => ShaderCompiler.LoadShader("Floe.LedScreen.hlsl", "ps_2_0"));

    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty(nameof(Input), typeof(LedScreenEffect), 0);
    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }

    /// <summary>Element size in device pixels; the shader cannot know it on its own.</summary>
    public static readonly DependencyProperty SizeProperty = Register(nameof(Size), new Point(1, 1), 0);
    public Point Size { get => (Point)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    public static readonly DependencyProperty PitchProperty = Register(nameof(Pitch), 6.0, 1);
    /// <summary>Cell pitch in device pixels; smaller pitch, finer picture.</summary>
    public double Pitch { get => (double)GetValue(PitchProperty); set => SetValue(PitchProperty, value); }

    public LedScreenEffect() : this(software: false) { }

    /// <param name="software">true compiles the same HLSL for ps_2_0, which RenderTargetBitmap can run.</param>
    internal LedScreenEffect(bool software)
    {
        PixelShader = software ? SoftwareShader.Value : Shader.Value;
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(SizeProperty);
        UpdateShaderValue(PitchProperty);
    }

    public static bool IsAvailable => Shader.Value is not null;
    /// <summary>The preview needs the ps_2_0 build too, or the render comes out plain.</summary>
    internal static bool IsSoftwareAvailable => SoftwareShader.Value is not null;

    private static DependencyProperty Register<T>(string name, T value, int register) =>
        DependencyProperty.Register(name, typeof(T), typeof(LedScreenEffect), new UIPropertyMetadata(value, PixelShaderConstantCallback(register)));
}
