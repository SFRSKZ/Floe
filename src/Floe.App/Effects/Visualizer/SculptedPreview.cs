using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floe.App.Services;
using Floe.Core.Interop;

namespace Floe.App.Effects.Visualizer;

/// <summary>Deterministic real-renderer captures: --render-sculpted=directory; optional --sculpted-look=, --size=WxH, --frames=N, --no-frames, --compact, --stage-light.</summary>
public static class SculptedPreview
{
    public static bool TryRender(string[] args)
    {
        const string option = "--render-sculpted=";
        var arg = args.FirstOrDefault(a => a.StartsWith(option, StringComparison.OrdinalIgnoreCase));
        if (arg is null) return false;
        try
        {
            var dir = Path.GetFullPath(arg[option.Length..]); Directory.CreateDirectory(dir);
            const string tintOption = "--hologram-tint=";
            var tintArgument = args.FirstOrDefault(a => a.StartsWith(tintOption, StringComparison.OrdinalIgnoreCase));
            if (tintArgument is not null && Enum.TryParse<HologramColor>(tintArgument[tintOption.Length..], true, out var tint))
            {
                GlassSettings.PreviewTint(tint);
            }
            bool light = args.Contains("--stage-light");
            bool compact = args.Contains("--compact");
            int width = compact ? 420 : 1000, height = compact ? 76 : 150;
            const string sizeOption = "--size=";
            var sizeArgument = args.FirstOrDefault(a => a.StartsWith(sizeOption, StringComparison.OrdinalIgnoreCase));
            if (sizeArgument is not null)
            {
                var parts = sizeArgument[sizeOption.Length..].Split('x');
                if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h) && w > 0 && h > 0)
                {
                    width = w; height = h;
                }
            }
            const string lookOption = "--sculpted-look=";
            var lookArgument = args.FirstOrDefault(a => a.StartsWith(lookOption, StringComparison.OrdinalIgnoreCase));
            IEnumerable<SculptedLook> looks = Enum.GetValues<SculptedLook>();
            if (lookArgument is not null && Enum.TryParse<SculptedLook>(lookArgument[lookOption.Length..], true, out var onlyLook))
            {
                looks = new[] { onlyLook };
            }
            int frames = 150;
            const string framesOption = "--frames=";
            var framesArgument = args.FirstOrDefault(a => a.StartsWith(framesOption, StringComparison.OrdinalIgnoreCase));
            if (framesArgument is not null && int.TryParse(framesArgument[framesOption.Length..], out var f) && f > 0) frames = f;
            bool writeFrames = !args.Contains("--no-frames");
            using var report = new StreamWriter(Path.Combine(dir, "render-report.txt"));
            foreach (var look in looks)
            {
                var folder = Path.Combine(dir, look.ToString()); Directory.CreateDirectory(folder);
                var frame = new SpectrumFrame(24);
                var view = new SculptedVisualizer { Look = look, Width = width, Height = height, Capsule = new Rect(0, 8, width, height - 16), DebugFrame = frame };
                var canvas = new Canvas { Width = width, Height = height, Background = new SolidColorBrush(light ? Color.FromRgb(184, 196, 208) : Color.FromRgb(11, 14, 20)) };
                canvas.Children.Add(view); canvas.Measure(new Size(width, height)); canvas.Arrange(new Rect(0, 0, width, height));
                var times = new List<double>();
                for (int i = 0; i < frames; i++)
                {
                    double t = i / 30.0;
                    double beat = Math.Exp(-(t % .5) * 9);
                    bool sounding = i < frames / 2;
                    frame.Level = sounding ? (float)(.60 + .25 * beat) : 0;
                    frame.Bass = sounding ? (float)(.40 + .5 * beat) : 0;
                    for (int j = 0; j < frame.Bands.Length; j++) frame.Bands[j] = sounding ? (float)(.48 + .25 * Math.Sin(j * .6 + t * 1.8) + .15 * beat) : 0;
                    var clock = Stopwatch.StartNew();
                    view.Step(1 / 30.0); view.InvalidateVisual(); canvas.UpdateLayout();
                    var bmp = new RenderTargetBitmap(width * 2, height * 2, 192, 192, PixelFormats.Pbgra32); bmp.Render(canvas);
                    times.Add(clock.Elapsed.TotalMilliseconds);
                    if (writeFrames)
                    {
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bmp));
                        using var stream = File.Create(Path.Combine(folder, $"f{i:000}.png")); encoder.Save(stream);
                    }
                }
                times.Sort(); report.WriteLine($"{look}: median offscreen step/layout/render={times[times.Count/2]:F2}ms; p95={times[(int)(times.Count*.95)]:F2}ms (not live desktop FPS)");
                // Guard invalid layout and exercise switching on a populated host.
                view.Capsule = Rect.Empty; view.Look = SculptedLook.Ribbon; view.InvalidateVisual(); canvas.UpdateLayout();
            }
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
        return true;
    }
}
