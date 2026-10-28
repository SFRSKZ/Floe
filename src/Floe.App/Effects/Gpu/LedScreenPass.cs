using System;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// A post pass that turns a scene's picture into a colour LED wall. It owns the offscreen picture the scene draws
/// into (<see cref="SceneTarget"/>), then samples that picture once per cell and draws three vertical rounded bars
/// per cell, each lit by its own channel. Premultiplied output, and a cell's alpha follows the picture's, so the
/// strip stays transparent wherever the scene drew nothing: there is no panel behind the light.
/// <para>Every method runs on the GPU host's render thread.</para>
/// </summary>
internal sealed class LedScreenPass : IDisposable
{
    // one cell every 3 DIP, as chunky as the little panels; the bars together cover ~57% of a cell
    private const float PitchDip = 3;
    // the bars are smaller than the cell, so they are pushed up by about this much to keep the average brightness
    private const float Gain = 2.4f;

    private readonly float[] _values = new float[8];
    private readonly ID3D11Buffer[] _constantBuffer = new ID3D11Buffer[1];
    private readonly ID3D11SamplerState[] _sampler = new ID3D11SamplerState[1];
    private readonly ID3D11ShaderResourceView[] _inputs = new ID3D11ShaderResourceView[1];
    private readonly ID3D11ShaderResourceView[] _noInputs = new ID3D11ShaderResourceView[1];
    private readonly Viewport[] _viewport = new Viewport[1];

    private int _width;
    private int _height;
    // Resize builds the offscreen picture, so it needs the device the pass was initialised with
    private ID3D11Device? _device;
    private ID3D11Texture2D? _sceneTexture;
    private ID3D11RenderTargetView? _sceneTarget;
    private ID3D11ShaderResourceView? _sceneView;
    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;
    private ID3D11Buffer? _constants;
    private ID3D11SamplerState? _samplerState;
    private ID3D11BlendState? _blend;
    private ID3D11RasterizerState? _raster;
    private ID3D11DepthStencilState? _noDepth;

    /// <summary>Where the scene should draw instead of the back buffer; null until <see cref="Resize"/> made one.</summary>
    public ID3D11RenderTargetView? SceneTarget => _sceneTarget;

    public void Initialize(ID3D11Device device)
    {
        ReleaseDevice();
        _device = device;
        _vertexShader = device.CreateVertexShader(Compile("vs_4_0", FullscreenVs).Span, null);
        _pixelShader = device.CreatePixelShader(Compile("ps_4_0", LedPs).Span, null);
        _constants = device.CreateBuffer(new BufferDescription((uint)(_values.Length * 4), BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
        _samplerState = device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Clamp, 0, 1, ComparisonFunction.Never, 0, float.MaxValue));
        _blend = device.CreateBlendState(new BlendDescription(Blend.One, Blend.InverseSourceAlpha, Blend.One, Blend.InverseSourceAlpha));
        _raster = device.CreateRasterizerState(RasterizerDescription.CullNone);
        _noDepth = device.CreateDepthStencilState(DepthStencilDescription.None);
        _sampler[0] = _samplerState;
    }

