using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MacDock.App.Windows;
using MacDock.Core.Interop;
using MacDock.Core.Services;

namespace MacDock.App;

public partial class App : Application
{
    private bool _hideTaskbar;
    private DispatcherTimer? _taskbarTimer;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (TryRunDiagnostic(e.Args))
        {
            Shutdown();
            return;
        }

        if (TryRenderSettings(e.Args))
        {
            Shutdown();
            return;
        }

        if (e.Args.Any(a => a.Equals("--dumpcolors", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var lines = new System.Text.StringBuilder();
                foreach (var app in MacDock.Core.Services.PinnedAppsProvider.Load())
                {
                    var icon = MacDock.App.Services.IconService.GetIcon(app);
                    var colors = MacDock.App.Services.IconService.GetIconColors(icon, 3);
                    lines.AppendLine($"{app.Name}: {colors[0]} {colors[1]} {colors[2]}");
                }

                System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "macdock-colors.log"), lines.ToString());
            }
            catch
            {
                // diagnostics must never crash
            }

            Shutdown();
            return;
        }

        var autoHide = e.Args.Any(a => a.Equals("--autohide", StringComparison.OrdinalIgnoreCase));
        _hideTaskbar = !e.Args.Any(a => a.Equals("--keep-taskbar", StringComparison.OrdinalIgnoreCase));

        if (_hideTaskbar)
        {
            TaskbarController.Hide();

            _taskbarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _taskbarTimer.Tick += (_, _) =>
            {
                if (_hideTaskbar && !TaskbarController.IsHidden())
                {
                    TaskbarController.Hide();
                }
            };
            _taskbarTimer.Start();

            Exit += (_, _) => TaskbarController.Show();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => TaskbarController.Show();
        }
        else
        {
            // recover from a previous forced kill that left the taskbar hidden
            TaskbarController.Show();
        }

        var blurArg = e.Args
            .FirstOrDefault(a => a.StartsWith("--blur=", StringComparison.OrdinalIgnoreCase))?["--blur=".Length..];
        if (blurArg is not null && double.TryParse(blurArg, out var blurRadius) && blurRadius > 0)
        {
            MacDock.App.Services.BlurSettings.Radius = blurRadius;
        }

        var renderPath = e.Args
            .FirstOrDefault(a => a.StartsWith("--render=", StringComparison.OrdinalIgnoreCase))?["--render=".Length..];

        if (renderPath is null
            && !e.Args.Any(a => a.Equals("--no-autostart", StringComparison.OrdinalIgnoreCase))
            && Environment.ProcessPath is { } exePath)
        {
            AutoStart.Enable(exePath);
        }

        var blur = renderPath is null
            && !e.Args.Any(a => a.Equals("--no-blur", StringComparison.OrdinalIgnoreCase));

        var dock = new DockWindow
        {
            AutoHide = renderPath is null && autoHide,
            BlurBackdrop = blur,
        };
        dock.Show();

        if (renderPath is not null)
        {
            var hoverArg = e.Args.FirstOrDefault(a => a.StartsWith("--hover=", StringComparison.OrdinalIgnoreCase))?["--hover=".Length..];
            dock.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                if (hoverArg is not null && double.TryParse(hoverArg, out var hoverX))
                {
                    dock.PreviewHover(hoverX);
                }

                var auraArg = e.Args
                    .FirstOrDefault(a => a.StartsWith("--aura=", StringComparison.OrdinalIgnoreCase))?["--aura=".Length..];
                if (auraArg is not null)
                {
                    dock.PreviewAura(auraArg);
                }

                var backgroundArg = e.Args
                    .FirstOrDefault(a => a.StartsWith("--renderbg=", StringComparison.OrdinalIgnoreCase))?["--renderbg=".Length..];
                RenderDock(dock, renderPath, backgroundArg);
                Shutdown();
            });
        }
    }

    private static bool TryRenderSettings(string[] args)
    {
        var path = args
            .FirstOrDefault(a => a.StartsWith("--rendersettings=", StringComparison.OrdinalIgnoreCase))?["--rendersettings=".Length..];
        if (path is null)
        {
            return false;
        }

        try
        {
            var window = new SettingsWindow();
            window.Show();
            window.UpdateLayout();

            var width = (int)Math.Ceiling(window.ActualWidth);
            var height = (int)Math.Ceiling(window.ActualHeight);
            var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            if (window.Content is Visual root)
            {
                target.Render(root);
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(target));
            using var stream = System.IO.File.Create(path);
            encoder.Save(stream);
        }
        catch
        {
            // diagnostics must never crash
        }

        return true;
    }

    private static void RenderDock(DockWindow dock, string path, string? backgroundPath)
    {
        var width = (int)Math.Ceiling(dock.Width);
        var height = (int)Math.Ceiling(dock.Height);

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            if (backgroundPath is not null && System.IO.File.Exists(backgroundPath))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(backgroundPath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                var left = dock.Left;
                var top = dock.Top;
                context.DrawImage(bitmap, new Rect(-left, -top, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight));
            }
            else
            {
                var background = new LinearGradientBrush(
                    Color.FromRgb(30, 90, 150),
                    Color.FromRgb(150, 70, 90),
                    45);
                context.DrawRectangle(background, null, new Rect(0, 0, width, height));
            }

            var brush = new VisualBrush(dock.Root)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
            };
            context.DrawRectangle(brush, null, new Rect(0, 0, width, height));
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        try
        {
            var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "macdock.log");
            System.IO.File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss} render: {dock.DebugInfo} rendered={width}x{height}{Environment.NewLine}");
        }
        catch
        {
            // ignore diagnostics failures
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = System.IO.File.Create(path);
        encoder.Save(stream);
    }

    private static bool TryRunDiagnostic(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith("--test-activate=", StringComparison.OrdinalIgnoreCase))
            {
                var processName = arg["--test-activate=".Length..];
                var hwnd = WindowFinder.FindMainWindow(processName);
                var activated = hwnd != IntPtr.Zero && WindowActivator.Activate(hwnd);
                Log($"test-activate '{processName}': hwnd={hwnd} activated={activated}");
                return true;
            }

            if (arg.StartsWith("--test-find=", StringComparison.OrdinalIgnoreCase))
            {
                var processName = arg["--test-find=".Length..];
                var hwnd = WindowFinder.FindMainWindow(processName);
                Log($"test-find '{processName}': hwnd={hwnd}");
                return true;
            }

            if (arg.StartsWith("--test-shellmenu=", StringComparison.OrdinalIgnoreCase))
            {
                var path = arg["--test-shellmenu=".Length..].Trim('"');
                var ok = ShellContextMenu.Show(IntPtr.Zero, path, 0, 0, null, showMenu: false);
                Log($"test-shellmenu '{path}': ok={ok}");
                return true;
            }

            if (arg.Equals("--test-windows", StringComparison.OrdinalIgnoreCase))
            {
                var self = System.Diagnostics.Process.GetCurrentProcess().Id;
                var windows = AppWindows.Enumerate(self);
                foreach (var name in windows.Select(w => w.ProcessName).Distinct().OrderBy(n => n))
                {
                    Log($"test-windows: {name}");
                }

                Log($"test-windows: total={windows.Count} distinct={windows.Select(w => w.ProcessName).Distinct().Count()}");
                return true;
            }
        }

        return false;
    }

    private static void Log(string message)
    {
        try
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "macdock.log");
            System.IO.File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
            // diagnostics must never crash the dock
        }
    }
}
