using System.Text.Json;
using Floe.Core.Models;

namespace Floe.Core.Services;

public static class PinnedAppsProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string DefaultConfigPath
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Floe", "pinned.json");

    public static IReadOnlyList<DockApp> Load(string? configPath = null)
    {
        configPath ??= DefaultConfigPath;

        if (File.Exists(configPath))
        {
            // An existing but empty list is a valid user choice (everything unpinned).
            var stored = ReadConfig(configPath, out var locked);
            if (stored is not null)
            {
                // store apps that updated themselves moved to a new folder: follow them and remember it
                if (HealStorePaths(stored) && !locked)
                {
                    Save(configPath, stored);
                }

                return stored;
            }

            if (locked)
            {
                // The file is fine but held by someone else (AV scan, second instance):
                // show the taskbar seeds for now but never overwrite the user's config.
                return SeedFromTaskbar();
            }

            // Unreadable or corrupt: keep a copy instead of silently overwriting it.
            try
            {
                File.Copy(configPath, configPath + ".bad", overwrite: true);
            }
            catch
            {
                // best effort
            }
        }

        var seeded = SeedFromTaskbar();
        Save(configPath, seeded);
        return seeded;
    }

    // Returns false instead of throwing so a locked or read-only config never crashes the dock.
    public static bool Save(string configPath, IReadOnlyList<DockApp> apps)
    {
        try
        {
            AtomicFile.WriteAllText(configPath, JsonSerializer.Serialize(apps, JsonOptions));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool HealStorePaths(List<DockApp> apps)
    {
        var changed = false;
        foreach (var app in apps)
        {
            var target = StorePackages.Heal(app.Target);
            var icon = app.IconPath is null ? null : StorePackages.Heal(app.IconPath);
            var folder = app.WorkingDirectory is null ? null : StorePackages.Heal(app.WorkingDirectory);
            changed |= target != app.Target || icon != app.IconPath || folder != app.WorkingDirectory;
            app.Target = target;
            app.IconPath = icon;
            app.WorkingDirectory = folder;
        }

        return changed;
    }

    private static List<DockApp>? ReadConfig(string configPath, out bool locked)
    {
        locked = false;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var json = File.ReadAllText(configPath);
                return JsonSerializer.Deserialize<List<DockApp>>(json, JsonOptions)?
                    .Where(app => app is not null)
                    .ToList();
            }
            catch (IOException) when (attempt < 3)
            {
                Thread.Sleep(100);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                locked = true;
                return null;
            }
            catch
            {
                // Corrupt or unexpected JSON.
                return null;
            }
        }
    }

    private static List<DockApp> SeedFromTaskbar()
    {
        var apps = new List<DockApp>();

        var pinnedDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft",
            "Internet Explorer",
            "Quick Launch",
            "User Pinned",
            "TaskBar");

        if (!Directory.Exists(pinnedDirectory))
        {
            return apps;
        }

        foreach (var lnk in Directory.EnumerateFiles(pinnedDirectory, "*.lnk").OrderBy(Path.GetFileName))
        {
            var app = ShortcutReader.Read(lnk);
            if (app is not null && (!string.IsNullOrWhiteSpace(app.Target) || !string.IsNullOrWhiteSpace(app.IconPath)))
            {
                apps.Add(app);
            }
        }

        return apps;
    }
}
