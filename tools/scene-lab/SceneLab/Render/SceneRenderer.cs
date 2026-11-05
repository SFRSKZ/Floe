using System.Runtime.InteropServices;
using Floe.App.Effects.Gpu;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace SceneLab.Render;

/// <summary>
/// The lab's own offscreen renderer: one D3D11 device, one BGRA target and a staging copy, the same way the
/// dock draws a scene. Nothing here is a preview approximation — the scene's real shaders and constants run,
/// so what the lab shows is what the dock shows.
/// </summary>
public sealed class SceneRenderer : IDisposable
{
    private static readonly FeatureLevel[] Levels =
    {
        FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0,
    };

    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11Texture2D _target;
    private readonly ID3D11Texture2D _staging;
    private readonly ID3D11RenderTargetView _view;
    private readonly int _width, _height;

    public SceneRenderer(int width, int height)
    {
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        Device = D3D11.D3D11CreateDevice(DriverType.Hardware, DeviceCreationFlags.BgraSupport, Levels);
        _context = Device.ImmediateContext;
        var description = new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)_width, (uint)_height, 1, 1,
            BindFlags.RenderTarget, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None);
        _target = Device.CreateTexture2D(description);
        _view = Device.CreateRenderTargetView(_target, null);
        _staging = Device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)_width, (uint)_height, 1, 1,
            BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read, 1, 0, ResourceOptionFlags.None));
        Info = $"{Device.FeatureLevel}";
    }

    public ID3D11Device Device { get; }

    public string Info { get; }

    public int Width => _width;
    public int Height => _height;

    /// <summary>Draws one frame and returns the premultiplied BGRA pixels.</summary>
    public byte[] Draw(IGpuScene scene, GpuFrame frame)
    {
        _context.ClearRenderTargetView(_view, new Color4(0, 0, 0, 0));
        scene.Render(_context, _view, frame);
        _context.CopyResource(_staging, _target);

        int stride = _width * 4;
        var pixels = new byte[stride * _height];
        var mapped = _context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            for (int y = 0; y < _height; y++)
            {
                Marshal.Copy(IntPtr.Add(mapped.DataPointer, y * (int)mapped.RowPitch), pixels, y * stride, stride);
            }
        }
        finally
        {
            _context.Unmap(_staging, 0);
        }

        return pixels;
    }

    public void Dispose()
    {
        _staging.Dispose();
        _view.Dispose();
        _target.Dispose();
        _context.Dispose();
        Device.Dispose();
    }
}
