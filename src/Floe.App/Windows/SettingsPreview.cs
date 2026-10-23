using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floe.App.Services;

namespace Floe.App.Windows;

/// <summary>--render-settings=&lt;png&gt; draws the settings panel offscreen, over a dark backdrop, at 2x.</summary>
public static class SettingsPreview
{
    public static bool TryRender(string[] args)
    {
        const string option = "--render-settings=";
        var argument = args.FirstOrDefault(a => a.StartsWith(option, StringComparison.OrdinalIgnoreCase));
        if (argument is null) return false;
        try
        {
            GlassSettings.Load();
            var window = new SettingsWindow();
            // --settings-glass opens the Cam tab
            if (args.Contains("--settings-glass", StringComparer.OrdinalIgnoreCase)) window.TabGlass.IsChecked = true;
            if (window.Content is not FrameworkElement content) return true;
            window.Content = null;
            var stage = new System.Windows.Controls.Border { Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x20, 0x2A)), Child = content };
            stage.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            stage.Arrange(new Rect(stage.DesiredSize));
            stage.UpdateLayout();
            var target = new RenderTargetBitmap((int)(stage.ActualWidth * 2), (int)(stage.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
            target.Render(stage);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(target));
            using var stream = File.Create(Path.GetFullPath(argument[option.Length..]));
            encoder.Save(stream);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }

        return true;
    }
}
