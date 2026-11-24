using Floe.Core.Services;

namespace Floe.Tests;

/// <summary>
/// The dock reads its pixel shaders precompiled; one edited without rerunning tools/compile-shaders.py would be
/// compiled on the UI thread again, which froze the dock for seconds at every start.
/// </summary>
public sealed class ShaderCacheTests
{
    [Theory]
    [InlineData("ActiveIcon/ActiveIcon.hlsl", "ps_3_0")]
    [InlineData("ActiveIcon/ClickGlass.hlsl", "ps_3_0")]
    [InlineData("ActiveIcon/ClickPaint.hlsl", "ps_3_0")]
    [InlineData("Chromatic/Chromatic.hlsl", "ps_2_0")]
    [InlineData("Glass/DozeGhost.hlsl", "ps_3_0")]
    [InlineData("Glass/DropLens.hlsl", "ps_3_0")]
    [InlineData("Glass/LiquidGlass.hlsl", "ps_3_0")]
    [InlineData("MetalRim/MetalRim.hlsl", "ps_3_0")]
    [InlineData("Visualizer/LedScreen.hlsl", "ps_3_0")]
    [InlineData("Visualizer/LedScreen.hlsl", "ps_2_0")]
    public void Shader_is_prebuilt_for_its_current_source(string file, string target)
    {
        var effects = Path.Combine(RepoRoot(), "src", "Floe.App", "Effects");
        var key = ShaderKey.For(File.ReadAllBytes(Path.Combine(effects, file)), target);
        Assert.True(File.Exists(Path.Combine(effects, "ShaderCache", key + ".cso")),
            $"{file} {target} changed: run python tools/compile-shaders.py");
    }

    [Fact]
    public void Key_ignores_line_endings()
    {
        Assert.Equal(ShaderKey.For("a\r\nb"u8.ToArray(), "ps_3_0"), ShaderKey.For("a\nb"u8.ToArray(), "ps_3_0"));
        Assert.NotEqual(ShaderKey.For("a\nb"u8.ToArray(), "ps_3_0"), ShaderKey.For("a\nb"u8.ToArray(), "ps_2_0"));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Floe.App"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
