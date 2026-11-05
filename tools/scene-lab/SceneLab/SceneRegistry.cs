using System.Reflection;
using Floe.App.Effects.Gpu;
using Floe.App.Services;

namespace SceneLab;

/// <summary>Marks a lab scene with the one line the list and the report show.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SceneDocAttribute : Attribute
{
    public SceneDocAttribute(string summary) => Summary = summary;
    public string Summary { get; }
}

/// <summary>
/// Finds the scenes this build can draw: the app's own catalog looks (CyberSignal, CyberBlackwall …) for
/// comparison, and every <see cref="IGpuScene"/> class in the lab assembly — so a designer adds one file to
/// Scenes/ and it appears here, with no registration to edit.
/// </summary>
internal static class SceneRegistry
{
    public static IEnumerable<(string Name, string Description)> All()
    {
        foreach (var type in LabScenes())
        {
            string summary = type.GetCustomAttribute<SceneDocAttribute>()?.Summary ?? "";
            yield return (type.Name, summary);
        }

        foreach (var look in Enum.GetValues<VisualizerStyle>())
        {
            if (GpuSceneCatalog.Has(look)) yield return (look.ToString(), "app: " + look);
        }
    }

    public static IGpuScene Create(string name, out string description)
    {
        var type = LabScenes().FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (type is not null)
        {
            description = type.GetCustomAttribute<SceneDocAttribute>()?.Summary ?? "lab scene";
            return (IGpuScene)Activator.CreateInstance(type)!;
        }

        if (Enum.TryParse<VisualizerStyle>(name, true, out var look) && GpuSceneCatalog.Has(look))
        {
            description = "app catalog: " + look;
            return GpuSceneCatalog.Create(look);
        }

        throw new ArgumentException($"no scene called '{name}'; --list shows what this build has");
    }

    private static IEnumerable<Type> LabScenes() => typeof(SceneRegistry).Assembly.GetTypes()
        .Where(t => typeof(IGpuScene).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) is not null)
        .OrderBy(t => t.Name);
}
