using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Floe.App.Services;

namespace Floe.App.Windows;

/// <summary>
/// The dock's take on Windows' hidden icons: a small glass panel above the ^ button with every app running in
/// the background behind a tray icon. A click opens the app, a right click offers to show its file or end it.
/// It closes as soon as it loses focus.
/// </summary>
internal sealed class TrayWindow : Window
{
    private const int Columns = 5;
    private const double Cell = 44;
    private const double IconSize = 26;

    private readonly WrapPanel _icons = new() { Width = Columns * Cell };
    private readonly TextBlock _empty = new()
    {
        Text = "Arka planda çalışan uygulama yok",
        Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
        FontSize = 12,
        Margin = new Thickness(4, 6, 4, 6),
        Visibility = Visibility.Collapsed,
    };

    private bool _closing;

    public TrayWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "Floe Tray";

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = "Arka planda çalışanlar",
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(4, 0, 4, 8),
        });
        stack.Children.Add(_icons);
        stack.Children.Add(_empty);

        Content = new Border
        {
            Margin = new Thickness(14),
            Padding = new Thickness(10, 10, 10, 8),
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.FromArgb(0xEB, 0x16, 0x19, 0x21)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = .45, Color = Colors.Black },
            Child = stack,
        };

        Deactivated += (_, _) => CloseOnce();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) CloseOnce();
        };
    }

    /// <summary>Fills the panel; the apps come from <see cref="TrayApps.Load"/>.</summary>
    public void SetApps(IReadOnlyList<TrayApp> apps)
    {
        _icons.Children.Clear();
        foreach (var app in apps) _icons.Children.Add(Item(app));
        _empty.Visibility = apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _icons.Visibility = apps.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private FrameworkElement Item(TrayApp app)
    {
        var hover = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
        var cell = new Border
        {
            Width = Cell - 4,
            Height = Cell - 4,
            Margin = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = app.Name,
        };

        if (app.Icon is not null)
        {
            var image = new Image { Source = app.Icon, Width = IconSize, Height = IconSize };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            cell.Child = image;
        }
        else
        {
            cell.Child = new TextBlock
            {
                Text = app.Name.Length > 0 ? app.Name[..1].ToUpperInvariant() : "?",
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        cell.MouseEnter += (_, _) => cell.Background = hover;
        cell.MouseLeave += (_, _) => cell.Background = Brushes.Transparent;
        cell.MouseLeftButtonUp += (_, _) =>
        {
            CloseOnce();
            TrayApps.Open(app);
        };

        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("Aç", () => { Finish(); TrayApps.Open(app); }));
        menu.Items.Add(MenuItem("Dosya konumunu aç", () => { Finish(); TrayApps.Reveal(app); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Sonlandır", () => { Finish(); TrayApps.Quit(app); }));
        // a menu dismissed without a choice leaves the panel without focus, so nothing else would close it
        menu.Closed += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (!IsActive) CloseOnce();
        });
        cell.ContextMenu = menu;
        return cell;
    }

    private static MenuItem MenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void CloseOnce()
    {
        // the context menu takes focus from the panel; only close when neither has it
        if (IsAnyMenuOpen()) return;
        Finish();
    }

    private void Finish()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    private bool IsAnyMenuOpen() => _icons.Children.OfType<FrameworkElement>().Any(c => c.ContextMenu is { IsOpen: true });
}
