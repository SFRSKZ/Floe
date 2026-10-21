using Microsoft.Win32;

namespace MacDock.Core.Services;

/// <summary>
/// Store (MSIX) apps live in versioned folders under WindowsApps; an update installs a new
/// folder and removes the old one, which breaks any path pinned to the old version.
/// </summary>
public static class StorePackages
{
    private const string Marker = @"\WindowsApps\";

    /// <summary>Points a path inside a removed package version at the same file in the installed version.</summary>
    public static string Heal(string path)
    {
        var marker = path.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        if (marker < 0 || File.Exists(path) || Directory.Exists(path))
        {
            return path;
        }

        var rest = path[(marker + Marker.Length)..];
        var slash = rest.IndexOf('\\');
        var parts = (slash < 0 ? rest : rest[..slash]).Split('_');
        if (parts.Length < 5 || CurrentDir(parts[0], parts[^1]) is not { } current)
        {
            return path;
        }

        var healed = slash < 0 ? current : Path.Combine(current, rest[(slash + 1)..]);
        return File.Exists(healed) || Directory.Exists(healed) ? healed : path;
    }

    /// <summary>Install folder of the currently registered version of a package, from the per-user package repository.</summary>
    public static string? CurrentDir(string name, string publisherId)
    {
        using var packages = Registry.CurrentUser.OpenSubKey(
            @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
        if (packages is null)
        {
            return null;
        }

        foreach (var fullName in packages.GetSubKeyNames())
        {
            // resource packs (a resource id such as split.scale-150) carry no applications
            if (!fullName.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase)
                || !fullName.EndsWith("_" + publisherId, StringComparison.OrdinalIgnoreCase)
                || fullName.Split('_') is not { Length: 5 } fields
                || fields[3].Length > 0)
            {
                continue;
            }

            using var key = packages.OpenSubKey(fullName);
            if (key?.GetValue("PackageRootFolder") is string root && File.Exists(Path.Combine(root, "AppxManifest.xml")))
            {
                return root;
            }
        }

        return null;
    }
}
