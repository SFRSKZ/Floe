using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MacDock.App.Services;
using MacDock.Core.Interop;

namespace MacDock.App.Windows;

public partial class WeatherWidgetWindow : Window, IBubbleWidget
{
    private static readonly Dictionary<string, BitmapSource> IconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(15) };
    private readonly BubbleController _bubble;

    public bool IsBubbleDragging => _bubble.IsDragging;

    public bool Blur
    {
        get => _bubble.Blur;
        set => _bubble.Blur = value;
    }

    public WeatherWidgetWindow()
    {
        InitializeComponent();
        WidgetChrome.Apply(this, Root, Panel);
        _bubble = new BubbleController(this, Backdrop, BackdropBrush, RootScale) { LayoutId = "weather" };
        _timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) =>
        {
            await RefreshAsync();
            _timer.Start();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _bubble.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _bubble.Stop();
        base.OnClosed(e);
    }

    private async Task RefreshAsync()
    {
        var weather = await WeatherService.GetAsync();
        if (weather is null)
        {
            return;
        }

        WeatherTemp.Text = $"{Math.Round(weather.Temperature)}°";
        WeatherLocation.Text = weather.Location;

        var icon = LoadIcon(weather.Condition);
        if (icon is not null)
        {
            WeatherIconImage.Source = icon;
            WeatherIconImage.Visibility = Visibility.Visible;
            WeatherIcon.Visibility = Visibility.Collapsed;
        }
        else
        {
            WeatherIcon.Text = weather.Icon;
            WeatherIcon.Visibility = Visibility.Visible;
            WeatherIconImage.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Size of the grid that hosts <c>WeatherIconImage</c> in the XAML.</summary>
    private const double IconSizeDip = 46;

    private BitmapSource? LoadIcon(string condition)
    {
        // Dot-matrix weather glyph first; PNG files in %AppData% are only a fallback.
        double dpiScale = 1.0;
        try
        {
            dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        }
        catch
        {
            // no presentation source yet: 96 DPI is a fine default
        }

        var dotMatrix = DotMatrixIcons.Render(condition, IconSizeDip, Colors.White, dpiScale);
        if (dotMatrix is not null)
        {
            return dotMatrix;
        }

        if (IconCache.TryGetValue(condition, out var cached))
        {
            return cached;
        }

        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MacDock", "weather", "icons", condition + ".png");

            if (!File.Exists(path))
            {
                IconCache[condition] = null!;
                return null;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            IconCache[condition] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}


