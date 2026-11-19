namespace Floe.Core.Services;

/// <summary>What a window is known by: its process, the exe behind it and, when Windows has one, its app id.</summary>
public readonly record struct WindowFacts(string ProcessName, string ProcessPath, string AppId);

/// <summary>
/// Decides which dock item a window belongs to. Only the process, its exe path, the app/package id and a short list
/// of known helper processes count: a window title naming an app is no evidence (a browser tab called "Steam" is
/// still the browser).
/// </summary>
public static class AppIdentity
{
    // one app known under several process names
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Code"] = "VisualStudioCode",
        ["Code - Insiders"] = "VisualStudioCode",
    };

    // helper processes that show windows on behalf of exactly one app
    private static readonly Dictionary<string, string> Helpers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["steamwebhelper"] = "steam",
        ["chrome_proxy"] = "chrome",
        ["msedge_proxy"] = "msedge",
        ["EpicWebHelper"] = "EpicGamesLauncher",
    };

    // Windows' own shell and system processes: their windows are never an app of their own
    private static readonly HashSet<string> ShellProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "dwm", "conhost", "taskhostw", "RuntimeBroker", "TextInputHost", "SearchHost", "SearchApp",
        "StartMenuExperienceHost", "ShellExperienceHost", "ShellHost", "Widgets", "WidgetService", "LockApp",
        "ApplicationFrameHost", "crashpad_handler", "SystemSettingsBroker", "ctfmon", "sihost",
    };

    /// <summary>The app a process name stands for: a helper becomes its app, an alias its one name.</summary>
    public static string CanonicalProcess(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var value = name.Trim();
        if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) value = value[..^4];
        if (Helpers.TryGetValue(value, out var owner)) value = owner;
        // a packaged id's tail (Company.App) names the app
        var dot = value.LastIndexOf('.');
        if (dot >= 0 && dot < value.Length - 1) value = value[(dot + 1)..];
        return Aliases.TryGetValue(value, out var alias) ? alias : value;
    }

    public static bool IsShellProcess(string name) => !string.IsNullOrWhiteSpace(name) && ShellProcesses.Contains(name.Trim());

    /// <summary>The AppUserModelID a shell:AppsFolder target launches, or empty.</summary>
    public static string TargetAppId(string? target)
    {
        if (string.IsNullOrWhiteSpace(target) || !target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        var value = target.TrimEnd('\\');
        var slash = value.LastIndexOf('\\');
        return slash >= 0 ? value[(slash + 1)..] : string.Empty;
    }

    /// <summary>The package's name (before the publisher hash) for a packaged target, or empty.</summary>
    public static string PackageName(string? target)
    {
        var id = TargetAppId(target);
        if (id.Length == 0) return string.Empty;
        var bang = id.IndexOf('!');
        if (bang >= 0) id = id[..bang];
        var underscore = id.IndexOf('_');
        // a Win32 app's id ("Chrome", "{GUID}\app.exe") has no package
        return underscore > 0 ? id[..underscore] : string.Empty;
    }

    private static string PackageFamily(string appId)
    {
        var bang = appId.IndexOf('!');
        return bang > 0 ? appId[..bang] : string.Empty;
    }

    /// <param name="itemProcess">The process the item launches (what <c>DockItemViewModel.ProcessName</c> holds).</param>
    /// <param name="itemTarget">The item's launch target: an exe path or a shell:AppsFolder id.</param>
    /// <param name="itemPath">The exe the item resolves to, when known.</param>
    public static bool Matches(string itemProcess, string? itemTarget, string? itemPath, WindowFacts window)
    {
        // the app id is the surest sign: packaged apps and apps that set their own id
        var appId = TargetAppId(itemTarget);
        if (appId.Length > 0 && window.AppId.Length > 0)
        {
            if (string.Equals(appId, window.AppId, StringComparison.OrdinalIgnoreCase)) return true;
            // two packaged ids settle it: one package can hold several apps (Package!Word, Package!Excel), and
            // the weaker signs below (the package folder, the process) would match all of them
            if (PackageFamily(appId).Length > 0 && PackageFamily(window.AppId).Length > 0) return false;
        }

        // the same exe
        if (!string.IsNullOrEmpty(itemPath) && window.ProcessPath.Length > 0
            && string.Equals(Path.GetFullPath(itemPath), window.ProcessPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // a packaged app runs out of its own folder under WindowsApps
        var package = PackageName(itemTarget);
        if (package.Length >= 4 && window.ProcessPath.Contains("\\" + package + "_", StringComparison.OrdinalIgnoreCase)) return true;

        var a = CanonicalProcess(itemProcess);
        var b = CanonicalProcess(window.ProcessName);
        return a.Length > 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The key a running, unpinned app is grouped and kept under: a packaged app by its id, anything else by the app
    /// its process stands for (a helper joins its app). Empty only when nothing names the window at all.
    /// </summary>
    public static string RunningKey(WindowFacts window)
    {
        if (PackageFamily(window.AppId).Length > 0) return "id:" + window.AppId.ToLowerInvariant();
        var name = CanonicalProcess(window.ProcessName);
        if (name.Length == 0 && window.ProcessPath.Length > 0) name = CanonicalProcess(Path.GetFileNameWithoutExtension(window.ProcessPath));
        return name.Length == 0 ? string.Empty : "proc:" + name.ToLowerInvariant();
    }

    /// <summary>The process name a helper's windows are shown under: its app's.</summary>
    public static string OwnerProcess(string name)
        => !string.IsNullOrWhiteSpace(name) && Helpers.TryGetValue(name.Trim(), out var owner) ? owner : name;

    /// <summary>The key a pinned item is known by: its whole target, so two packaged apps never collide.</summary>
    public static string PinnedKey(string? target, string processName)
        => !string.IsNullOrWhiteSpace(target) ? "target:" + target.Trim().ToLowerInvariant() : "proc:" + processName.ToLowerInvariant();
}
