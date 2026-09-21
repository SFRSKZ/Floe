using System;
using System.Collections.Generic;
using System.Linq;
using MacDock.Core.Interop;

namespace MacDock.App.Services;

public static class AppMatcher
{
    private static readonly Dictionary<string, string> ProcessAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Code", "VisualStudioCode" },
        { "Code - Insiders", "VisualStudioCode" },
        { "chrome_proxy", "chrome" },
    };

    public static bool ProcessMatches(string itemProcess, string windowProcess)
    {
        var a = NormalizeProcess(itemProcess);
        var b = NormalizeProcess(windowProcess);
        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }

        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // helper sub-processes start with the app name: steamwebhelper -> Steam
        return a.Length >= 4 && b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
    }

    public static bool WindowMatchesItem(string itemName, string itemProcess, string? itemTarget, AppWindow window)
        => Matches(itemName, itemProcess, itemTarget, window.ProcessName, window.Title, window.ProcessPath);

    public static bool Matches(string itemName, string itemProcess, string? itemTarget, string process, string title, string path)
    {
        if (ProcessMatches(itemProcess, process))
        {
            return true;
        }

        if (TitleMatches(itemName, title))
        {
            return true;
        }

        var package = PackageName(itemTarget);
        return package.Length >= 4 && path.Contains(package, StringComparison.OrdinalIgnoreCase);
    }

    public static bool TitleMatches(string itemName, string title)
    {
        var a = NormalizeText(itemName);
        var b = NormalizeText(title);
        return a.Length >= 4 && b.Contains(a, StringComparison.Ordinal);
    }

    public static string PackageName(string? target)
    {
        if (string.IsNullOrWhiteSpace(target) || !target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var value = target;
        var slash = value.LastIndexOf('\\');
        if (slash >= 0)
        {
            value = value[(slash + 1)..];
        }

        var bang = value.IndexOf('!');
        if (bang >= 0)
        {
            value = value[..bang];
        }

        var underscore = value.IndexOf('_');
        if (underscore > 0)
        {
            value = value[..underscore];
        }

        return value;
    }

    private static string NormalizeProcess(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var dot = name.LastIndexOf('.');
        var value = dot >= 0 && dot < name.Length - 1 ? name[(dot + 1)..] : name;
        return ProcessAliases.TryGetValue(value, out var alias) ? alias : value;
    }

    private static string NormalizeText(string value)
        => string.IsNullOrEmpty(value)
            ? string.Empty
            : new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
