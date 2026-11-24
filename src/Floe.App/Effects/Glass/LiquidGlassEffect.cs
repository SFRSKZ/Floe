using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;
using Floe.App.Effects.MetalRim;

namespace Floe.App.Effects.Glass;

/// <summary>
/// The vibrancy pass plus a refracting bevel: the capsule's rim bends in the scene from just outside it, splits it into
/// a spectrum, mirrors a window in its curl and wears a thin oil-film rainbow.
/// </summary>
internal sealed class LiquidGlassEffect : ShaderEffect
{
    private static readonly Lazy<PixelShader?> Shader = new(() => ShaderCompiler.LoadShader("Floe.LiquidGlass.hlsl"));
    internal static bool IsAvailable => Shader.Value is not null;

    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty(nameof(Input), typeof(LiquidGlassEffect), 0, SamplingMode.Bilinear);
    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    public static readonly DependencyProperty SizeProperty = Register(nameof(Size), new Point(1280, 160), 0);
    public Point Size { get => (Point)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public static readonly DependencyProperty CapsuleProperty = Register(nameof(Capsule), new Point4D(0, 0, 640, 68), 1);
    public Point4D Capsule { get => (Point4D)GetValue(CapsuleProperty); set => SetValue(CapsuleProperty, value); }
    public static readonly DependencyProperty RadiusProperty = Register(nameof(Radius), 22.0, 2);
    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public static readonly DependencyProperty HoverProperty = Register(nameof(Hover), 0.0, 3);
    public double Hover { get => (double)GetValue(HoverProperty); set => SetValue(HoverProperty, value); }
    public static readonly DependencyProperty BulgeProperty = Register(nameof(Bulge), 0.0, 4);
    public double Bulge { get => (double)GetValue(BulgeProperty); set => SetValue(BulgeProperty, value); }
    public static readonly DependencyProperty PlateauProperty = Register(nameof(Plateau), 0.0, 5);
    public double Plateau { get => (double)GetValue(PlateauProperty); set => SetValue(PlateauProperty, value); }
    public static readonly DependencyProperty SigmaProperty = Register(nameof(Sigma), 60.0, 6);
    public double Sigma { get => (double)GetValue(SigmaProperty); set => SetValue(SigmaProperty, value); }
    public static readonly DependencyProperty RefractionProperty = Register(nameof(Refraction), 16.0, 7);
    public double Refraction { get => (double)GetValue(RefractionProperty); set => SetValue(RefractionProperty, value); }
    public static readonly DependencyProperty DispersionProperty = Register(nameof(Dispersion), 0.35, 8);
    public double Dispersion { get => (double)GetValue(DispersionProperty); set => SetValue(DispersionProperty, value); }
    public static readonly DependencyProperty BevelProperty = Register(nameof(Bevel), 18.0, 9);
    public double Bevel { get => (double)GetValue(BevelProperty); set => SetValue(BevelProperty, value); }
    public static readonly DependencyProperty IridescenceProperty = Register(nameof(Iridescence), 1.0, 10);
    public double Iridescence { get => (double)GetValue(IridescenceProperty); set => SetValue(IridescenceProperty, value); }
    public static readonly DependencyProperty SpecularProperty = Register(nameof(Specular), 1.0, 11);
    public double Specular { get => (double)GetValue(SpecularProperty); set => SetValue(SpecularProperty, value); }

    internal LiquidGlassEffect()
    {
        PixelShader = Shader.Value;
        foreach (var property in new[] { InputProperty, SizeProperty, CapsuleProperty, RadiusProperty, HoverProperty, BulgeProperty,
                     PlateauProperty, SigmaProperty, RefractionProperty, DispersionProperty, BevelProperty, IridescenceProperty, SpecularProperty })
            UpdateShaderValue(property);
    }

    private static DependencyProperty Register<T>(string name, T value, int register) =>
        DependencyProperty.Register(name, typeof(T), typeof(LiquidGlassEffect), new UIPropertyMetadata(value, PixelShaderConstantCallback(register)));
}
