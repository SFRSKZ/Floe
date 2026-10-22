using System.Diagnostics;
using System.IO;
using System.Xml.Linq;
using Floe.Core.Interop;
using Floe.Core.Models;
using Floe.Core.Services;

namespace Floe.App.Services;

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
            if (StoreAppTarget(target) is { } store)
            {
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = store, UseShellExecute = true });
                return new LaunchResult($"launched:{store}", true);
            }

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
            else if (StoreAppTarget(target) is { } store)
            {
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = store, UseShellExecute = true });
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

    /// <summary>
    /// Store (MSIX) apps can't be started from their exe under WindowsApps ("access denied");
    /// maps such a path to shell:AppsFolder\{family}!{app id} read from the package manifest.
    /// </summary>
    private static string? StoreAppTarget(string exePath)
    {
        exePath = StorePackages.Heal(exePath);
        var marker = exePath.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return null;
        }

        var rest = exePath[(marker + @"\WindowsApps\".Length)..];
        var slash = rest.IndexOf('\\');
        if (slash <= 0)
        {
            return null;
        }

        // full name: Name_Version_Arch_ResourceId_PublisherId -> family: Name_PublisherId
        var packageDir = exePath[..(marker + @"\WindowsApps\".Length + slash)];
        var parts = rest[..slash].Split('_');
        if (parts.Length < 5)
        {
            return null;
        }

        var family = $"{parts[0]}_{parts[^1]}";
        var exeName = Path.GetFileName(exePath);
        var appId = "App";
        try
        {
            // store apps update themselves into a new versioned folder and drop the pinned one
            if (!File.Exists(Path.Combine(packageDir, "AppxManifest.xml")) && StorePackages.CurrentDir(parts[0], parts[^1]) is { } current)
            {
                packageDir = current;
            }

            var apps = XDocument.Load(Path.Combine(packageDir, "AppxManifest.xml")).Descendants()
                .Where(e => e.Name.LocalName == "Application")
                .Select(e => (Id: (string?)e.Attribute("Id"), Exe: (string?)e.Attribute("Executable")))
                .Where(a => a.Id is not null)
                .ToList();
            var match = apps.FirstOrDefault(a => string.Equals(Path.GetFileName(a.Exe ?? string.Empty), exeName, StringComparison.OrdinalIgnoreCase));
            match = match.Id is not null ? match : apps.FirstOrDefault(a => string.Equals(a.Id, Path.GetFileNameWithoutExtension(exeName), StringComparison.OrdinalIgnoreCase));
            match = match.Id is not null ? match : apps.FirstOrDefault();
            appId = match.Id ?? appId;
        }
        catch (Exception)
        {
            // unreadable manifest: "App" is the default id most packages use
        }

        return $@"shell:AppsFolder\{family}!{appId}";
    }

}
