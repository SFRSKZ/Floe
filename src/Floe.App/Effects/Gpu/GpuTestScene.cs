using System;
using System.Numerics;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// Diagnostic scene for <see cref="GpuOffscreen"/>: a full-screen triangle painting the palette from left
/// to right, opaque at the bottom edge and fading out upwards, with a slow pulse. If this reaches the
/// PNGs the device, the composition conventions and the palette path all work.
/// </summary>
internal sealed class GpuTestScene : IGpuScene
{
    private const string ShaderSource = """
        cbuffer Params : register(b0)
        {
            float4 Palette[16];
            float4 Info;   // width, height, time, fade
            float4 Extra;  // level, bass, wave, unused
        };

        struct VSOut
        {
            float4 position : SV_Position;
        };

        // One triangle that covers the target; no vertex buffer needed.
        VSOut VSMain(uint id : SV_VertexID)
        {
            float2 corner = float2(id == 2 ? 3.0 : -1.0, id == 1 ? 3.0 : -1.0);
            VSOut output;
            output.position = float4(corner, 0, 1);
            return output;
        }

        float4 PSMain(VSOut input) : SV_Target
        {
            float2 uv = input.position.xy / Info.xy;
            float at = saturate(uv.x) * 15.0;
            int low = (int)floor(at);
            int high = min(low + 1, 15);
            float3 colour = lerp(Palette[low].rgb, Palette[high].rgb, at - low);

            // bright along the screen's bottom edge, gone before the strip's top
            float rise = saturate(uv.y);
            float pulse = 0.82 + 0.18 * sin((Info.z * 2.4) - (uv.x * 6.2831));
            float alpha = rise * rise * pulse * Info.w * (0.65 + 0.35 * Extra.x);

            return float4(colour * alpha, alpha);
        }
        """;

    private readonly Vector4[] _constants = new Vector4[18]; // 16 palette + info + extra
    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;
    private ID3D11Buffer? _buffer;
    private ID3D11RasterizerState? _rasterizer;
    private ID3D11Buffer[]? _buffers;

    public void Initialize(ID3D11Device device)
    {
        var vertexCode = Compiler.Compile(ShaderSource, "VSMain", "GpuTestScene", "vs_5_0", ShaderFlags.OptimizationLevel3, EffectFlags.None);
        var pixelCode = Compiler.Compile(ShaderSource, "PSMain", "GpuTestScene", "ps_5_0", ShaderFlags.OptimizationLevel3, EffectFlags.None);
        _vertexShader = device.CreateVertexShader(vertexCode.Span, null);
        _pixelShader = device.CreatePixelShader(pixelCode.Span, null);
        _buffer = device.CreateBuffer(new BufferDescription(
            (uint)(_constants.Length * sizeof(float) * 4),
            BindFlags.ConstantBuffer,
            ResourceUsage.Default,
            CpuAccessFlags.None,
            ResourceOptionFlags.None,
            0));
        _buffers = new[] { _buffer };
        _rasterizer = device.CreateRasterizerState(RasterizerDescription.CullNone);
    }

    public void Resize(int width, int height)
    {
        // nothing size-dependent: the full-screen triangle follows the viewport
    }

    public void Render(ID3D11DeviceContext context, ID3D11RenderTargetView target, GpuFrame frame)
    {
        if (_vertexShader is null || _pixelShader is null || _buffer is null || _buffers is null) return;

        for (int i = 0; i < GpuFrame.PaletteSize; i++)
        {
            var c = frame.Palette[i];
            _constants[i] = new Vector4(c.X, c.Y, c.Z, 1);
        }

        _constants[16] = new Vector4(frame.Width, frame.Height, frame.Time, frame.Fade);
        _constants[17] = new Vector4(frame.Audio.Level, frame.Audio.Bass, frame.Wave ? 1 : 0, 0);

        context.UpdateSubresource(_constants, _buffer, 0, 0, 0, null);
        context.OMSetRenderTargets(target, null);
        context.OMSetBlendState(null!);
        context.RSSetState(_rasterizer);
        context.RSSetViewports(new[] { new Viewport(0, 0, Math.Max(1, frame.Width), Math.Max(1, frame.Height)) });
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(_vertexShader);
        context.VSSetConstantBuffers(0, _buffers);
        context.PSSetShader(_pixelShader);
        context.PSSetConstantBuffers(0, _buffers);
        context.Draw(3, 0);
    }

    public void ReleaseDevice()
    {
        _rasterizer?.Dispose();
        _rasterizer = null;
        _buffer?.Dispose();
        _buffer = null;
        _buffers = null;
        _vertexShader?.Dispose();
        _vertexShader = null;
        _pixelShader?.Dispose();
        _pixelShader = null;
    }

    public void Dispose() => ReleaseDevice();
}
