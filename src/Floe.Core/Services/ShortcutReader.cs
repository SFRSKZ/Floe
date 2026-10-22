using System.Reflection;
using System.Runtime.InteropServices;
using Floe.Core.Models;

namespace Floe.Core.Services;

public static class ShortcutReader
{
    public static DockApp? Read(string lnkPath)
    {
        if (!File.Exists(lnkPath))
        {
            return null;
        }

        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return null;
        }

        object? shell = null;

        try
        {
            shell = Activator.CreateInstance(shellType);
            var shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
            if (shortcut is null)
            {
                return null;
            }

            var name = Path.GetFileNameWithoutExtension(lnkPath);

            return new DockApp
            {
                Name = name,
                Target = GetString(shortcut, "TargetPath"),
                Arguments = GetString(shortcut, "Arguments"),
                WorkingDirectory = GetString(shortcut, "WorkingDirectory"),
                IconPath = GetString(shortcut, "IconLocation"),
            };
        }
        catch
        {
            return null;
        }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.ReleaseComObject(shell);
            }
        }
    }

    private static string GetString(object target, string property)
        => target.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, null) as string ?? string.Empty;
}
