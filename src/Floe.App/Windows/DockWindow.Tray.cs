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
        _tray.ToolTip = Loc.T("Arka planda çalışanlar");
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
        KeepTrayOnDock();
        window.Activate();
    }

    // the capsule falls back once the cursor leaves for the panel, so the panel goes down with it
    private void KeepTrayOnDock()
    {
        if (_trayWindow is not { IsLoaded: true } window) return;
        PlacePopover(window, DockAnchor(Canvas.GetLeft(_tray) + (TrayWidth / 2)), SettingsWindow.GetWorkArea(this), -2);
    }
}
