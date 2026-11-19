using System.Windows.Media;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Floe.App.Services;
using Color = System.Windows.Media.Color;

namespace Floe.App.Effects.Gpu.Cp77;

/// <summary>
/// Base class for pure GPU-instanced 3D point-cloud sculptures: renders tens of thousands of procedural
/// anti-aliased micro-particles via <see cref="ID3D11DeviceContext.DrawInstanced"/> into an HDR float target
/// with additive blending, then films-tonemaps and masks them cleanly onto the strip wings in a second pass.
/// </summary>
public abstract class PointCloudGpuScene : IGpuScene, IThemedGpuScene
{
    private readonly int _particleCount;
    private readonly string _particleVsSource, _particlePsSource, _compositePsSource;
    private ID3D11Device? _device;
    private ID3D11VertexShader? _particleVs, _compositeVs;
    private ID3D11PixelShader? _particlePs, _compositePs;
    private ID3D11Buffer? _constants;
    private ID3D11BlendState? _additive, _opaque;
    private ID3D11RasterizerState? _raster;
    private ID3D11DepthStencilState? _noDepth;
    private ID3D11SamplerState? _linear;
    private ID3D11Texture2D? _hdrTex;
    private ID3D11RenderTargetView? _hdrRtv;
    private ID3D11ShaderResourceView? _hdrSrv;

    private readonly ID3D11Buffer[] _cb = new ID3D11Buffer[1];
    private readonly ID3D11SamplerState[] _sampler = new ID3D11SamplerState[1];
    private readonly ID3D11ShaderResourceView[] _oneSrv = new ID3D11ShaderResourceView[1];
    private Viewport[] _viewports = Array.Empty<Viewport>();

    private int _width, _height;
    private float _lastKick, _lastSnare, _beatGap;

    protected PointCloudGpuScene(
        int particleCount,
        int constantFloats,
        string particleVsSource,
        string? particlePsSource = null,
        string? compositePsSource = null)
    {
        _particleCount = particleCount;
        Data = new float[constantFloats];
        _particleVsSource = particleVsSource;
        _particlePsSource = particlePsSource ?? DefaultParticlePs;
        _compositePsSource = compositePsSource ?? DefaultCompositePs;
    }

    public CyberLook? Theme { get; set; } = GlassSettings.VisualizerThemeColours ? CyberLook.Current : null;

    protected float[] Data { get; }
    protected Cp77AudioSmoother Smoother { get; } = new();
    protected bool BeatHit { get; private set; }
    protected bool BeatSnare { get; private set; }
    protected float BeatPulse { get; private set; }

    protected abstract void Advance(GpuFrame frame);
    protected abstract void Fill(GpuFrame frame);

    public void Initialize(ID3D11Device device)
    {
        _device = device;
        _particleVs = device.CreateVertexShader(Compile("vs_5_0", _particleVsSource).Span, null);
        _particlePs = device.CreatePixelShader(Compile("ps_5_0", _particlePsSource).Span, null);
        _compositeVs = device.CreateVertexShader(Compile("vs_5_0", FullscreenVs).Span, null);
        _compositePs = device.CreatePixelShader(Compile("ps_5_0", _compositePsSource).Span, null);
        _constants = device.CreateBuffer(new BufferDescription((uint)Data.Length * 4, BindFlags.ConstantBuffer, ResourceUsage.Default));
        _cb[0] = _constants;

        var addDesc = new BlendDescription
        {
            AlphaToCoverageEnable = false,
            IndependentBlendEnable = false,
        };
        addDesc.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = Blend.One,
            DestinationBlend = Blend.One,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.One,
            DestinationBlendAlpha = Blend.One,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All,
        };
        _additive = device.CreateBlendState(addDesc);

