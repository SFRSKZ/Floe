using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Floe.App.Effects.MetalRim;

namespace Floe.App.Effects.ActiveIcon;

public sealed class ClickGlassEffect : ShaderEffect
{
    /// <summary>Room the effect may draw in around the icon (DIP); icons sit 8 DIP apart.</summary>
    public const double Pad = ActiveIconEffect.Pad;

    private static readonly Lazy<PixelShader?> Shader = new(() => ShaderCompiler.LoadShader("Floe.ClickGlass.hlsl"));

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

    /// <summary>Rim colours: 0 rainbow, 1 colourless, 2 <see cref="ColorA"/>/<see cref="ColorB"/>.</summary>
    public static readonly DependencyProperty PaletteProperty = Register(nameof(Palette), 0.0, 3);
    public double Palette { get => (double)GetValue(PaletteProperty); set => SetValue(PaletteProperty, value); }

    public static readonly DependencyProperty ColorAProperty = Register(nameof(ColorA), Colors.White, 4);
    public Color ColorA { get => (Color)GetValue(ColorAProperty); set => SetValue(ColorAProperty, value); }

    public static readonly DependencyProperty ColorBProperty = Register(nameof(ColorB), Colors.White, 5);
    public Color ColorB { get => (Color)GetValue(ColorBProperty); set => SetValue(ColorBProperty, value); }

    public ClickGlassEffect(double iconSize)
    {
        PixelShader = Shader.Value;
        PaddingLeft = PaddingTop = PaddingRight = PaddingBottom = Pad;
        Box = new Point(iconSize + (2 * Pad), 0);
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(FieldProperty);
        foreach (var property in new[] { ProgressProperty, ToneProperty, BoxProperty, PaletteProperty, ColorAProperty, ColorBProperty })
        {
            UpdateShaderValue(property);
        }
    }

    public static bool IsAvailable => Shader.Value is not null;

    private static DependencyProperty Register<T>(string name, T value, int register) =>
        DependencyProperty.Register(name, typeof(T), typeof(ClickGlassEffect), new UIPropertyMetadata(value, PixelShaderConstantCallback(register)));
}
