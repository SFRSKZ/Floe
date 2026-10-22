using System.Reflection;
using System.Runtime.InteropServices;

namespace Floe.Core.Services;

public static class ShellVerbs
{
    public static IReadOnlyList<(string Name, Action Invoke)> Get(string path)
    {
        var result = new List<(string, Action)>();

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return result;
        }

        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null)
        {
            return result;
        }

        object? shell = null;

        try
        {
            shell = Activator.CreateInstance(shellType);
            var folder = shellType.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, shell, new object[] { Path.GetDirectoryName(path)! });
            if (folder is null)
            {
                return result;
            }

            var item = folder.GetType().InvokeMember("ParseName", BindingFlags.InvokeMethod, null, folder, new object[] { Path.GetFileName(path) });
            if (item is null)
            {
                return result;
            }

            var verbs = item.GetType().InvokeMember("Verbs", BindingFlags.InvokeMethod, null, item, null);
            if (verbs is null)
            {
                return result;
            }

            var countObj = verbs.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, verbs, null);
            var count = countObj is int c ? c : 0;
            for (var i = 0; i < count; i++)
            {
                var verb = verbs.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, verbs, new object[] { i });
                if (verb is null)
                {
                    continue;
                }

                var rawName = verb.GetType().InvokeMember("Name", BindingFlags.GetProperty, null, verb, null) as string;
                if (string.IsNullOrWhiteSpace(rawName))
                {
                    continue;
                }

                var captured = verb;
                var name = rawName.Replace("&", string.Empty);
                result.Add((name, () =>
                {
                    try
                    {
                        captured.GetType().InvokeMember("DoIt", BindingFlags.InvokeMethod, null, captured, null);
                    }
                    catch
                    {
                        // ignore verb failures
                    }
                }));
            }
        }
        catch
        {
            // shell verbs are best-effort
        }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.ReleaseComObject(shell);
            }
        }

        return result;
    }
}
