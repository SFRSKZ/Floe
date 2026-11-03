using System.Text.Json;
using Floe.Core.Interop;
using Floe.Plugins;

namespace Floe.Tests;

public sealed class VisualizerPluginTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "floe-plugin-tests-" + Guid.NewGuid().ToString("N"));

    public VisualizerPluginTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    [Fact]
    public void A_complete_manifest_loads()
    {
        var folder = WritePlugin("floe.tidepool", entry: "index.html", extra: "\"capabilities\":[\"audio.bands\",\"host.palette\"]");

        var load = VisualizerPluginReader.Load(folder);

        Assert.True(load.Ok, string.Join("; ", load.Problems));
        Assert.Equal("floe.tidepool", load.Plugin!.Id);
        Assert.Equal("Tidepool", load.Plugin.Manifest.Name.En);
        Assert.Equal("Gelgit", load.Plugin.Manifest.Name.Tr);
        Assert.EndsWith("index.html", load.Plugin.EntryPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(32, load.Plugin.Manifest.Audio.Bands);
    }

    [Theory]
    [InlineData("TidePool")]
    [InlineData("tidepool")]
    [InlineData("floe..tidepool")]
    [InlineData("../tidepool")]
    public void Bad_ids_are_rejected(string id)
    {
        var folder = WritePlugin(id);

        var load = VisualizerPluginReader.Load(folder);

        Assert.False(load.Ok);
    }

    [Fact]
    public void An_entry_that_leaves_the_folder_is_rejected()
    {
        var folder = WritePlugin("floe.escape", entry: "../outside.html");
        File.WriteAllText(Path.Combine(_root, "outside.html"), "<html></html>");

        var load = VisualizerPluginReader.Load(folder);

        Assert.False(load.Ok);
        Assert.Contains(load.Problems, p => p.Contains("inside", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_unsupported_api_version_is_rejected()
    {
        var folder = WritePlugin("floe.future", api: "2.0");

        var load = VisualizerPluginReader.Load(folder);

        Assert.False(load.Ok);
        Assert.Contains(load.Problems, p => p.Contains("apiVersion", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_unknown_capability_is_rejected()
    {
        var folder = WritePlugin("floe.capabilities", extra: "\"capabilities\":[\"filesystem.read\"]");

        var load = VisualizerPluginReader.Load(folder);

        Assert.False(load.Ok);
        Assert.Contains(load.Problems, p => p.Contains("capability", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Duplicate_option_ids_are_rejected()
    {
        var options = "\"options\":[{\"id\":\"glow\",\"type\":\"bool\"},{\"id\":\"glow\",\"type\":\"bool\"}]";
        var folder = WritePlugin("floe.options", extra: options);

        var load = VisualizerPluginReader.Load(folder);

        Assert.False(load.Ok);
        Assert.Contains(load.Problems, p => p.Contains("twice", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_manifest_without_its_entry_is_rejected()
    {
        var folder = WritePlugin("floe.noentry");
        File.Delete(Path.Combine(folder, "index.html"));

        var load = VisualizerPluginReader.Load(folder);

        Assert.False(load.Ok);
        Assert.Contains(load.Problems, p => p.Contains("does not exist", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_catalog_prefers_the_user_folder_and_reports_the_rest()
    {
        var user = Path.Combine(_root, "user");
        var shipped = Path.Combine(_root, "shipped");
        Directory.CreateDirectory(user);
        Directory.CreateDirectory(shipped);
        WritePlugin("floe.same", root: user, name: "User copy");
        WritePlugin("floe.same", root: shipped, name: "Shipped copy");
        WritePlugin("floe.broken", root: shipped, idOverride: "Not An Id");

        var catalog = VisualizerPluginCatalog.Discover(new[] { user, shipped });

        Assert.Single(catalog.Plugins);
        Assert.Equal("User copy", catalog.Plugins[0].Manifest.Name.En);
        Assert.Contains(catalog.Rejected, r => r.Folder.Contains("floe_broken", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Settings_round_trip_with_options_and_disabled_ids()
    {
        var path = Path.Combine(_root, "plugins.json");
        var settings = new VisualizerPluginSettings { SelectedId = "floe.tidepool", Quality = "high" };
        settings.SetEnabled("floe.other", false);
        using var document = JsonDocument.Parse("0.75");
        settings.SetOption("floe.tidepool", "intensity", document.RootElement.Clone());
        settings.Save(path);

        var loaded = VisualizerPluginSettings.Load(path);

        Assert.Equal("floe.tidepool", loaded.SelectedId);
        Assert.Equal("high", loaded.Quality);
        Assert.False(loaded.IsEnabled("floe.other"));
        Assert.True(loaded.IsEnabled("floe.tidepool"));
        Assert.Equal(0.75, loaded.OptionsFor("floe.tidepool")!["intensity"].GetDouble(), 3);
    }

    [Fact]
    public void Options_merge_defaults_with_stored_values()
    {
        var folder = WritePlugin("floe.merged", extra: "\"options\":[{\"id\":\"glow\",\"type\":\"bool\",\"default\":true},{\"id\":\"speed\",\"type\":\"number\",\"min\":0,\"max\":2,\"default\":0.5}]");
        var load = VisualizerPluginReader.Load(folder);
        Assert.True(load.Ok);

        using var document = JsonDocument.Parse("1.25");
        var stored = new Dictionary<string, JsonElement> { ["speed"] = document.RootElement.Clone() };

        var json = PluginOptionsDocument.Merge(load.Plugin!.Manifest, stored);
        using var merged = JsonDocument.Parse(json);

        Assert.True(merged.RootElement.GetProperty("glow").GetBoolean());
        Assert.Equal(1.25, merged.RootElement.GetProperty("speed").GetDouble(), 3);
    }

    [Fact]
    public void The_state_message_carries_the_palette_and_the_raw_options()
    {
        var state = new PluginState
        {
            Width = 1600,
            Height = 120,
            Dpr = 1.5,
            DockLeft = 0.2,
            DockRight = 0.8,
            DockBottom = 0.1,
            DockTop = 0.9,
            Core = "#70E2F5",
            OptionsJson = "{\"intensity\":0.7}",
            FpsCap = 60,
            Running = true,
        };

        var json = PluginProtocol.BuildState(state);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("state", root.GetProperty("type").GetString());
        Assert.Equal(1.5, root.GetProperty("dpr").GetDouble(), 3);
        Assert.Equal("#70E2F5", root.GetProperty("palette").GetProperty("core").GetString());
        Assert.Equal(0.7, root.GetProperty("options").GetProperty("intensity").GetDouble(), 3);
        Assert.True(root.GetProperty("running").GetBoolean());
    }

    [Fact]
    public void A_frame_message_has_one_entry_per_band_and_per_wave_sample()
    {
        var frame = new SpectrumFrame(32);
        for (int i = 0; i < frame.Bands.Length; i++) frame.Bands[i] = i / 32f;
        frame.Bass = 0.5f;
        frame.Level = 0.8f;
        frame.Kick = 0.9f;
        for (int i = 0; i < frame.Wave.Length; i++) frame.Wave[i] = (i / 512f) - 0.5f;

        var json = PluginProtocol.BuildFrame(frame, 12.3456, -0.25f);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("frame", root.GetProperty("type").GetString());
        Assert.Equal(12.346, root.GetProperty("t").GetDouble(), 3);
        Assert.Equal(32, root.GetProperty("bands").GetArrayLength());
        Assert.Equal(512, root.GetProperty("wave").GetArrayLength());
        Assert.Equal(0.9, root.GetProperty("kick").GetDouble(), 3);
        Assert.Equal(-0.25, root.GetProperty("balance").GetDouble(), 3);
    }

    [Fact]
    public void A_wave_count_other_than_the_analyser_buffer_is_rejected()
    {
        var folder = WritePlugin("floe.wave", extra: "\"audio\":{\"bands\":32,\"wave\":256,\"waveRateHz\":60}");

        var load = VisualizerPluginReader.Load(folder);

        Assert.False(load.Ok);
        Assert.Contains(load.Problems, p => p.Contains("audio.wave", StringComparison.OrdinalIgnoreCase));
    }

    // ---- helpers -------------------------------------------------------------------------

    private string WritePlugin(
        string id,
        string entry = "index.html",
        string api = "1.0",
        string name = "Tidepool",
        string? extra = null,
        string? root = null,
        string? idOverride = null)
    {
        var folder = Path.Combine(root ?? _root, id.Replace('.', '_') + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "index.html"), "<!doctype html><html><body></body></html>");

        var extras = extra is null ? string.Empty : ", " + extra;
        var manifest = $$"""
        {
          "schemaVersion": 1,
          "apiVersion": "{{api}}",
          "id": "{{idOverride ?? id}}",
          "version": "1.0.0",
          "name": { "en": "{{name}}", "tr": "Gelgit" },
          "entry": "{{entry}}",
          "audio": { "bands": 32, "wave": 512, "waveRateHz": 60 }
          {{extras}}
        }
        """;
        File.WriteAllText(Path.Combine(folder, "manifest.json"), manifest);
        return folder;
    }
}
