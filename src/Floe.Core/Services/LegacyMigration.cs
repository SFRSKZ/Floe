using Microsoft.Win32;

namespace Floe.Core.Services;

/// <summary>
/// Floe used to be called MacDock. On the first start under the new name, the settings folder and the
/// registry preferences are carried over and the old login entry is dropped (AutoStart registers Floe's own).
/// </summary>
public static class LegacyMigration
{
    private const string OldName = "MacDock";
    private const string NewName = "Floe";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Run(string? exePath)
    {
        CopySettings();
        CopyPreferences();
        // A test build in %TEMP% does not register autostart, so it must not remove the old entry either.
        if (exePath is not null && !exePath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
        {
            DropOldLoginEntry();
        }
    }

    private static void CopySettings()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var from = Path.Combine(appData, OldName);
            var to = Path.Combine(appData, NewName);
            if (Directory.Exists(to) || !Directory.Exists(from)) return;
            CopyDirectory(from, to);
        }
        catch
        {
            // best effort: Floe starts with defaults
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), false);
        }

        foreach (var directory in Directory.GetDirectories(from))
        {
            CopyDirectory(directory, Path.Combine(to, Path.GetFileName(directory)));
        }
    }

    private static void CopyPreferences()
    {
        try
        {
            using var existing = Registry.CurrentUser.OpenSubKey(@"Software\" + NewName, false);
            if (existing is not null) return;
            using var from = Registry.CurrentUser.OpenSubKey(@"Software\" + OldName, false);
            if (from is null) return;
            using var to = Registry.CurrentUser.CreateSubKey(@"Software\" + NewName, true);
            foreach (var name in from.GetValueNames())
            {
                if (from.GetValue(name) is { } value) to.SetValue(name, value, from.GetValueKind(name));
            }
        }
        catch
        {
            // best effort
        }
    }

    private static void DropOldLoginEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            key?.DeleteValue(OldName, false);
        }
        catch
        {
            // best effort
        }
    }
}
