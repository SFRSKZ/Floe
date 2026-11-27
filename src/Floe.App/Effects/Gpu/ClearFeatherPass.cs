using System;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// Dims the spans of the strip behind the taskbar's icons and groups: the light runs on behind them, faint in the
/// middle and full again towards their ends, instead of stopping at a hard vertical cut. One fullscreen triangle whose
/// blend scales what is already drawn by (1 - cover), colour and alpha alike, so the premultiplied picture stays
/// right.
/// <para>Every method runs on the GPU host's render thread.</para>
/// </summary>
internal sealed class ClearFeatherPass : IDisposable
{
    public const int MaxSpans = 8;

    // Info (width, feather px, span count, height px), then the spans as pairs, two to a float4, then (top px, top ramp px)
    private readonly float[] _values = new float[8 + (MaxSpans * 2)];
    private readonly ID3D11Buffer[] _constantBuffer = new ID3D11Buffer[1];
    private readonly ID3D11ShaderResourceView[] _noInputs = new ID3D11ShaderResourceView[1];
    private readonly Viewport[] _viewport = new Viewport[1];

    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;
    private ID3D11Buffer? _constants;
    private ID3D11BlendState? _fade;
    private ID3D11RasterizerState? _raster;
    private ID3D11DepthStencilState? _noDepth;

    public void Initialize(ID3D11Device device)
    {
        ReleaseDevice();
        _vertexShader = device.CreateVertexShader(Compile("vs_4_0", FullscreenVs).Span, null);
        _pixelShader = device.CreatePixelShader(Compile("ps_4_0", FeatherPs).Span, null);
        _constants = device.CreateBuffer(new BufferDescription((uint)(_values.Length * 4), BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
        // dest * (1 - src.a): the shader's alpha is how much of the picture goes
        _fade = device.CreateBlendState(new BlendDescription(Blend.Zero, Blend.InverseSourceAlpha, Blend.Zero, Blend.InverseSourceAlpha));
        _raster = device.CreateRasterizerState(RasterizerDescription.CullNone);
        _noDepth = device.CreateDepthStencilState(DepthStencilDescription.None);
    }

    public void ReleaseDevice()
    {
        _vertexShader?.Dispose(); _vertexShader = null;
        _pixelShader?.Dispose(); _pixelShader = null;
        _constants?.Dispose(); _constants = null;
        _fade?.Dispose(); _fade = null;
        _raster?.Dispose(); _raster = null;
        _noDepth?.Dispose(); _noDepth = null;
        _constantBuffer[0] = null!;
    }

    public void Dispose() => ReleaseDevice();

    /// <param name="spans">Sorted, merged 0..1 pairs; past <see cref="MaxSpans"/> the rest are dropped.</param>
    /// <param name="feather">How far before each span the light starts to fade, in pixels.</param>
    /// <param name="top">The row the spans start at, in pixels; above it (less the ramp) the light is left alone.</param>
    public void Apply(ID3D11DeviceContext context, ID3D11RenderTargetView target, int width, int height, float[] spans, float feather, float top = 0, float ramp = 1)
    {
        if (_pixelShader is null || _vertexShader is null || _constants is null) return;

        int count = Math.Min(MaxSpans, spans.Length / 2);
        Array.Clear(_values);
        _values[0] = Math.Max(1, width);
        _values[1] = Math.Max(.5f, feather);
        _values[2] = count;
        _values[3] = Math.Max(1, height);
        for (int i = 0; i < count * 2; i++) _values[4 + i] = spans[i];
        _values[4 + (MaxSpans * 2)] = Math.Max(0, top);
        _values[5 + (MaxSpans * 2)] = Math.Max(.5f, ramp);

        context.PSSetShaderResources(0, _noInputs);
        _viewport[0] = new Viewport(0, 0, Math.Max(1, width), Math.Max(1, height), 0, 1);
        context.RSSetViewports(_viewport);
        context.OMSetRenderTargets(target, null);
        context.OMSetDepthStencilState(_noDepth, 0);
        context.OMSetBlendState(_fade);
        context.RSSetState(_raster);
        context.IASetInputLayout(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(_vertexShader);
        context.PSSetShader(_pixelShader);
        context.UpdateSubresource(_values, _constants);
        _constantBuffer[0] = _constants;
        context.PSSetConstantBuffers(0, _constantBuffer);
        context.Draw(3, 0);
        // the next frame's scene starts from the default blend, not from this one
        context.OMSetBlendState(null);
    }

    private static ReadOnlyMemory<byte> Compile(string profile, string source) =>
        Floe.App.Effects.Gpu.GpuShaderCache.Compile(source, "main", "ClearFeather", profile, ShaderFlags.OptimizationLevel3, EffectFlags.None);

    private const string FullscreenVs = """
        float4 main(uint id : SV_VertexID) : SV_Position
        {
            float2 uv = float2((id << 1) & 2, id & 2);
            return float4((uv.x * 2) - 1, 1 - (uv.y * 2), 0, 1);
        }
        """;

    // Inside every icon/group span [a, b] the visualizer is cleared completely (keep = 0) so no light ever draws
    // over icons, the start button, the song card or the clock. Outside a span, within `Feather` pixels of an open
    // boundary, the light ramps smoothly from 0 up to 1 into the free glass.
    private const string FeatherPs = """
        cbuffer Params : register(b0)
        {
            float Width;
            float Feather;
            float Count;
            float Height;
            float4 Spans[4];
            float Top;
            float TopRamp;
            float2 Pad;
        };

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float x = position.x;
            float keep = 1;
            int count = (int)(Count + .5);
            [loop]
            for (int i = 0; i < count; i++)
            {
                float4 pair = Spans[i / 2];
                float2 span = (i % 2) == 0 ? pair.xy : pair.zw;
                float a = span.x * Width, b = span.y * Width;
                if (x >= a && x <= b)
                {
                    keep = 0;
                    break;
                }

                if (x < a && a > .5)
                {
                    keep *= smoothstep(0, Feather, a - x);
                }
                else if (x > b && b < Width - .5)
                {
                    keep *= smoothstep(0, Feather, x - b);
                }
            }

            // above the bar the peaks run on: the clearing fades in over a few pixels down to its top
            float cover = smoothstep(Top - TopRamp, Top, position.y);
            return float4(0, 0, 0, (1 - keep) * cover);
        }
        """;
}
