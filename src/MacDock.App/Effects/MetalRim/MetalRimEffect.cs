// Ported from metal-fx by Jakub Antalik (MIT), https://github.com/Jakubantalik/Libraries.dev
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace MacDock.App.Effects.MetalRim;

/// <summary>
/// Chrome overlay; the centre is transparent. Place above the dock fill, set Size
/// to the element's pixel dimensions, and animate Time in seconds externally.
/// CornerRadius = -1 selects a capsule. Strength and BendStrength use 0..1.
/// The effect reserves 24 pixels outside the element for glow; do not clip it.
/// </summary>
public sealed class MetalRimEffect : ShaderEffect
{
    internal const int GlowPadding = 24;
    public bool IsAvailable { get; }
    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty(nameof(Input), typeof(MetalRimEffect), 0);
    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }

    // WPF cannot upload System.Windows.Size directly; its Point equivalent can.
    private static readonly DependencyProperty PixelSizeProperty = Register("PixelSize", new Point(640, 80), 7);
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(nameof(Size), typeof(Size), typeof(MetalRimEffect),
        new UIPropertyMetadata(new Size(640, 80), (d, e) =>
        {
            var size = (Size)e.NewValue;
            d.SetValue(PixelSizeProperty, new Point(size.Width, size.Height));
        }), v => v is Size s && double.IsFinite(s.Width) && double.IsFinite(s.Height) && s.Width > 0 && s.Height > 0);
    public Size Size { get => (Size)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    public static readonly DependencyProperty TimeProperty = Register(nameof(Time), 0.0, 0);
    public double Time { get => (double)GetValue(TimeProperty); set => SetValue(TimeProperty, value); }

    public static readonly DependencyProperty CursorXProperty = Register(nameof(CursorX), 0.5, 1);
    public double CursorX { get => (double)GetValue(CursorXProperty); set => SetValue(CursorXProperty, value); }

    public static readonly DependencyProperty CursorYProperty = Register(nameof(CursorY), 0.5, 2);
    public double CursorY { get => (double)GetValue(CursorYProperty); set => SetValue(CursorYProperty, value); }

    public static readonly DependencyProperty BendStrengthProperty = Register(nameof(BendStrength), 0.0, 3);
    public double BendStrength { get => (double)GetValue(BendStrengthProperty); set => SetValue(BendStrengthProperty, value); }

    public static readonly DependencyProperty StrengthProperty = Register(nameof(Strength), 1.0, 4);
    public double Strength { get => (double)GetValue(StrengthProperty); set => SetValue(StrengthProperty, value); }

    public static readonly DependencyProperty ThicknessProperty = Register(nameof(Thickness), 3.5, 5);
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    public static readonly DependencyProperty CornerRadiusProperty = Register(nameof(CornerRadius), -1.0, 6);
    public double CornerRadius { get => (double)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }

    public static readonly DependencyProperty GlowGainProperty = Register(nameof(GlowGain), 1.0, 8);
    public double GlowGain { get => (double)GetValue(GlowGainProperty); set => SetValue(GlowGainProperty, value); }

    public static readonly DependencyProperty TintColorProperty = Register(nameof(TintColor), Color.FromArgb(0x66, 255, 255, 255), 9);
    public Color TintColor { get => (Color)GetValue(TintColorProperty); set => SetValue(TintColorProperty, value); }

    public static readonly DependencyProperty HighlightColorProperty = Register(nameof(HighlightColor), Color.FromRgb(250, 250, 255), 10);
    public Color HighlightColor { get => (Color)GetValue(HighlightColorProperty); set => SetValue(HighlightColorProperty, value); }

    public static readonly DependencyProperty ShadowColorProperty = Register(nameof(ShadowColor), Color.FromRgb(26, 26, 26), 11);
    public Color ShadowColor { get => (Color)GetValue(ShadowColorProperty); set => SetValue(ShadowColorProperty, value); }

    public static readonly DependencyProperty SpeedProperty = Register(nameof(Speed), 1.0, 12);
    public double Speed { get => (double)GetValue(SpeedProperty); set => SetValue(SpeedProperty, value); }

    public static readonly DependencyProperty RepetitionProperty = Register(nameof(Repetition), 1.5, 13);
    public double Repetition { get => (double)GetValue(RepetitionProperty); set => SetValue(RepetitionProperty, value); }

    public static readonly DependencyProperty SoftnessProperty = Register(nameof(Softness), 0.05, 14);
    public double Softness { get => (double)GetValue(SoftnessProperty); set => SetValue(SoftnessProperty, value); }

    public static readonly DependencyProperty ShiftRedProperty = Register(nameof(ShiftRed), 0.3, 15);
    public double ShiftRed { get => (double)GetValue(ShiftRedProperty); set => SetValue(ShiftRedProperty, value); }

    public static readonly DependencyProperty ShiftBlueProperty = Register(nameof(ShiftBlue), 0.3, 16);
    public double ShiftBlue { get => (double)GetValue(ShiftBlueProperty); set => SetValue(ShiftBlueProperty, value); }

    public static readonly DependencyProperty DistortionProperty = Register(nameof(Distortion), 0.1, 17);
    public double Distortion { get => (double)GetValue(DistortionProperty); set => SetValue(DistortionProperty, value); }

    public static readonly DependencyProperty ContourProperty = Register(nameof(Contour), 0.4, 18);
    public double Contour { get => (double)GetValue(ContourProperty); set => SetValue(ContourProperty, value); }

    public static readonly DependencyProperty ShaderOpacityProperty = Register(nameof(ShaderOpacity), 0.88, 19);
    public double ShaderOpacity { get => (double)GetValue(ShaderOpacityProperty); set => SetValue(ShaderOpacityProperty, value); }

    public static readonly DependencyProperty GlowColorProperty = Register(nameof(GlowColor), Colors.White, 20);
    public Color GlowColor { get => (Color)GetValue(GlowColorProperty); set => SetValue(GlowColorProperty, value); }

    public static readonly DependencyProperty FlashXProperty = Register(nameof(FlashX), 0.0, 21);
    /// <summary>Where a drop hit the rim, in DIP from the capsule's left edge.</summary>
    public double FlashX { get => (double)GetValue(FlashXProperty); set => SetValue(FlashXProperty, value); }

    public static readonly DependencyProperty FlashAgeProperty = Register(nameof(FlashAge), -1.0, 22);
    /// <summary>Seconds since the drop hit; negative when there is no flash.</summary>
    public double FlashAge { get => (double)GetValue(FlashAgeProperty); set => SetValue(FlashAgeProperty, value); }

    // Ordered to match HLSL c0..c22; also used by the native diagnostic renderer.
    private static readonly DependencyProperty[] Constants =
    [
        TimeProperty, CursorXProperty, CursorYProperty, BendStrengthProperty, StrengthProperty,
        ThicknessProperty, CornerRadiusProperty, PixelSizeProperty, GlowGainProperty,
        TintColorProperty, HighlightColorProperty, ShadowColorProperty, SpeedProperty,
        RepetitionProperty, SoftnessProperty, ShiftRedProperty, ShiftBlueProperty,
        DistortionProperty, ContourProperty, ShaderOpacityProperty, GlowColorProperty,
        FlashXProperty, FlashAgeProperty
    ];

    public MetalRimEffect()
    {
        PixelShader = ShaderCompiler.CreatePixelShader(out bool available);
        IsAvailable = available;
        if (available)
            PaddingLeft = PaddingTop = PaddingRight = PaddingBottom = GlowPadding;
        UpdateShaderValue(InputProperty);
        foreach (var property in Constants) UpdateShaderValue(property);
    }

    private static DependencyProperty Register<T>(string name, T value, int register) =>
        DependencyProperty.Register(name, typeof(T), typeof(MetalRimEffect),
            new UIPropertyMetadata(value, PixelShaderConstantCallback(register)),
            v => v is not double number || (double.IsFinite(number) && Math.Abs(number) <= float.MaxValue));

    /// <summary>Returns a new independent effect configured from presets.ts (RGBA alpha is tint strength).</summary>
    public static MetalRimEffect ApplyPreset(string name, bool dark)
    {
        var effect = new MetalRimEffect();
        switch (name?.ToLowerInvariant())
        {
            case "silver":
                effect.TintColor = Color.FromArgb(dark ? (byte)0x66 : (byte)0x40, 255, 255, 255);
                effect.ShaderOpacity = dark ? 0.88 : 1;
                break;
            case "gold":
                effect.TintColor = dark ? Color.FromArgb(0xcc, 0xff, 0xcc, 0x55) : Color.FromArgb(0xaa, 0xf7, 0xd4, 0x88);
                effect.Speed = dark ? 0.85 : 1;
                effect.ShaderOpacity = dark ? 0.92 : 1;
                break;
            case "chromatic":
                effect.TintColor = dark ? Color.FromArgb(0x2e, 0x88, 0xcc, 0xff) : Color.FromArgb(0x99, 0x66, 0xb0, 0xff);
                effect.ShiftRed = effect.ShiftBlue = dark ? 0.75 : 0.6;
                effect.Repetition = dark ? 2 : 1.5;
                effect.Softness = dark ? 0.09 : 0.05;
                effect.ShaderOpacity = 1;
                break;
            default:
                throw new ArgumentException("Expected silver, gold or chromatic.", nameof(name));
        }
        effect.GlowColor = Color.FromRgb(effect.TintColor.R, effect.TintColor.G, effect.TintColor.B);
        return effect;
    }

    internal float[] GetShaderConstants()
    {
        var values = new float[Constants.Length * 4];
        for (int i = 0; i < Constants.Length; i++)
        {
            int offset = i * 4;
            switch (GetValue(Constants[i]))
            {
                case double number: values[offset] = (float)number; break;
                case Point point: values[offset] = (float)point.X; values[offset + 1] = (float)point.Y; break;
                case Color color:
                    values[offset] = color.R / 255f; values[offset + 1] = color.G / 255f;
                    values[offset + 2] = color.B / 255f; values[offset + 3] = color.A / 255f;
                    break;
            }
        }
        return values;
    }
}
