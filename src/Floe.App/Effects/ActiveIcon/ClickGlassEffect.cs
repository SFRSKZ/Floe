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
    // the oil-paint click: same first registers (Progress, Tone, Box), a different picture
    private static readonly Lazy<PixelShader?> PaintShader = new(() => ShaderCompiler.LoadShader("Floe.ClickPaint.hlsl"));

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

    public static readonly DependencyProperty InwardProperty = Register(nameof(Inward), 0.0, 6);
    public double Inward { get => (double)GetValue(InwardProperty); set => SetValue(InwardProperty, value); }

    /// <summary>1 leaves the icon unbent under the glass, so a glass that stays on it never blurs it.</summary>
    public static readonly DependencyProperty SharpProperty = Register(nameof(Sharp), 0.0, 7);
    public double Sharp { get => (double)GetValue(SharpProperty); set => SetValue(SharpProperty, value); }

    /// <summary>1 shows the icon through the glass as an LED screen of light dots that bend with the lens.</summary>
    public static readonly DependencyProperty ScreenProperty = Register(nameof(Screen), 0.0, 8);
    public double Screen { get => (double)GetValue(ScreenProperty); set => SetValue(ScreenProperty, value); }

    /// <summary>
    /// Seconds into the active icon's loop; animate it from 0 to <see cref="LoopSeconds"/> forever. It drives
    /// <see cref="Progress"/> and <see cref="Inward"/> through <see cref="LoopAt"/>.
    /// </summary>
    public static readonly DependencyProperty LoopTimeProperty = DependencyProperty.Register(nameof(LoopTime), typeof(double), typeof(ClickGlassEffect),
        new PropertyMetadata(0.0, (d, e) =>
        {
            var effect = (ClickGlassEffect)d;
            var (progress, inward) = LoopAt((double)e.NewValue);
            effect.Progress = progress;
            effect.Inward = inward;
        }));
    public double LoopTime { get => (double)GetValue(LoopTimeProperty); set => SetValue(LoopTimeProperty, value); }

    public const double LoopSeconds = 2.8;
    private const double InwardSeconds = 1.2;

    /// <summary>
    /// The loop: the glass the click left hollows into a ring that runs in from the edge to the middle; there
    /// the drop opens again and swells out to the edge, as the click's first moments do; and round again.
    /// </summary>
    public static (double Progress, double Inward) LoopAt(double seconds)
    {
        double s = ((seconds % LoopSeconds) + LoopSeconds) % LoopSeconds;
        if (s < InwardSeconds)
        {
            double x = s / InwardSeconds;
            return (.30, x * x * (3 - (2 * x)));
        }

        double y = (s - InwardSeconds) / (LoopSeconds - InwardSeconds);
        // ease out: the drop bursts open, then settles against the edge
        return (.06 + (.24 * (1 - Math.Pow(1 - y, 2.2))), 0);
    }

    /// <param name="paint">true plays the click as swirling impasto brush strokes instead of glass.</param>
    public ClickGlassEffect(double iconSize, bool paint = false)
    {
        PixelShader = paint ? PaintShader.Value : Shader.Value;
        PaddingLeft = PaddingTop = PaddingRight = PaddingBottom = Pad;
        Box = new Point(iconSize + (2 * Pad), 0);
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(FieldProperty);
        foreach (var property in new[] { ProgressProperty, ToneProperty, BoxProperty, PaletteProperty, ColorAProperty, ColorBProperty, InwardProperty, SharpProperty, ScreenProperty })
        {
            UpdateShaderValue(property);
        }
    }

    public static bool IsAvailable => Shader.Value is not null;
    public static bool IsPaintAvailable => PaintShader.Value is not null;

    private static DependencyProperty Register<T>(string name, T value, int register) =>
        DependencyProperty.Register(name, typeof(T), typeof(ClickGlassEffect), new UIPropertyMetadata(value, PixelShaderConstantCallback(register)));
}
