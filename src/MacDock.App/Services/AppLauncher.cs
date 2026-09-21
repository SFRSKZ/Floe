using System.Diagnostics;
using System.IO;
using MacDock.Core.Interop;
using MacDock.Core.Models;

namespace MacDock.App.Services;

public readonly record struct LaunchResult(string Message, bool Started);

public static class AppLauncher
{
    public static LaunchResult LaunchOrActivate(DockApp app)
    {
        var target = ResolveTarget(app);
        if (string.IsNullOrWhiteSpace(target))
        {
            return new LaunchResult($"no-target:{app.Name}", false);
        }

        if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = target, UseShellExecute = true });
                return new LaunchResult($"launched:{target}", true);
            }
            catch (Exception ex)
            {
                return new LaunchResult($"launch-failed:{ex.Message}", false);
            }
        }

        var processName = Path.GetFileNameWithoutExtension(target);
        var existing = WindowFinder.FindMainWindow(processName);
        if (existing != IntPtr.Zero)
        {
            return WindowActivator.Activate(existing)
                ? new LaunchResult($"activated:{processName}", false)
                : new LaunchResult($"activate-failed:{processName}", false);
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                Arguments = app.Arguments ?? string.Empty,
                WorkingDirectory = string.IsNullOrWhiteSpace(app.WorkingDirectory)
                    ? Path.GetDirectoryName(target) ?? string.Empty
                    : Environment.ExpandEnvironmentVariables(app.WorkingDirectory),
                UseShellExecute = true,
            });

            return new LaunchResult($"launched:{processName}", true);
        }
        catch (Exception ex)
        {
            return new LaunchResult($"launch-failed:{processName}:{ex.Message}", false);
        }
    }

    /// <summary>Starts the target directly (no existing-window activation) - single-instance apps restore their window.</summary>
    public static LaunchResult LaunchFresh(DockApp app)
    {
        var target = ResolveTarget(app);
        if (string.IsNullOrWhiteSpace(target))
        {
            return new LaunchResult($"no-target:{app.Name}", false);
        }

        try
        {
            if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = target, UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    Arguments = app.Arguments ?? string.Empty,
                    WorkingDirectory = string.IsNullOrWhiteSpace(app.WorkingDirectory)
                        ? Path.GetDirectoryName(target) ?? string.Empty
                        : Environment.ExpandEnvironmentVariables(app.WorkingDirectory),
                    UseShellExecute = true,
                });
            }

            return new LaunchResult($"launched-fresh:{target}", true);
        }
        catch (Exception ex)
        {
            return new LaunchResult($"launch-failed:{ex.Message}", false);
        }
    }

    public static string ResolveTarget(DockApp app)
    {
        var target = Environment.ExpandEnvironmentVariables(app.Target ?? string.Empty).Trim().Trim('"');
        if (!string.IsNullOrWhiteSpace(target))
        {
            return target;
        }

        var icon = app.IconPath;
        if (!string.IsNullOrWhiteSpace(icon))
        {
            var comma = icon.LastIndexOf(',');
            var pathPart = comma > 0 ? icon[..comma] : icon;
            pathPart = Environment.ExpandEnvironmentVariables(pathPart.Trim().Trim('"'));
            if (File.Exists(pathPart))
            {
                return pathPart;
            }
        }

        return string.Empty;
    }
}
