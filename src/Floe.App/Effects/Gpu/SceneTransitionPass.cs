using System;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Floe.App.Effects.Gpu;

/// <summary>The moves a <see cref="SceneTransitionPass"/> can hand one scene over to the next with.</summary>
public enum SceneTransition
{
    /// <summary>The old scene swells past the viewer and fades; the new one grows in from smaller.</summary>
    ZoomIn,
    /// <summary>The old scene shrinks away into the middle; the new one comes down from larger.</summary>
    ZoomOut,
    /// <summary>The new scene pushes the old one out to the left.</summary>
    SlideLeft,
    /// <summary>The new scene pushes the old one out to the right.</summary>
    SlideRight,
    /// <summary>The new scene pushes the old one out upwards.</summary>
    SlideUp,
    /// <summary>The new scene pushes the old one out downwards.</summary>
    SlideDown,
}

/// <summary>
/// Hands one GPU scene over to the next: both draw into their own offscreen pictures (<see cref="OldTarget"/>,
/// <see cref="NewTarget"/>) and this pass lays them over each other with a zoom or a push, eased in and out.
/// Premultiplied throughout, so the strip stays transparent wherever neither scene drew. Point sampling on the
/// pushes keeps the pictures pixel-sharp while they move by whole pixels; the zooms sample linearly.
/// <para>Every method runs on the GPU host's render thread.</para>
/// </summary>
internal sealed class SceneTransitionPass : IDisposable
{
    private readonly float[] _values = new float[8];
    private readonly ID3D11Buffer[] _constantBuffer = new ID3D11Buffer[1];
    private readonly ID3D11SamplerState[] _samplers = new ID3D11SamplerState[2];
    private readonly ID3D11ShaderResourceView[] _inputs = new ID3D11ShaderResourceView[2];
    private readonly ID3D11ShaderResourceView[] _noInputs = new ID3D11ShaderResourceView[2];
    private readonly Viewport[] _viewport = new Viewport[1];

    private int _width;
    private int _height;
    private ID3D11Device? _device;
    private ID3D11Texture2D? _oldTexture, _newTexture;
    private ID3D11RenderTargetView? _oldTarget, _newTarget;
    private ID3D11ShaderResourceView? _oldView, _newView;
    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;
    private ID3D11Buffer? _constants;
    private ID3D11SamplerState? _linear, _point;
    private ID3D11BlendState? _blend;
    private ID3D11RasterizerState? _raster;
    private ID3D11DepthStencilState? _noDepth;

    /// <summary>Where the outgoing scene draws; null until <see cref="Resize"/> made it.</summary>
    public ID3D11RenderTargetView? OldTarget => _oldTarget;

    /// <summary>Where the incoming scene draws; null until <see cref="Resize"/> made it.</summary>
    public ID3D11RenderTargetView? NewTarget => _newTarget;

