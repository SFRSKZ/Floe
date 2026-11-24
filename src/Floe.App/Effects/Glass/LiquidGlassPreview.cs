using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floe.App.Effects.ActiveIcon;
using Floe.App.Effects.MetalRim;

namespace Floe.App.Effects.Glass;

/// <summary>
/// Hidden diagnostic: runs the dock's liquid glass HLSL through D3D11 WARP over a backdrop picture and saves the
/// raw pass. --renderliquid=out.png;backdrop.png;hover;bulge;iridescence;specular[;capsuleLeft;capsuleWidth]
/// The backdrop is the window's picture at any pixel size; the shader works in the window's 1280x144 DIPs.
/// </summary>
internal static class LiquidGlassPreview
{
    internal static bool TryRender(string[] args)
    {
        const string option = "--renderliquid=";
        var argument = args.FirstOrDefault(a => a.StartsWith(option, StringComparison.OrdinalIgnoreCase));
        if (argument is null) return false;
        var parts = argument[option.Length..].Split(';');
        string output = Path.GetFullPath(parts[0]);
        try
        {
            if (ShaderCompiler.LoadShader("Floe.LiquidGlass.hlsl") is null)
                throw new InvalidOperationException("LiquidGlass ps_3_0 compilation failed.");
            var backdrop = new FormatConvertedBitmap(new BitmapImage(new Uri(Path.GetFullPath(parts[1]))), PixelFormats.Pbgra32, null, 0);
            float Arg(int index, float fallback) => parts.Length > index && float.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
            float hover = Arg(2, 640), bulge = Arg(3, 0), iridescence = Arg(4, 1), specular = Arg(5, 1);
            float left = Arg(6, 190), width = Arg(7, 900);
            using var renderer = ClickGlassRenderer.Liquid(backdrop);
            var frame = renderer.Render(new float[]
            {
                1280, 144, 0, 0,
                left, 52, width, 68,
                22, 0, 0, 0,
                hover, 0, 0, 0,
                bulge, 0, 0, 0,
                0, 0, 0, 0,
                52, 0, 0, 0,
                16, 0, 0, 0,
                Arg(8, .35f), 0, 0, 0,
                18, 0, 0, 0,
                iridescence, 0, 0, 0,
                specular, 0, 0, 0,
            });
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using var stream = File.Create(output);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(frame));
            encoder.Save(stream);
        }
        catch (Exception ex)
        {
            ShaderCompiler.Log($"Liquid preview failed: {ex}");
            try { File.WriteAllText(output + ".error.txt", ex.ToString()); } catch { }
            Environment.ExitCode = 1;
        }
        return true;
    }
}
