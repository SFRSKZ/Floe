using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Floe.App.Services;

namespace Floe.App.Windows;

/// <summary>The ^ before the clock, where Windows keeps its hidden icons: it opens the background apps' panel.</summary>
public partial class DockWindow
{
    private const double TrayWidth = 28;
    private const double TrayGlyph = 22;

    private readonly Grid _tray = new() { Width = TrayWidth, Height = StartWidth, Background = Brushes.Transparent, Cursor = Cursors.Hand };
    private TrayWindow? _trayWindow;
    private DateTime _trayClosed = DateTime.MinValue;

    private void InitializeTray()
    {
        var glyph = Glyph("chevron", TrayGlyph);
        _inkGlyphs.Add((glyph, "chevron", TrayGlyph));
        glyph.Opacity = 0.82;
        _tray.ToolTip = "Arka planda çalışanlar";
        _tray.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(1, 1);
        _tray.RenderTransform = scale;
        _tray.Children.Add(glyph);
        _tray.MouseEnter += (_, _) => AnimateGlyph(scale, glyph, 1.12, 1);
        _tray.MouseLeave += (_, _) => AnimateGlyph(scale, glyph, 1, 0.82);
        _tray.MouseLeftButtonUp += async (_, _) => await ToggleTrayAsync();
        _clockLayer.Children.Add(_tray);
        Closed += (_, _) => _trayWindow?.Close();
    }

    private async Task ToggleTrayAsync()
    {
        // the click that takes focus from an open panel closes it first; don't reopen it at once
        if (_trayWindow is not null || (DateTime.UtcNow - _trayClosed).TotalMilliseconds < 300) return;

        var apps = await Task.Run(TrayApps.Load);
        var window = new TrayWindow();
        window.SetApps(apps);
        window.Closed += (_, _) =>
        {
            _trayWindow = null;
            _trayClosed = DateTime.UtcNow;
        };
        _trayWindow = window;

        window.Show();
        window.UpdateLayout();
        var work = SettingsWindow.GetWorkArea(this);
        window.Left = Math.Clamp(
            ToScreenX(Canvas.GetLeft(_tray) + (TrayWidth / 2)) - (window.ActualWidth / 2),
            work.Left, Math.Max(work.Left, work.Right - window.ActualWidth));
        // the panel's shadow margin (14) lets it sit a touch closer to the dock
        window.Top = Math.Clamp(Top + RestTop - window.ActualHeight + 2,
            work.Top, Math.Max(work.Top, work.Bottom - window.ActualHeight));
        window.Activate();
    }
}