        _opaque = device.CreateBlendState(BlendDescription.Opaque);
        _raster = device.CreateRasterizerState(RasterizerDescription.CullNone);
        _noDepth = device.CreateDepthStencilState(DepthStencilDescription.None);
        _linear = device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Clamp, 0, 1, ComparisonFunction.Never, 0, float.MaxValue));
        _sampler[0] = _linear;
    }

    public void Resize(int width, int height)
    {
        if (_device is null || width < 2 || height < 2) return;
        ReleaseSized();
        _width = width;
        _height = height;
        _hdrTex = _device.CreateTexture2D(new Texture2DDescription(Format.R16G16B16A16_Float, (uint)width, (uint)height, 1, 1,
            BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceUsage.Default));
        _hdrRtv = _device.CreateRenderTargetView(_hdrTex, null);
        _hdrSrv = _device.CreateShaderResourceView(_hdrTex, null);
        _viewports = new[] { new Viewport { Width = width, Height = height, MinDepth = 0, MaxDepth = 1 } };
    }

    public void Render(ID3D11DeviceContext context, ID3D11RenderTargetView target, GpuFrame frame)
    {
        if (_device is null || _particleVs is null || _particlePs is null || _compositeVs is null || _compositePs is null) return;
        if (frame.Width < 2 || frame.Height < 2) return;
        if (frame.Width != _width || frame.Height != _height) Resize(frame.Width, frame.Height);
        if (_hdrRtv is null || _hdrSrv is null) return;

        UpdateBeats(frame);
        Advance(frame);
        Fill(frame);
        context.UpdateSubresource(Data, _constants!);

        context.RSSetViewports(_viewports);
        context.RSSetState(_raster);
        context.OMSetDepthStencilState(_noDepth, 0);
        context.IASetInputLayout(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);

        // Pass 1: Draw all instanced 3D micro-particles additively into HDR float target
        context.ClearRenderTargetView(_hdrRtv, new Color4(0, 0, 0, 0));
        context.OMSetRenderTargets(_hdrRtv, null);
        context.OMSetBlendState(_additive);
        context.VSSetShader(_particleVs);
        context.PSSetShader(_particlePs);
        context.VSSetConstantBuffers(0, _cb);
        context.PSSetConstantBuffers(0, _cb);
        context.DrawInstanced(6, (uint)_particleCount, 0, 0);

        // Pass 2: Filmic highlight roll-off, wing edge fade & optional sharp 1px optics overlay
        context.OMSetRenderTargets(target, null);
        context.OMSetBlendState(_opaque);
        context.VSSetShader(_compositeVs);
        context.PSSetShader(_compositePs);
        context.PSSetConstantBuffers(0, _cb);
        context.PSSetSamplers(0, _sampler);
        _oneSrv[0] = _hdrSrv;
        context.PSSetShaderResources(0, _oneSrv);
        context.Draw(3, 0);

        _oneSrv[0] = null!;
        context.PSSetShaderResources(0, _oneSrv);
    }

    private void UpdateBeats(GpuFrame frame)
    {
        float dt = Math.Clamp(frame.Dt, 0f, 0.1f);
        var a = frame.Audio;
        bool kick = a.Kick > .12f && _lastKick <= .12f;
        BeatSnare = a.Snare > .12f && _lastSnare <= .12f;
        _lastKick = a.Kick;
        _lastSnare = a.Snare;
        _beatGap += dt;
        BeatHit = (kick || BeatSnare) && _beatGap > .22f;
        if (BeatHit) _beatGap = 0f;
        BeatPulse = BeatHit ? .65f : BeatPulse * MathF.Exp(-dt * 4.5f);
    }

    protected Color Lead(Color own) => Theme is { } theme ? Shown(own, theme.Body, theme.Strip, theme.Accent) : own;
    protected Color Second(Color own) => Theme is { } theme ? Shown(own, theme.Accent, theme.Strip, theme.Body) : own;

    private static Color Shown(Color own, params Color[] colours)
    {
        var field = Color.FromRgb(0x0A, 0x0A, 0x10);
        foreach (var colour in colours)
        {
            if (CyberLook.Contrast(colour, field) >= 3) return colour;
        }
        return own;
    }

    protected static Color Mix(Color a, Color b, float t) => Color.FromRgb(
        (byte)(a.R + ((b.R - a.R) * t)), (byte)(a.G + ((b.G - a.G) * t)), (byte)(a.B + ((b.B - a.B) * t)));

    protected void Put(int at, Color color)
    {
        Data[at] = color.R / 255f;
        Data[at + 1] = color.G / 255f;
        Data[at + 2] = color.B / 255f;
        Data[at + 3] = 1f;
    }

    public void ReleaseDevice()
    {
        ReleaseSized();
        _particleVs?.Dispose(); _particleVs = null;
        _particlePs?.Dispose(); _particlePs = null;
        _compositeVs?.Dispose(); _compositeVs = null;
        _compositePs?.Dispose(); _compositePs = null;
        _constants?.Dispose(); _constants = null;
        _cb[0] = null!;
        _additive?.Dispose(); _additive = null;
        _opaque?.Dispose(); _opaque = null;
        _raster?.Dispose(); _raster = null;
        _noDepth?.Dispose(); _noDepth = null;
        _linear?.Dispose(); _linear = null;
        _sampler[0] = null!;
        _device = null;
    }

    private void ReleaseSized()
    {
        _hdrSrv?.Dispose(); _hdrSrv = null;
        _hdrRtv?.Dispose(); _hdrRtv = null;
        _hdrTex?.Dispose(); _hdrTex = null;
        _viewports = Array.Empty<Viewport>();
        _width = _height = 0;
    }

    public void Dispose() => ReleaseDevice();

    private static ReadOnlyMemory<byte> Compile(string profile, string source) =>
        Compiler.Compile(source, "main", "PointCloud", profile, ShaderFlags.OptimizationLevel3, EffectFlags.None);

    private const string FullscreenVs = """
        float4 main(uint id : SV_VertexID) : SV_Position
        {
            float2 uv = float2((id << 1) & 2, id & 2);
            return float4((uv.x * 2) - 1, 1 - (uv.y * 2), 0, 1);
        }
        """;

    protected const string SharedHeader = """
        struct VSOut
        {
            float4 Pos : SV_Position;
            float2 LocalUv : TEXCOORD0;
            float3 Color : COLOR0;
        };

        float Hash(float2 p)
        {
            float3 q = frac(p.xyx * .1031);
            q += dot(q, q.yzx + 33.33);
            return frac((q.x + q.y) * q.z);
        }

        float Noise(float2 p)
        {
            float2 i = floor(p);
            float2 f = p - i;
            f = f * f * (3.0 - (2.0 * f));
            return lerp(lerp(Hash(i), Hash(i + float2(1.0, 0.0)), f.x),
                        lerp(Hash(i + float2(0.0, 1.0)), Hash(i + float2(1.0, 1.0)), f.x), f.y);
        }

        float Band(float u)
        {
            float f = saturate(u) * 22.99;
            int i = (int)f;
            int j = i + 1;
            return lerp(Bands[i / 4][i % 4], Bands[j / 4][j % 4], f - i);
        }

        float WingInfo(float u, out float wingU, out float wingEdge, out float side)
        {
            if (CrownMode > 0.5)
            {
                side = 1.0;
                wingU = u;
                wingEdge = smoothstep(0.0, 0.015, u) * smoothstep(1.0, 0.985, u);
                return 1.0;
            }
            float leftW = max(DockLeft, 0.001);
            float rightW = max(1.0 - DockRight, 0.001);
            if (u < DockLeft)
            {
                side = -1.0;
                wingU = saturate((DockLeft - u) / leftW);
                wingEdge = smoothstep(0.0, 0.03, wingU) * smoothstep(1.0, 0.95, wingU);
                return 1.0;
            }
            if (u > DockRight)
            {
                side = 1.0;
                wingU = saturate((u - DockRight) / rightW);
                wingEdge = smoothstep(0.0, 0.03, wingU) * smoothstep(1.0, 0.95, wingU);
                return 1.0;
            }
            side = 0.0;
            wingU = 0.0;
            wingEdge = 0.0;
            return 0.0;
        }

        void WingBoundsPx(float u, out float xMinPx, out float xMaxPx)
        {
            if (CrownMode > 0.5)
            {
                xMinPx = 0.0;
                xMaxPx = Size.x;
                return;
            }
            if (u < DockLeft)
            {
                xMinPx = 0.0;
                xMaxPx = DockLeft * Size.x;
                return;
            }
            xMinPx = DockRight * Size.x;
            xMaxPx = Size.x;
        }

        float3 AcesToneClean(float3 x)
        {
            return saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14));
        }

        static const float2 _QuadCorners[6] =
        {
            float2(-1.0, -1.0),
            float2( 1.0, -1.0),
            float2(-1.0,  1.0),
            float2(-1.0,  1.0),
            float2( 1.0, -1.0),
            float2( 1.0,  1.0),
        };

        float4 ProjectParticle(float2 centerPx, float radiusPx, uint vid, out float2 localUv)
        {
            localUv = _QuadCorners[min(vid, 5u)];
            bool inDockGap = (CrownMode < 0.5)
                && (centerPx.x > (DockLeft * Size.x - 1.5))
                && (centerPx.x < (DockRight * Size.x + 1.5));
            if (radiusPx <= 0.01 || inDockGap
                || centerPx.x < -radiusPx || centerPx.x > Size.x + radiusPx
                || centerPx.y < -radiusPx || centerPx.y > Size.y + radiusPx)
            {
                return float4(2.0, 2.0, 2.0, 1.0);
            }

            float2 p = centerPx + localUv * radiusPx;
            float2 ndc;
            ndc.x = (p.x / Size.x) * 2.0 - 1.0;
            ndc.y = 1.0 - (p.y / Size.y) * 2.0;
            return float4(ndc, 0.0, 1.0);
        }
        """;

    protected const string DefaultParticlePs = """
        struct VSOut
        {
            float4 Pos : SV_Position;
            float2 LocalUv : TEXCOORD0;
            float3 Color : COLOR0;
        };

        float4 main(VSOut inp) : SV_Target
        {
            float r2 = dot(inp.LocalUv, inp.LocalUv);
            if (r2 > 1.0) discard;
            float core = pow(1.0 - r2, 1.85);
            return float4(inp.Color * core, core * 0.65);
        }
        """;

    protected const string DefaultCompositePs = """
        cbuffer PointCompositeCb : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float DockLeft;
            float DockRight;
            float Level;
            float CrownMode;
        };

        Texture2D HdrTex : register(t0);
        SamplerState Linear : register(s0);

        float WingMask(float u, out float wingEdge)
        {
            if (CrownMode > 0.5)
            {
                wingEdge = smoothstep(0.0, 0.015, u) * smoothstep(1.0, 0.985, u);
                return 1.0;
            }
            if (u < DockLeft)
            {
                float wu = saturate((DockLeft - u) / max(DockLeft, 0.001));
                wingEdge = smoothstep(0.0, 0.025, wu) * smoothstep(1.0, 0.95, wu);
                return 1.0;
            }
            if (u > DockRight)
            {
                float wu = saturate((u - DockRight) / max(1.0 - DockRight, 0.001));
                wingEdge = smoothstep(0.0, 0.025, wu) * smoothstep(1.0, 0.95, wu);
                return 1.0;
            }
            wingEdge = 0.0;
            return 0.0;
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 sp = position.xy;
            float2 uv = sp / Size;
            float wingEdge;
            if (WingMask(uv.x, wingEdge) < 0.5) return float4(0, 0, 0, 0);

            float4 c0 = HdrTex.Load(int3((int2)sp, 0));
            float3 hdr = c0.rgb;
            float peak = max(hdr.r, max(hdr.g, hdr.b));

            float3 graded = saturate((hdr * (2.51 * hdr + 0.03)) / (hdr * (2.43 * hdr + 0.59) + 0.14));
            graded = saturate(graded + smoothstep(1.15, 3.6, peak) * float3(0.32, 0.24, 0.30));

            float topSoft = smoothstep(0.0, 0.12, uv.y);
            float colPeak = max(graded.r, max(graded.g, graded.b));
            float alpha = saturate(max(smoothstep(0.004, 0.14, colPeak), smoothstep(0.01, 0.25, c0.a) * 0.72)) * wingEdge * topSoft * Fade;
            return float4(graded * alpha, alpha);
        }
        """;
}
