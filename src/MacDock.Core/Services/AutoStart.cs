using Microsoft.Win32;

namespace MacDock.Core.Services;

public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MacDock";

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
        }
        else if (Environment.ProcessPath is { } path)
        {
            Enable(path);
        }
    }
}