    public void Initialize(ID3D11Device device)
    {
        ReleaseDevice();
        _device = device;
        _vertexShader = device.CreateVertexShader(Compile("vs_4_0", FullscreenVs).Span, null);
        _pixelShader = device.CreatePixelShader(Compile("ps_4_0", BlendPs).Span, null);
        _constants = device.CreateBuffer(new BufferDescription((uint)(_values.Length * 4), BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
        _linear = device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Clamp, 0, 1, ComparisonFunction.Never, 0, float.MaxValue));
        _point = device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipPoint, TextureAddressMode.Clamp, 0, 1, ComparisonFunction.Never, 0, float.MaxValue));
        _blend = device.CreateBlendState(new BlendDescription(Blend.One, Blend.InverseSourceAlpha, Blend.One, Blend.InverseSourceAlpha));
        _raster = device.CreateRasterizerState(RasterizerDescription.CullNone);
        _noDepth = device.CreateDepthStencilState(DepthStencilDescription.None);
        _samplers[0] = _linear;
        _samplers[1] = _point;
    }

    public void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == _width && height == _height && _oldTarget is not null) return;

        ReleaseSized();
        var device = _device;
        if (device is null) return;

        _width = width;
        _height = height;
        (_oldTexture, _oldTarget, _oldView) = Picture(device, width, height);
        (_newTexture, _newTarget, _newView) = Picture(device, width, height);
    }

    private static (ID3D11Texture2D, ID3D11RenderTargetView, ID3D11ShaderResourceView) Picture(ID3D11Device device, int width, int height)
    {
        var texture = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1,
            BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
        return (texture, device.CreateRenderTargetView(texture, null), device.CreateShaderResourceView(texture, null));
    }

    public void ReleaseDevice()
    {
        ReleaseSized();
        _vertexShader?.Dispose(); _vertexShader = null;
        _pixelShader?.Dispose(); _pixelShader = null;
        _constants?.Dispose(); _constants = null;
        _linear?.Dispose(); _linear = null;
        _point?.Dispose(); _point = null;
        _blend?.Dispose(); _blend = null;
        _raster?.Dispose(); _raster = null;
        _noDepth?.Dispose(); _noDepth = null;
        _constantBuffer[0] = null!;
        _samplers[0] = _samplers[1] = null!;
        _device = null;
    }

    public void Dispose() => ReleaseDevice();

    /// <summary>Lays the two pictures over <paramref name="target"/>: <paramref name="progress"/> 0 is all old, 1 all new.</summary>
    public void Apply(ID3D11DeviceContext context, ID3D11RenderTargetView target, SceneTransition kind, float progress)
    {
        if (_oldView is null || _newView is null || _pixelShader is null || _vertexShader is null || _constants is null) return;

        _values[0] = _width;
        _values[1] = _height;
        _values[2] = Math.Clamp(progress, 0, 1);
        _values[3] = (float)kind;

        context.PSSetShaderResources(0, _noInputs);
        _viewport[0] = new Viewport(0, 0, _width, _height, 0, 1);
        context.RSSetViewports(_viewport);
        context.OMSetRenderTargets(target, null);
        context.OMSetDepthStencilState(_noDepth, 0);
        context.OMSetBlendState(_blend);
        context.RSSetState(_raster);
        context.IASetInputLayout(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(_vertexShader);
        context.PSSetShader(_pixelShader);
        context.UpdateSubresource(_values, _constants);
        _constantBuffer[0] = _constants;
        context.PSSetConstantBuffers(0, _constantBuffer);
        context.PSSetSamplers(0, _samplers);
        _inputs[0] = _oldView;
        _inputs[1] = _newView;
        context.PSSetShaderResources(0, _inputs);
        context.Draw(3, 0);
        context.PSSetShaderResources(0, _noInputs);
    }

    private void ReleaseSized()
    {
        _oldView?.Dispose(); _oldView = null;
        _oldTarget?.Dispose(); _oldTarget = null;
        _oldTexture?.Dispose(); _oldTexture = null;
        _newView?.Dispose(); _newView = null;
        _newTarget?.Dispose(); _newTarget = null;
        _newTexture?.Dispose(); _newTexture = null;
        _width = _height = 0;
    }

    private static ReadOnlyMemory<byte> Compile(string profile, string source) =>
        Floe.App.Effects.Gpu.GpuShaderCache.Compile(source, "main", "Transition", profile, ShaderFlags.OptimizationLevel3, EffectFlags.None);

    private const string FullscreenVs = """
        float4 main(uint id : SV_VertexID) : SV_Position
        {
            float2 uv = float2((id << 1) & 2, id & 2);
            return float4((uv.x * 2) - 1, 1 - (uv.y * 2), 0, 1);
        }
        """;

    // Zooms scale about the strip's middle in pixels (not in 0..1), so the picture keeps its proportions on a
    // 30:1 strip. Pushes move by whole pixels, so the edges stay crisp while they travel. Outside a picture is transparent.
    private const string BlendPs = """
        cbuffer Params : register(b0)
        {
            float2 Size;
            float Progress;
            float Kind;
            float4 Spare;
        };

        Texture2D<float4> Old : register(t0);
        Texture2D<float4> New : register(t1);
        SamplerState Linear : register(s0);
        SamplerState Point : register(s1);

        float Ease(float t) { return t < .5 ? 4 * t * t * t : 1 - pow(-2 * t + 2, 3) * .5; }

        float4 Zoomed(Texture2D<float4> tex, float2 p, float scale)
        {
            float2 c = Size * .5;
            float2 q = c + (p - c) / scale;
            if (any(q < 0) || any(q > Size)) return 0;
            return tex.SampleLevel(Linear, q / Size, 0);
        }

        float4 Pushed(Texture2D<float4> tex, float2 p, float2 offset)
        {
            float2 q = floor(p + offset) + .5;
            if (any(q < 0) || any(q > Size)) return 0;
            return tex.SampleLevel(Point, q / Size, 0);
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 p = position.xy;
            float e = Ease(saturate(Progress));
            int kind = (int)round(Kind);

            if (kind <= 1)
            {
                // the old scene leaves through the first two thirds, the new one arrives through the last two
                float outT = saturate(e * 1.5), inT = saturate((e - .33) * 1.5);
                float oldScale = kind == 0 ? lerp(1, 3.2, outT) : lerp(1, .35, outT);
                float newScale = kind == 0 ? lerp(.45, 1, inT) : lerp(2.6, 1, inT);
                float4 o = Zoomed(Old, p, oldScale) * (1 - outT);
                float4 n = Zoomed(New, p, newScale) * inT;
                return n + o * (1 - n.a);
            }

            float2 dir = kind == 2 ? float2(1, 0) : kind == 3 ? float2(-1, 0) : kind == 4 ? float2(0, 1) : float2(0, -1);
            float2 travel = dir * Size * e;
            float4 o2 = Pushed(Old, p, travel);
            float4 n2 = Pushed(New, p, travel - dir * Size);
            return n2 + o2 * (1 - n2.a);
        }
        """;
}
