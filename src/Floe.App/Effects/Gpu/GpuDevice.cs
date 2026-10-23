using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using SharpGen.Runtime;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// Device and composition-swap-chain plumbing shared by the layer window and the offscreen preview.
/// Everything here belongs to the calling (render) thread.
/// </summary>
internal static class GpuDevice
{
    private const int DxgiErrorDeviceRemoved = unchecked((int)0x887A0005);
    private const int DxgiErrorDeviceReset = unchecked((int)0x887A0007);

    private static readonly FeatureLevel[] Levels =
    {
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0,
        FeatureLevel.Level_10_1,
        FeatureLevel.Level_10_0,
    };

    /// <summary>Hardware first, WARP when that is all the machine has (remote sessions, no adapter).</summary>
    public static ID3D11Device Create()
    {
        try
        {
            return D3D11.D3D11CreateDevice(DriverType.Hardware, DeviceCreationFlags.BgraSupport, Levels);
        }
        catch (Exception)
        {
            return D3D11.D3D11CreateDevice(DriverType.Warp, DeviceCreationFlags.BgraSupport, Levels);
        }
    }

    public static bool IsDeviceLost(int code) => code is DxgiErrorDeviceRemoved or DxgiErrorDeviceReset;

    public static bool IsDeviceLost(Exception exception)
    {
        if (exception is SharpGenException sharp && sharp.ResultCode.Code != 0)
        {
            return IsDeviceLost(sharp.ResultCode.Code);
        }

        return IsDeviceLost(exception.HResult);
    }
}

/// <summary>
/// A flip-model swap chain on a composition visual. Composition draws the window, so the window itself
/// never paints; the swap chain owns the pixels and the frame-latency handle paces the render loop.
/// </summary>
internal sealed class GpuSwapChain : IDisposable
{
    private readonly IDCompositionDevice _composition;
    private readonly IDCompositionTarget _target;
    private readonly IDCompositionVisual _visual;

    private GpuSwapChain(IDXGISwapChain1 chain, IDCompositionDevice composition, IDCompositionTarget target, IDCompositionVisual visual, IntPtr frameLatency)
    {
        Chain = chain;
        _composition = composition;
        _target = target;
        _visual = visual;
        FrameLatency = frameLatency;
    }

    public IDXGISwapChain1 Chain { get; }

    /// <summary>Signalled when the chain can take another frame; owned by the swap chain.</summary>
    public IntPtr FrameLatency { get; }

    public static GpuSwapChain Create(ID3D11Device device, IntPtr hwnd, int width, int height)
    {
        IDXGISwapChain1? chain = null;
        IDCompositionDevice? composition = null;
        IDCompositionTarget? target = null;
        IDCompositionVisual? visual = null;
        IntPtr latency = IntPtr.Zero;
        try
        {
            var dxgiDevice = device.QueryInterface<IDXGIDevice>();
            using (dxgiDevice)
            {
                using var adapter = dxgiDevice.GetAdapter();
                using var factory = adapter.GetParent<IDXGIFactory2>();
                var description = new SwapChainDescription1(
                    (uint)Math.Max(1, width),
                    (uint)Math.Max(1, height),
                    Format.B8G8R8A8_UNorm,
                    false,
                    Usage.RenderTargetOutput,
                    2,
                    Scaling.Stretch,
                    SwapEffect.FlipSequential,
                    AlphaMode.Premultiplied,
                    SwapChainFlags.FrameLatencyWaitableObject);

                chain = factory.CreateSwapChainForComposition(dxgiDevice, description, null);
                using (var chain2 = chain.QueryInterface<IDXGISwapChain2>())
                {
                    chain2.MaximumFrameLatency = 1;
                    latency = chain2.FrameLatencyWaitableObject;
                }

                composition = DComp.DCompositionCreateDevice<IDCompositionDevice>(dxgiDevice);
                composition.CreateTargetForHwnd(hwnd, true, out target).CheckError();
                composition.CreateVisual(out visual).CheckError();
                visual.SetContent(chain).CheckError();
                target.SetRoot(visual).CheckError();
                composition.Commit().CheckError();
            }

            return new GpuSwapChain(chain, composition, target, visual, latency);
        }
        catch
        {
            visual?.Dispose();
            target?.Dispose();
            composition?.Dispose();
            chain?.Dispose();
            throw;
        }
    }

    public void Resize(int width, int height)
    {
        Chain.ResizeBuffers(
            2,
            (uint)Math.Max(1, width),
            (uint)Math.Max(1, height),
            Format.B8G8R8A8_UNorm,
            SwapChainFlags.FrameLatencyWaitableObject).CheckError();
    }

    public void Dispose()
    {
        _visual.Dispose();
        _target.Dispose();
        _composition.Dispose();
        Chain.Dispose();
    }
}
