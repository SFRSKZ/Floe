using Vortice.Direct3D11;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// One visualizer drawn on the GPU layer. The host owns the device, the swap chain and the render thread;
/// a scene owns only its own shaders, buffers and textures. Every method is called on the host's render thread.
/// </summary>
public interface IGpuScene : IDisposable
{
    /// <summary>Creates the scene's device resources. Called once, and again after a device loss.</summary>
    void Initialize(ID3D11Device device);

    /// <summary>The target changed size (pixels); recreate size-dependent textures. Also called right after Initialize.</summary>
    void Resize(int width, int height);

    /// <summary>
    /// Draws one frame into <paramref name="target"/> (B8G8R8A8_UNORM, premultiplied alpha, already cleared to
    /// transparent by the host). The scene sets its own viewport, shaders and state.
    /// </summary>
    void Render(ID3D11DeviceContext context, ID3D11RenderTargetView target, GpuFrame frame);

    /// <summary>Releases device resources but keeps CPU state (particles etc.), ahead of a re-Initialize.</summary>
    void ReleaseDevice();
}
