using System.Text.Json;
using MacDock.Core.Models;

namespace MacDock.Core.Services;

public static class PinnedAppsProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string DefaultConfigPath
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MacDock", "pinned.json");

    public static IReadOnlyList<DockApp> Load(string? configPath = null)
    {
        configPath ??= DefaultConfigPath;

        if (File.Exists(configPath))
        {
            var stored = ReadConfig(configPath);
            if (stored.Count > 0)
            {
                return stored;
            }
        }

        var seeded = SeedFromTaskbar();
        Save(configPath, seeded);
        return seeded;
    }

    public static void Save(string configPath, IReadOnlyList<DockApp> apps)
    {
        var directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(configPath, JsonSerializer.Serialize(apps, JsonOptions));
    }

    private static List<DockApp> ReadConfig(string configPath)
    {
        try
        {
            var json = File.ReadAllText(configPath);
            return JsonSerializer.Deserialize<List<DockApp>>(json, JsonOptions) ?? new List<DockApp>();
        }
        catch
        {
            return new List<DockApp>();
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
