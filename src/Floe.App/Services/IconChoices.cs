using System.IO;
using System.Text.Json;
using Floe.Core.Models;

namespace Floe.App.Services;

/// <summary>The icon picked for one app from its right click menu; an app with no choice follows the automatic match.</summary>
public static class IconChoices
{
    private const string Real = "real";
    private const string SetPrefix = "set:";
    private const string CardPrefix = "card:";
    private const string FilePrefix = "file:";

    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Floe.Core.Services.Product.Folder,
        "icons.json");

    private static readonly Lazy<Dictionary<string, string>> Choices = new(Read);

    public static bool HasChoice(DockApp app) => Choices.Value.ContainsKey(Key(app));

    public static bool UsesReal(DockApp app) => Get(app) == Real;

    /// <summary>The outline icon's file name when one was picked.</summary>
    public static string? SetIcon(DockApp app) => Value(app, SetPrefix);

    /// <summary>The Cyberpunk card's file name when one was picked.</summary>
    public static string? CardIcon(DockApp app) => Value(app, CardPrefix);

    /// <summary>The image file when one was picked.</summary>
    public static string? File(DockApp app) => Value(app, FilePrefix);

    public static void ChooseSetIcon(DockApp app, string file) => Store(app, SetPrefix + file);

    public static void ChooseCardIcon(DockApp app, string file) => Store(app, CardPrefix + file);

    public static void ChooseFile(DockApp app, string path) => Store(app, FilePrefix + path);

    public static void ChooseReal(DockApp app) => Store(app, Real);

    public static void Clear(DockApp app) => Store(app, null);

    private static string? Get(DockApp app) => Choices.Value.GetValueOrDefault(Key(app));

    private static string? Value(DockApp app, string prefix) =>
        Get(app) is { } value && value.StartsWith(prefix, StringComparison.Ordinal) ? value[prefix.Length..] : null;

    private static string Key(DockApp app) => (string.IsNullOrWhiteSpace(app.Target) ? app.Name : app.Target).ToLowerInvariant();

    private static void Store(DockApp app, string? value)
    {
        if (value is null) Choices.Value.Remove(Key(app));
        else Choices.Value[Key(app)] = value;

        try
        {
            Floe.Core.Services.AtomicFile.WriteAllText(ConfigPath, JsonSerializer.Serialize(Choices.Value));
        }
        catch
        {
            // Settings are best effort; the in-memory choice still applies for this run.
        }
    }

    private static Dictionary<string, string> Read()
    {
        try
        {
            if (System.IO.File.Exists(ConfigPath))
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(System.IO.File.ReadAllText(ConfigPath)) ?? new();
            }
        }
        catch
        {
            // A bad file must not take the icons with it.
        }

        return new();
    }
}
