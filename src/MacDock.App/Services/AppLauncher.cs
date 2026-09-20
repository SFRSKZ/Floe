using System.Diagnostics;
using System.IO;
using MacDock.Core.Interop;
using MacDock.Core.Models;

namespace MacDock.App.Services;

public static class AppLauncher
{
    public static string LaunchOrActivate(DockApp app)
    {
        var target = ResolveTarget(app);
        if (string.IsNullOrWhiteSpace(target))
        {
            return $"no-target:{app.Name}";
        }

        if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = target, UseShellExecute = true });
                return $"launched:{target}";
            }
            catch (Exception ex)
            {
                return $"launch-failed:{ex.Message}";
            }
        }

        var processName = Path.GetFileNameWithoutExtension(target);
        var existing = WindowFinder.FindMainWindow(processName);
        if (existing != IntPtr.Zero)
        {
            return WindowActivator.Activate(existing)
                ? $"activated:{processName}"
                : $"activate-failed:{processName}";
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

            return $"launched:{processName}";
        }
        catch (Exception ex)
        {
            return $"launch-failed:{processName}:{ex.Message}";
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
