namespace MacDock.App.Services;

public static class BlurSettings
{
    /// <summary>Backdrop blur radius (DIP). Tunable via --blur=NN.</summary>
    public static double Radius { get; set; } = 22;

    /// <summary>Opacity of the acrylic grain overlay (0..1).</summary>
    public static double GrainOpacity { get; set; } = 0.05;
}
