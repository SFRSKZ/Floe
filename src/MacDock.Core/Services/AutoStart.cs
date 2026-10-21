using Microsoft.Win32;

namespace MacDock.Core.Services;

public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MacDock";
    private const string PrefsKey = @"Software\MacDock";
    private const string OptOutValue = "AutoStartDisabled";

    // Called on every launch: registers autostart (refreshing the exe path) unless the
    // user turned it off from the menu.
    public static void EnsureDefault(string exePath)
    {
        // test builds run from %TEMP%; letting them register would start a stale build at login
        if (exePath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!IsOptedOut())
        {
            Enable(exePath);
        }
    }

    private static bool IsOptedOut()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PrefsKey, false);
            return key?.GetValue(OptOutValue) is int value && value != 0;
        }
        catch
        {
            return false;
        }
    }

    private static void SetOptedOut(bool optedOut)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(PrefsKey, true);
            key?.SetValue(OptOutValue, optedOut ? 1 : 0, RegistryValueKind.DWord);
        }
        catch
        {
            // best effort
        }
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(ValueName) is not null;
        }
        catch
        {
            return false;
        }
    }

    public static void Enable(string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            key?.SetValue(ValueName, $"\"{exePath}\"");
        }
        catch
        {
            // best effort
        }
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            key?.DeleteValue(ValueName, false);
        }
        catch
        {
            // best effort
        }
    }

    public static void Toggle()
    {
        if (IsEnabled())
        {
            Disable();
            SetOptedOut(true);
        }
        else if (Environment.ProcessPath is { } path)
        {
            Enable(path);
            SetOptedOut(false);
        }
    }
}
