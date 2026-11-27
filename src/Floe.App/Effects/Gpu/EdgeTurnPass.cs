using System;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// Puts a strip drawn for the bottom edge onto another edge: the scenes keep drawing their horizontal strip with its
/// foot down (<see cref="SceneTarget"/>), and this pass copies it pixel for pixel onto the back buffer mirrored for
/// the top edge or turned a quarter for a side, the same turn the dock itself takes. No filtering: every pixel lands
/// on exactly one pixel, so the light stays as crisp as on the bottom edge.
/// <para>Every method runs on the GPU host's render thread.</para>
/// </summary>
internal sealed class EdgeTurnPass : IDisposable
{
    private readonly float[] _values = new float[4];
    private readonly ID3D11Buffer[] _constantBuffer = new ID3D11Buffer[1];
    private readonly ID3D11ShaderResourceView[] _inputs = new ID3D11ShaderResourceView[1];
    private readonly ID3D11ShaderResourceView[] _noInputs = new ID3D11ShaderResourceView[1];
    private readonly Viewport[] _viewport = new Viewport[1];

    private int _width;
    private int _height;
    private ID3D11Device? _device;
    private ID3D11Texture2D? _sceneTexture;
    private ID3D11RenderTargetView? _sceneTarget;
    private ID3D11ShaderResourceView? _sceneView;
    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;
    private ID3D11Buffer? _constants;
    private ID3D11BlendState? _opaque;
    private ID3D11RasterizerState? _raster;
    private ID3D11DepthStencilState? _noDepth;

    /// <summary>The strip as the scene draws it, foot down; null until <see cref="Resize"/> made one.</summary>
    public ID3D11RenderTargetView? SceneTarget => _sceneTarget;

    public void Initialize(ID3D11Device device)
    {
        ReleaseDevice();
        _device = device;
        _vertexShader = device.CreateVertexShader(Compile("vs_4_0", FullscreenVs).Span, null);
        _pixelShader = device.CreatePixelShader(Compile("ps_4_0", TurnPs).Span, null);
        _constants = device.CreateBuffer(new BufferDescription((uint)(_values.Length * 4), BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
        _opaque = device.CreateBlendState(BlendDescription.Opaque);
        _raster = device.CreateRasterizerState(RasterizerDescription.CullNone);
        _noDepth = device.CreateDepthStencilState(DepthStencilDescription.None);
    }

    /// <summary>The scene's strip, in the scene's own (horizontal) size.</summary>
    public void Resize(int sceneWidth, int sceneHeight)
    {
        sceneWidth = Math.Max(1, sceneWidth);
        sceneHeight = Math.Max(1, sceneHeight);
        if (sceneWidth == _width && sceneHeight == _height && _sceneTarget is not null) return;

        ReleaseSized();
        var device = _device;
        if (device is null) return;

        _width = sceneWidth;
        _height = sceneHeight;
        _sceneTexture = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)sceneWidth, (uint)sceneHeight, 1, 1,
            BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
        _sceneTarget = device.CreateRenderTargetView(_sceneTexture, null);
        _sceneView = device.CreateShaderResourceView(_sceneTexture, null);
    }

    public void ReleaseDevice()
    {
        ReleaseSized();
        _vertexShader?.Dispose(); _vertexShader = null;
        _pixelShader?.Dispose(); _pixelShader = null;
        _constants?.Dispose(); _constants = null;
        _opaque?.Dispose(); _opaque = null;
        _raster?.Dispose(); _raster = null;
        _noDepth?.Dispose(); _noDepth = null;
        _constantBuffer[0] = null!;
        _device = null;
    }

    public void Dispose() => ReleaseDevice();

    /// <summary>Copies the strip onto <paramref name="target"/> (the window's size) turned for <paramref name="edge"/>: 1 top, 2 left, 3 right.</summary>
    public void Apply(ID3D11DeviceContext context, ID3D11RenderTargetView target, int width, int height, int edge)
    {
        if (_sceneView is null || _pixelShader is null || _vertexShader is null || _constants is null) return;

        _values[0] = Math.Max(1, width);
        _values[1] = Math.Max(1, height);
        _values[2] = edge;

        context.PSSetShaderResources(0, _noInputs);
        _viewport[0] = new Viewport(0, 0, Math.Max(1, width), Math.Max(1, height), 0, 1);
        context.RSSetViewports(_viewport);
        context.OMSetRenderTargets(target, null);
        context.OMSetDepthStencilState(_noDepth, 0);
        context.OMSetBlendState(_opaque);
        context.RSSetState(_raster);
        context.IASetInputLayout(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(_vertexShader);
        context.PSSetShader(_pixelShader);
        context.UpdateSubresource(_values, _constants);
        _constantBuffer[0] = _constants;
        context.PSSetConstantBuffers(0, _constantBuffer);
        _inputs[0] = _sceneView;
        context.PSSetShaderResources(0, _inputs);
        context.Draw(3, 0);
        context.PSSetShaderResources(0, _noInputs);
    }

    private void ReleaseSized()
    {
        _sceneView?.Dispose(); _sceneView = null;
        _sceneTarget?.Dispose(); _sceneTarget = null;
        _sceneTexture?.Dispose(); _sceneTexture = null;
        _width = _height = 0;
    }

    private static ReadOnlyMemory<byte> Compile(string profile, string source) =>
        Floe.App.Effects.Gpu.GpuShaderCache.Compile(source, "main", "EdgeTurn", profile, ShaderFlags.OptimizationLevel3, EffectFlags.None);

    private const string FullscreenVs = """
        float4 main(uint id : SV_VertexID) : SV_Position
        {
            float2 uv = float2((id << 1) & 2, id & 2);
            return float4((uv.x * 2) - 1, 1 - (uv.y * 2), 0, 1);
        }
        """;

    // the window pixel (x, y) of a W x H window shows the strip's pixel:
    // top (x, H-1-y), left (y, W-1-x), right (y, x); the same turns the dock takes
    private const string TurnPs = """
        cbuffer Params : register(b0)
        {
            float2 Size;
            float Edge;
            float Spare;
        };

        Texture2D<float4> Source : register(t0);

        float4 main(float4 position : SV_Position) : SV_Target
        {
            int2 p = (int2)position.xy;
            int2 size = (int2)Size;
            int edge = (int)(Edge + .5);
            int2 s = edge == 1 ? int2(p.x, size.y - 1 - p.y)
                : edge == 2 ? int2(p.y, size.x - 1 - p.x)
                : int2(p.y, p.x);
            return Source.Load(int3(s, 0));
        }
        """;
}
