using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Floe.Core.Interop;

namespace Floe.Plugins;

/// <summary>A short text in both of Floe's languages.</summary>
public sealed class LocalizedText
{
    public string En { get; init; } = string.Empty;
    public string Tr { get; init; } = string.Empty;

    /// <summary>Picks the Turkish text when it exists and the UI is Turkish; English otherwise.</summary>
    public string For(string language) =>
        language.StartsWith("tr", StringComparison.OrdinalIgnoreCase) && Tr.Length > 0 ? Tr : En;

    public override string ToString() => En;
}

/// <summary>The audio contract a plugin asks for. The host caps every field to sane limits.</summary>
public sealed class AudioContract
{
    public int Bands { get; init; } = 32;
    public int Wave { get; init; } = 512;
    public int WaveRateHz { get; init; } = 60;
}

/// <summary>Rendering hints; the host trusts these only within the documented ranges.</summary>
public sealed class RenderContract
{
    public bool Transparent { get; init; } = true;
    public double FrameBudgetMs { get; init; } = 16.6;
    public int MinHeight { get; init; } = 48;
    public int MaxPixels { get; init; } = 3_000_000;
}

/// <summary>One control the plugin wants in Floe's settings.</summary>
public sealed class PluginOption
{
    public string Id { get; init; } = string.Empty;
    public string Type { get; init; } = "number";
    public double? Min { get; init; }
    public double? Max { get; init; }
    public double? Step { get; init; }
    public JsonElement? Default { get; init; }
    public string[] Values { get; init; } = Array.Empty<string>();
    public LocalizedText? Label { get; init; }
    public Dictionary<string, LocalizedText> Labels { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>What a plugin folder says about itself (manifest.json).</summary>
public sealed class VisualizerPluginManifest
{
    public int SchemaVersion { get; init; } = 1;
    public string ApiVersion { get; init; } = "1.0";
    public string Id { get; init; } = string.Empty;
    public string Version { get; init; } = "0.0.0";
    public LocalizedText Name { get; init; } = new();
    public LocalizedText? Description { get; init; }
    public string? Author { get; init; }
    public string? License { get; init; }
    public string Entry { get; init; } = "index.html";
    public string? Preview { get; init; }
    public string[] Capabilities { get; init; } = Array.Empty<string>();
    public AudioContract Audio { get; init; } = new();
    public RenderContract Render { get; init; } = new();
    public PluginOption[] Options { get; init; } = Array.Empty<PluginOption>();
}

/// <summary>A validated plugin, ready to be hosted.</summary>
public sealed class VisualizerPlugin
{
    public required VisualizerPluginManifest Manifest { get; init; }
    public required string Folder { get; init; }
    public required string EntryPath { get; init; }
    public required string Source { get; init; }

    public string Id => Manifest.Id;

    public string DisplayName(string language) => Manifest.Name.For(language);

    public override string ToString() => $"{Manifest.Id} ({Manifest.Version})";
}

/// <summary>Outcome of reading one plugin folder; invalid folders never reach the host.</summary>
public sealed class VisualizerPluginLoad
{
    public VisualizerPlugin? Plugin { get; init; }
    public IReadOnlyList<string> Problems { get; init; } = Array.Empty<string>();

    public bool Ok => Plugin is not null;

    public static VisualizerPluginLoad Failed(params string[] problems) => new() { Problems = problems };
}

/// <summary>
/// Reads and validates a visualizer plugin folder. The rules here are the whole security boundary for
/// third-party content: a folder that fails them is never loaded.
/// </summary>
public static class VisualizerPluginReader
{
    /// <summary>The newest plugin API this build understands; a plugin's major version must match.</summary>
    public const string SupportedApiVersion = "1.0";

    public const int MaxManifestBytes = 512 * 1024;

    private static readonly Regex IdPattern = new(
        "^[a-z][a-z0-9-]{1,31}(\\.[a-z][a-z0-9-]{0,31})+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> KnownCapabilities = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio.bands",
        "audio.wave",
        "audio.onsets",
        "audio.stereo",
        "host.palette",
        "host.dockGeometry",
        "host.clock",
        "host.options",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static VisualizerPluginLoad Load(string folder, string source = "user")
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return VisualizerPluginLoad.Failed($"plugin folder missing: {folder}");
        }

        var manifestPath = Path.Combine(folder, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return VisualizerPluginLoad.Failed("manifest.json missing");
        }

        var problems = new List<string>();
        VisualizerPluginManifest? manifest;
        try
        {
            var size = new FileInfo(manifestPath).Length;
            if (size > MaxManifestBytes)
            {
                return VisualizerPluginLoad.Failed($"manifest.json too large ({size} bytes)");
            }

            manifest = JsonSerializer.Deserialize<VisualizerPluginManifest>(
                File.ReadAllText(manifestPath), JsonOptions);
        }
        catch (JsonException ex)
        {
            return VisualizerPluginLoad.Failed($"manifest.json is not valid JSON: {ex.Message}");
        }

        if (manifest is null)
        {
            return VisualizerPluginLoad.Failed("manifest.json is empty");
        }

        if (manifest.SchemaVersion != 1)
        {
            problems.Add($"unsupported schemaVersion {manifest.SchemaVersion}");
        }

        if (!IdPattern.IsMatch(manifest.Id))
        {
            problems.Add($"id '{manifest.Id}' must look like 'author.plugin' (lowercase, dots, dashes)");
        }

        if (manifest.Name.En.Length == 0 && manifest.Name.Tr.Length == 0)
        {
            problems.Add("name is empty");
        }

        var api = manifest.ApiVersion.Split('.');
        if (api.Length < 1 || !int.TryParse(api[0], out var apiMajor) || apiMajor != 1)
        {
            problems.Add($"apiVersion '{manifest.ApiVersion}' is not supported (host speaks {SupportedApiVersion})");
        }

        var entryPath = ResolveInside(folder, manifest.Entry);
        if (entryPath is null)
        {
            problems.Add($"entry '{manifest.Entry}' must stay inside the plugin folder");
        }
        else if (!File.Exists(entryPath))
        {
            problems.Add($"entry '{manifest.Entry}' does not exist");
        }
        else if (!entryPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("entry must be an .html file");
        }

        if (manifest.Preview is { Length: > 0 })
        {
            var preview = ResolveInside(folder, manifest.Preview);
            if (preview is null || !File.Exists(preview))
            {
                problems.Add($"preview '{manifest.Preview}' does not exist");
            }
        }

        foreach (var capability in manifest.Capabilities)
        {
            if (!KnownCapabilities.Contains(capability))
            {
                problems.Add($"unknown capability '{capability}'");
            }
        }

        CheckAudio(manifest.Audio, problems);
        CheckRender(manifest.Render, problems);
        CheckOptions(manifest.Options, problems);

        if (problems.Count > 0 || entryPath is null)
        {
            return new VisualizerPluginLoad { Problems = problems };
        }

        return new VisualizerPluginLoad
        {
            Plugin = new VisualizerPlugin
            {
                Manifest = manifest,
                Folder = Path.GetFullPath(folder),
                EntryPath = entryPath,
                Source = source,
            },
        };
    }

    private static void CheckAudio(AudioContract audio, List<string> problems)
    {
        if (audio.Bands is < 4 or > 64) problems.Add($"audio.bands {audio.Bands} must be 4..64");
        // API 1.x always ships the analyser's 512-sample scope buffer; a different count would be a lie.
        if (audio.Wave != SpectrumFrame.WaveLength)
        {
            problems.Add($"audio.wave must be {SpectrumFrame.WaveLength} in apiVersion 1.x (got {audio.Wave})");
        }

        if (audio.WaveRateHz is < 15 or > 120) problems.Add($"audio.waveRateHz {audio.WaveRateHz} must be 15..120");
    }

    private static void CheckRender(RenderContract render, List<string> problems)
    {
        if (render.FrameBudgetMs is < 4 or > 40) problems.Add($"render.frameBudgetMs {render.FrameBudgetMs} must be 4..40");
        if (render.MinHeight is < 24 or > 400) problems.Add($"render.minHeight {render.MinHeight} must be 24..400");
        if (render.MaxPixels is < 100_000 or > 16_000_000) problems.Add($"render.maxPixels {render.MaxPixels} is outside 100k..16M");
    }

    private static void CheckOptions(PluginOption[] options, List<string> problems)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in options)
        {
            if (!Regex.IsMatch(option.Id, "^[a-z][a-z0-9-]{1,31}$"))
            {
                problems.Add($"option id '{option.Id}' must be lowercase letters, digits and dashes");
                continue;
            }

            if (!ids.Add(option.Id))
            {
                problems.Add($"option id '{option.Id}' is declared twice");
            }

            switch (option.Type)
            {
                case "number":
                    if (option.Min is null || option.Max is null || option.Min >= option.Max)
                    {
                        problems.Add($"option '{option.Id}' needs min < max");
                    }

                    break;
                case "enum":
                    if (option.Values.Length == 0)
                    {
                        problems.Add($"option '{option.Id}' needs values");
                    }
                    else if (option.Default is { ValueKind: JsonValueKind.String } d &&
                             !option.Values.Contains(d.GetString(), StringComparer.OrdinalIgnoreCase))
                    {
                        problems.Add($"option '{option.Id}' default is not one of its values");
                    }

                    break;
                case "bool":
                    break;
                default:
                    problems.Add($"option '{option.Id}' has unknown type '{option.Type}'");
                    break;
            }
        }
    }

    /// <summary>Resolves a relative path, refusing anything that leaves the plugin folder.</summary>
    public static string? ResolveInside(string folder, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
        {
            return null;
        }

        var trimmed = relative.Replace('/', Path.DirectorySeparatorChar).Trim();
        if (Path.IsPathRooted(trimmed) || trimmed.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        var root = Path.GetFullPath(folder);
        var full = Path.GetFullPath(Path.Combine(root, trimmed));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}

