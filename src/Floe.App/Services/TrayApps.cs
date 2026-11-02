using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floe.Core.Interop;
using Microsoft.Win32;

namespace Floe.App.Services;

/// <summary>One app that keeps an icon in the notification area and is running right now.</summary>
public sealed record TrayApp(string Name, string Path, ImageSource? Icon, int ProcessId);

/// <summary>
/// The apps Windows tucks behind the ^ in its notification area. Windows 11 keeps every tray icon it has seen
/// under HKCU\Control Panel\NotifyIconSettings, with the owner's path and a snapshot of the icon; the ones whose
/// program is running now are the apps working in the background.
/// </summary>
public static class TrayApps
{
    private const string SettingsKey = @"Control Panel\NotifyIconSettings";

    // the known folders the registry writes paths against, as GUID prefixes
    private static readonly (string Guid, Environment.SpecialFolder Folder)[] KnownFolders =
    {
        ("{6D809377-6AF0-444B-8957-A3773F02200E}", Environment.SpecialFolder.ProgramFiles),
        ("{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}", Environment.SpecialFolder.ProgramFilesX86),
        ("{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}", Environment.SpecialFolder.System),
        ("{F38BF404-1D43-42F2-9305-67DE0B28FC23}", Environment.SpecialFolder.Windows),
    };

    // the shell's own icons (volume, network, security...) are not apps to open
    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "rundll32.exe", "wlrmdr.exe", "SecurityHealthSystray.exe", "MoNotificationUx.exe", "Taskmgr.exe",
    };

    /// <summary>The running tray apps, by name. Reads the registry and the process list; call off the UI thread.</summary>
    public static IReadOnlyList<TrayApp> Load()
    {
        var running = RunningByPath();
        var self = Environment.ProcessId;
        var apps = new Dictionary<string, TrayApp>(StringComparer.OrdinalIgnoreCase);
        using var root = Registry.CurrentUser.OpenSubKey(SettingsKey);
        if (root is null) return Array.Empty<TrayApp>();

        foreach (var id in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(id);
            var path = Resolve(key?.GetValue("ExecutablePath") as string);
            if (key is null || string.IsNullOrEmpty(path) || apps.ContainsKey(path)) continue;
            if (Skipped.Contains(System.IO.Path.GetFileName(path))) continue;
            if (!running.TryGetValue(path, out var pid) || pid == self) continue;

            string name = key.GetValue("InitialTooltip") as string ?? string.Empty;
            name = string.IsNullOrWhiteSpace(name) ? Describe(path) : name.Split('\n')[0].Trim();
            apps[path] = new TrayApp(name, path, Snapshot(key.GetValue("IconSnapshot") as byte[]), pid);
        }

        return apps.Values.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Brings the app forward: its window if it has one, otherwise a second launch, which most tray apps answer by showing themselves.</summary>
    public static void Open(TrayApp app)
    {
        var window = AppWindows.Enumerate(Environment.ProcessId).FirstOrDefault(w => string.Equals(w.ProcessPath, app.Path, StringComparison.OrdinalIgnoreCase));
        if (window.Hwnd != IntPtr.Zero && WindowActivator.Activate(window.Hwnd)) return;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = app.Path, UseShellExecute = true, WorkingDirectory = System.IO.Path.GetDirectoryName(app.Path) ?? string.Empty });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Floe.Tray] open {app.Path}: {ex.Message}");
        }
    }

    public static void Reveal(TrayApp app)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{app.Path}\"", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Floe.Tray] reveal {app.Path}: {ex.Message}");
        }
    }

    /// <summary>Ends every process of the app, the way Task Manager's End task does.</summary>
    public static void Quit(TrayApp app)
    {
        foreach (var process in Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(app.Path)))
        {
            try
            {
                if (string.Equals(AppWindows.GetProcessPath((uint)process.Id), app.Path, StringComparison.OrdinalIgnoreCase))
                {
                    process.Kill();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Floe.Tray] quit {app.Path}: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static Dictionary<string, int> RunningByPath()
    {
        var running = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var path = AppWindows.GetProcessPath((uint)process.Id);
                if (!string.IsNullOrEmpty(path)) running.TryAdd(path, process.Id);
            }
            finally
            {
                process.Dispose();
            }
        }

        return running;
    }

    private static string Resolve(string? path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        foreach (var (guid, folder) in KnownFolders)
        {
            if (path.StartsWith(guid, StringComparison.OrdinalIgnoreCase))
            {
                return Environment.GetFolderPath(folder) + path[guid.Length..];
            }
        }

        return path;
    }

    private static string Describe(string path)
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
        }
        catch
        {
            // packaged apps' folders can refuse a read; the file name will do
        }

        return System.IO.Path.GetFileNameWithoutExtension(path);
    }

    private static ImageSource? Snapshot(byte[]? png)
    {
        if (png is not { Length: > 0 }) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(png);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