    /// <summary>Keeps the offscreen picture the size of the strip; the scene draws into it.</summary>
    public void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == _width && height == _height && _sceneTarget is not null) return;

        ReleaseSized();
        var device = _device;
        if (device is null) return;

        _width = width;
        _height = height;
        _sceneTexture = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1,
            BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
        _sceneTarget = device.CreateRenderTargetView(_sceneTexture, null);
        _sceneView = device.CreateShaderResourceView(_sceneTexture, null);
    }

    /// <summary>Drops every device object; a new device needs a new <see cref="Initialize"/>.</summary>
    public void ReleaseDevice()
    {
        ReleaseSized();
        _vertexShader?.Dispose(); _vertexShader = null;
        _pixelShader?.Dispose(); _pixelShader = null;
        _constants?.Dispose(); _constants = null;
        _samplerState?.Dispose(); _samplerState = null;
        _blend?.Dispose(); _blend = null;
        _raster?.Dispose(); _raster = null;
        _noDepth?.Dispose(); _noDepth = null;
        _constantBuffer[0] = null!;
        _sampler[0] = null!;
        _device = null;
    }

    public void Dispose() => ReleaseDevice();

    /// <summary>
    /// Draws <see cref="SceneTarget"/> into <paramref name="target"/> as the LED grid. Premultiplied; cells whose
    /// source is transparent draw nothing, so the strip never turns into a black panel.
    /// </summary>
    public void Apply(ID3D11DeviceContext context, ID3D11RenderTargetView target, int width, int height, float pixelsPerDip, float time)
    {
        if (_sceneView is null || _pixelShader is null || _vertexShader is null || _constants is null) return;

        width = Math.Max(1, width);
        height = Math.Max(1, height);
        float ppd = pixelsPerDip > 0 ? pixelsPerDip : 1;

        _values[0] = width;
        _values[1] = height;
        _values[2] = PitchDip * ppd;
        _values[3] = Gain;
        _values[4] = time;

        // nothing may still be bound as an input while it becomes the target
        context.PSSetShaderResources(0, _noInputs);
        _viewport[0] = new Viewport(0, 0, width, height, 0, 1);
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
        context.PSSetSamplers(0, _sampler);
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
        Compiler.Compile(source, "main", "Led", profile, ShaderFlags.OptimizationLevel3, EffectFlags.None);

    private const string FullscreenVs = """
        float4 main(uint id : SV_VertexID) : SV_Position
        {
            float2 uv = float2((id << 1) & 2, id & 2);
            return float4((uv.x * 2) - 1, 1 - (uv.y * 2), 0, 1);
        }
        """;

    // One cell: its middle plus four points towards the corners, so a single bright pixel cannot decide a whole
    // dot. The cell's colour lights three vertical rounded bars in turn, and each bar bleeds a little glow into the
    // dark between them, the way the lamps of a real panel do.
    private const string LedPs = """
        cbuffer Params : register(b0)
        {
            float2 Size;
            float Pitch;
            float Gain;
            float Time;
            float3 Spare;
        };

        Texture2D<float4> Source : register(t0);
        SamplerState Linear : register(s0);

        // distance to a box with rounded ends, negative inside
        float Bar(float2 q, float2 halfSize, float radius)
        {
            float2 d = abs(q) - halfSize + radius;
            return length(max(d, 0)) + min(max(d.x, d.y), 0) - radius;
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 p = position.xy / Pitch;
            int2 cell = (int2)floor(p);
            float2 local = p - cell;

            float2 texel = 1 / Size;
            float2 centre = (cell + .5) * Pitch;
            float4 s0 = Source.SampleLevel(Linear, centre * texel, 0);
            float4 s1 = Source.SampleLevel(Linear, (centre + float2(-.25, -.25) * Pitch) * texel, 0);
            float4 s2 = Source.SampleLevel(Linear, (centre + float2(.25, -.25) * Pitch) * texel, 0);
            float4 s3 = Source.SampleLevel(Linear, (centre + float2(-.25, .25) * Pitch) * texel, 0);
            float4 s4 = Source.SampleLevel(Linear, (centre + float2(.25, .25) * Pitch) * texel, 0);
            // lean towards the brightest sample: an average would wash the thin sparks and lines out of the cell
            float4 c = lerp((s0 + s1 + s2 + s3 + s4) * .2, max(max(max(s0, s1), max(s2, s3)), s4), .6);

            // the picture is premultiplied: its rgb is the light itself, which keeps additive sparks glowing;
            // the alpha only decides how much of it the strip shows
            float alpha = c.a;

            float barWidth = .22 * Pitch;
            float halfHeight = .43 * Pitch;
            float step = .28 * Pitch;
            float soft = max(1, Pitch * .12);
            float2 q = (local - .5) * Pitch;

            // real panels drift a little in brightness; keep it subtle so the picture itself does not flicker
            float breath = .94 + (.06 * sin(Time * 1.9));

            float3 light = 0;
            float cover = 0;
            [unroll]
            for (int i = 0; i < 3; i++)
            {
                float2 offset = float2((i - 1) * step, 0);
                float2 q2 = q - offset;
                float d = Bar(q2, float2(barWidth * .5, halfHeight), barWidth * .5);
                float mask = saturate(.5 - (d / soft));
                // the glow only fills where the bar is not, so a lit lamp keeps its full brightness
                float halo = exp(-dot(q2, q2) / (Pitch * Pitch * .036)) * (1 - mask) * .2 * breath;
                light[i] += c[i] * (mask + halo);
                cover += mask + halo;
            }

            float3 rgb = saturate(light * Gain);
            float a = saturate(cover) * alpha;
            return float4(rgb, a);
        }
        """;
}
