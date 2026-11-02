using MediaColor = System.Windows.Media.Color;
using Floe.App.Effects.Visualizer;
using Floe.App.Services;
using Floe.Core.Interop;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// The hologram look on the GPU, with a MilkDrop-style feedback behind it. Every frame draws the same cold flame
/// as <see cref="HologramVisualizer"/> (the filaments in their bell, the haze, the pool and the drifting sparks,
/// in the same tint) and lays it over the last frame, warped a little and dimmed: the echoes of the strands rise,
/// spread, curl and ripple away like smoke, and the beat kicks them further. The warp takes turns between a few
/// shapes every <see cref="CycleSeconds"/>, easing from one to the next on a kick. The fresh flame always sits on
/// top at full strength (the two meet by max, not by adding), so the design itself never washes out.
/// </summary>
public sealed class HologramGpuScene : IGpuScene
{
    // the CPU look's geometry, in DIP
    private const float FilamentSpacing = 3;
    private const int MaxFilaments = 640;
    private const int AlphaGrades = 8;

    // the CPU look's level shaping
    private const float GainDecaySeconds = 4;
    private const float GainFloor = .002f;
    private const float AttackSeconds = .05f;
    private const float ReleaseSeconds = .30f;

    private const float EaseSeconds = 2;

    // The warps: how far the echoes rise (DIP per 60 fps frame), spread out from the middle (fraction per frame,
    // negative draws them in), lean outward as they climb (DIP per frame per DIP of height), ripple sideways (DIP
    // per frame, and the ripple's spatial frequency per DIP), how hard the three whirlpools wandering along the
    // strip spin them (radians per frame at their hearts) and how much of them survives a frame.
    private static readonly Warp[] Warps =
    {
        new(Rise: .6f, Spread: .001f, Lean: .02f, Wave: .6f, WaveFreq: .08f, Whirl: 0, Decay: .95f),     // smoke flaring out
        new(Rise: .3f, Spread: 0, Lean: 0, Wave: .2f, WaveFreq: .05f, Whirl: .06f, Decay: .95f),        // whirlpools
        new(Rise: .4f, Spread: 0, Lean: 0, Wave: 1.6f, WaveFreq: .04f, Whirl: 0, Decay: .94f),          // a rippling heat haze
        new(Rise: .15f, Spread: -.006f, Lean: 0, Wave: .3f, WaveFreq: .1f, Whirl: .02f, Decay: .96f),   // drawn into the core
    };

    private readonly record struct Warp(float Rise, float Spread, float Lean, float Wave, float WaveFreq, float Whirl, float Decay)
    {
        public static Warp Lerp(Warp a, Warp b, float t) => new(
            a.Rise + ((b.Rise - a.Rise) * t), a.Spread + ((b.Spread - a.Spread) * t), a.Lean + ((b.Lean - a.Lean) * t),
            a.Wave + ((b.Wave - a.Wave) * t), a.WaveFreq + ((b.WaveFreq - a.WaveFreq) * t), a.Whirl + ((b.Whirl - a.Whirl) * t),
            a.Decay + ((b.Decay - a.Decay) * t));
    }

    private ID3D11Device? _device;
    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _drawPs, _outPs;
    private ID3D11Buffer? _constants;
    private ID3D11BlendState? _opaque;
    private ID3D11RasterizerState? _raster;
    private ID3D11DepthStencilState? _noDepth;
    private ID3D11SamplerState? _linear;
    private Target? _feedback0, _feedback1, _shown;
    private readonly ID3D11RenderTargetView[] _drawTargets = new ID3D11RenderTargetView[2];
    private Viewport[] _viewport = Array.Empty<Viewport>();
    private bool _needClear;
    private int _ping;

    private readonly ID3D11Buffer[] _constantBuffer = new ID3D11Buffer[1];
    private readonly ID3D11SamplerState[] _sampler = new ID3D11SamplerState[1];
    private readonly ID3D11ShaderResourceView[] _inputs = new ID3D11ShaderResourceView[1];
    private readonly ID3D11ShaderResourceView[] _noInputs = new ID3D11ShaderResourceView[1];
    private readonly float[] _cb = new float[4 * 20];

    private int _paletteVersion = -1;
    private int _width, _height;

    private float _level, _gainPeak, _beat, _time, _wavePhase;
    private bool _kickArmed = true;
    private int _warp, _from;
    private float _warpTime, _ease = 1;
    private readonly Random _random = new(7);

    /// <summary>How long each warp plays before the next one eases in.</summary>
    public float CycleSeconds { get; set; } = 20;

    /// <summary>Holds one warp for good (0..3) instead of taking turns; for the previews.</summary>
    public int? ForcedWarp { get; set; }

    /// <summary>How bright the echoes come back against the fresh flame; 0 shows the plain hologram.</summary>
    public float Echo { get; set; } = 1;

    // ---- lifecycle --------------------------------------------------------------------------------

    public void Initialize(ID3D11Device device)
    {
        _device = device;
        _vertexShader = device.CreateVertexShader(Compile("vs_4_0", FullscreenVs).Span, null);
        _drawPs = device.CreatePixelShader(Compile("ps_4_0", DrawPs).Span, null);
        _outPs = device.CreatePixelShader(Compile("ps_4_0", OutPs).Span, null);
        _constants = device.CreateBuffer(new BufferDescription((uint)(_cb.Length * 4), BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
        _opaque = device.CreateBlendState(BlendDescription.Opaque);
        _raster = device.CreateRasterizerState(RasterizerDescription.CullNone);
        _noDepth = device.CreateDepthStencilState(DepthStencilDescription.None);
        _linear = device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Clamp, 0, 1, ComparisonFunction.Never, 0, float.MaxValue));
        _sampler[0] = _linear;
        _paletteVersion = -1;
    }

    public void Resize(int width, int height)
    {
        if (_device is null || width < 2 || height < 2) return;
        ReleaseSized();
        _width = width;
        _height = height;
        _feedback0 = new Target(_device, width, height);
        _feedback1 = new Target(_device, width, height);
        _shown = new Target(_device, width, height);
        _viewport = new[] { new Viewport(0, 0, width, height, 0, 1) };
        _needClear = true;
    }

    public void ReleaseDevice()
    {
        ReleaseSized();
        _vertexShader?.Dispose(); _vertexShader = null;
        _drawPs?.Dispose(); _drawPs = null;
        _outPs?.Dispose(); _outPs = null;
        _constants?.Dispose(); _constants = null;
        _constantBuffer[0] = null!;
        _opaque?.Dispose(); _raster?.Dispose(); _noDepth?.Dispose(); _linear?.Dispose();
        _opaque = null; _raster = null; _noDepth = null; _linear = null;
        _sampler[0] = null!;
        _device = null;
    }

    public void Dispose() => ReleaseDevice();

    private void ReleaseSized()
    {
        _feedback0?.Dispose(); _feedback0 = null;
        _feedback1?.Dispose(); _feedback1 = null;
        _shown?.Dispose(); _shown = null;
        _width = _height = 0;
    }

    // ---- one frame --------------------------------------------------------------------------------

    public void Render(ID3D11DeviceContext context, ID3D11RenderTargetView target, GpuFrame frame)
    {
        if (_device is null || _drawPs is null || frame.Width < 2 || frame.Height < 2) return;
        if (frame.Width != _width || frame.Height != _height) Resize(frame.Width, frame.Height);
        if (_feedback0 is null || _feedback1 is null || _shown is null) return;

        float dt = Math.Clamp(frame.Dt, 0, .05f);
        bool kick = UpdateAudio(dt, frame.Audio);
        Advance(dt, kick);
        if (_paletteVersion != GlassSettings.ColorVersion) BuildPalette();

        float ppd = frame.PixelsPerDip > 0 ? frame.PixelsPerDip : 1;
        float width = frame.Width / ppd, height = frame.Height / ppd;
        // the CPU look's capsule is the strip plus 4 DIP, so its base line sits on the screen's bottom edge
        float baseY = height;
        float maxHeight = (height + 4) * .95f;
        int count = Math.Clamp((int)MathF.Round(width / FilamentSpacing), 8, MaxFilaments);
        float level = Math.Clamp(_level, 0, 1);
        float energy = .30f + (.70f * MathF.Sqrt(level));
        float t = _time;
        var warp = Warp.Lerp(Warps[_from], Warps[_warp], Smooth(_ease));

        _cb[0] = frame.Width; _cb[1] = frame.Height; _cb[2] = ppd; _cb[3] = t;
        _cb[4] = level; _cb[5] = energy; _cb[6] = count; _cb[7] = Math.Clamp(MathF.Round(energy * (AlphaGrades - 1)), 0, AlphaGrades - 1);
        _cb[8] = MathF.Sin(t * .23f) * .014f; _cb[9] = (MathF.Sin((t * .31f) + 1.3f) * .105f);
        _cb[10] = .12f + (1.10f * MathF.Pow(level, .66f)); _cb[11] = maxHeight;
        // the beat throws the echoes higher and wider
        float k60 = dt * 60;
        _cb[12] = (warp.Rise + (2.2f * _beat)) * k60;
        _cb[13] = (warp.Spread + (.006f * _beat)) * k60;
        _cb[14] = warp.Lean * (1 + _beat) * k60;
        _cb[15] = MathF.Pow(warp.Decay, k60);
        _cb[16] = warp.Wave * (1 + _beat) * k60; _cb[17] = warp.WaveFreq; _cb[18] = _wavePhase; _cb[19] = .05f * k60;
        _cb[20] = width; _cb[21] = baseY; _cb[22] = width * .5f; _cb[23] = 0;
        _cb[68] = Echo;
        _cb[70] = .012f * k60;
        _cb[69] = warp.Whirl * (1 + (1.5f * _beat)) * k60;

        context.RSSetState(_raster);
        context.OMSetDepthStencilState(_noDepth, 0);
        context.OMSetBlendState(_opaque);
        context.IASetInputLayout(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(_vertexShader);
        context.PSSetSamplers(0, _sampler);

        if (_needClear)
        {
            var empty = new Color4(0, 0, 0, 0);
            context.ClearRenderTargetView(_feedback0.View, empty);
            context.ClearRenderTargetView(_feedback1.View, empty);
            _needClear = false;
        }

        // 1: the last frame warped and dimmed, the fresh hologram over it
        var from = _ping == 0 ? _feedback0 : _feedback1;
        var to = _ping == 0 ? _feedback1 : _feedback0;
        _ping ^= 1;
        _drawTargets[0] = to.View;
        _drawTargets[1] = _shown.View;
        Pass(context, _drawPs, _drawTargets, from.Srv);

        // 2: onto the strip, faded with the music
        _cb[23] = Math.Clamp(frame.Fade, 0, 1);
        _drawTargets[0] = target;
        _drawTargets[1] = null!;
        Pass(context, _outPs!, _drawTargets, _shown.Srv);
        context.OMSetRenderTargets(target, null);
        context.PSSetShaderResources(0, _noInputs);
    }

    private void Pass(ID3D11DeviceContext context, ID3D11PixelShader shader, ID3D11RenderTargetView[] destinations, ID3D11ShaderResourceView source)
    {
        context.PSSetShaderResources(0, _noInputs);
        context.RSSetViewports(_viewport);
        context.OMSetRenderTargets(destinations, null);
        context.PSSetShader(shader);
        context.UpdateSubresource(_cb, _constants!);
        _constantBuffer[0] = _constants!;
        context.PSSetConstantBuffers(0, _constantBuffer);
        _inputs[0] = source;
        context.PSSetShaderResources(0, _inputs);
        context.Draw(3, 0);
    }

    // The same tinted colours the CPU look builds its sprites and brushes from.
    private void BuildPalette()
    {
        _paletteVersion = GlassSettings.ColorVersion;
        int i = 24;
        Put(ref i, HologramVisualizer.Tint(MediaColor.FromRgb(0xEA, 0xF6, 0xFF)));          // icy core
        Put(ref i, HologramVisualizer.Tint(MediaColor.FromRgb(0x6F, 0xB8, 0xFF)));          // body
        Put(ref i, HologramVisualizer.Tint(MediaColor.FromRgb(0x2A, 0x6F, 0xD6)));          // faint edge
        Put(ref i, HologramVisualizer.Tint(MediaColor.FromRgb(0xD2, 0xEC, 0xFF)));          // haze core
        // the spark's stops
        Put(ref i, HologramVisualizer.Tint(MediaColor.FromArgb(0xD8, 0xFF, 0xFF, 0xFF)));
        Put(ref i, HologramVisualizer.Tint(MediaColor.FromArgb(0x78, 0xDA, 0xF2, 0xFF)));
        Put(ref i, HologramVisualizer.Tint(MediaColor.FromArgb(0x24, 0x8F, 0xCD, 0xFF)));
        // the pool's stops
        Put(ref i, HologramVisualizer.Tint(MediaColor.FromArgb(0xF2, 0xF2, 0xFB, 0xFF)));
        Put(ref i, HologramVisualizer.Tint(MediaColor.FromArgb(0xA8, 0xB8, 0xE4, 0xFF)));
        Put(ref i, HologramVisualizer.Tint(MediaColor.FromArgb(0x50, 0x6F, 0xB8, 0xFF)));
        Put(ref i, HologramVisualizer.Tint(MediaColor.FromArgb(0x10, 0x2A, 0x6F, 0xD6)));
    }

    private void Put(ref int i, MediaColor c)
    {
        _cb[i++] = c.R / 255f; _cb[i++] = c.G / 255f; _cb[i++] = c.B / 255f; _cb[i++] = c.A / 255f;
    }

    private bool UpdateAudio(float dt, SpectrumFrame audio)
    {
        // auto gain against a slowly decaying recent peak, as the CPU look does with its meter
        float raw = Math.Clamp(audio.Level, 0, 1);
        _gainPeak = MathF.Max(raw, _gainPeak * MathF.Exp(-dt / GainDecaySeconds));
        float target = Math.Clamp(raw / MathF.Max(_gainPeak, GainFloor), 0, 1);
        float tau = target > _level ? AttackSeconds : ReleaseSeconds;
        _level += (target - _level) * (1 - MathF.Exp(-dt / tau));
        _beat = MathF.Max(_beat * MathF.Exp(-dt * 5), Math.Clamp(audio.Kick, 0, 1));

        _time += dt;
        _wavePhase += dt * (1.2f + (2.5f * Math.Clamp(audio.Mid, 0, 1)) + (3 * _beat));

        bool kick = false;
        if (audio.Kick > .3f)
        {
            kick = _kickArmed;
            _kickArmed = false;
        }
        else
        {
            _kickArmed = true;
        }

        return kick;
    }

    // When a warp has played its time, the next kick eases in another one.
    private void Advance(float dt, bool kick)
    {
        _ease = MathF.Min(1, _ease + (dt / EaseSeconds));
        _warpTime += dt;
        if (ForcedWarp is int forced)
        {
            _from = _warp = Math.Clamp(forced, 0, Warps.Length - 1);
            return;
        }

        if (_warpTime < CycleSeconds || !(kick || _warpTime >= CycleSeconds + 4)) return;

        int next;
        do next = _random.Next(Warps.Length);
        while (next == _warp);
        _from = _warp;
        _warp = next;
        _warpTime = 0;
        _ease = 0;
    }

    private static float Smooth(float t) => t * t * (3 - (2 * t));

    // ---- shaders ----------------------------------------------------------------------------------

    private static ReadOnlyMemory<byte> Compile(string profile, string source) =>
        Compiler.Compile(source, "main", "Hologram", profile, ShaderFlags.OptimizationLevel3, EffectFlags.None);

    /// <summary>One premultiplied colour texture: its render target and its shader view, always together.</summary>
    private sealed class Target : IDisposable
    {
        private readonly ID3D11Texture2D _texture;

        public Target(ID3D11Device device, int width, int height)
        {
            _texture = device.CreateTexture2D(new Texture2DDescription(Format.R16G16B16A16_Float, (uint)width, (uint)height, 1, 1,
                BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
            View = device.CreateRenderTargetView(_texture, null);
            Srv = device.CreateShaderResourceView(_texture, null);
        }

        public ID3D11RenderTargetView View { get; }
        public ID3D11ShaderResourceView Srv { get; }

        public void Dispose()
        {
            Srv.Dispose();
            View.Dispose();
            _texture.Dispose();
        }
    }

    private const string FullscreenVs = """
        float4 main(uint id : SV_VertexID) : SV_Position
        {
            float2 uv = float2((id << 1) & 2, id & 2);
            return float4((uv.x * 2) - 1, 1 - (uv.y * 2), 0, 1);
        }
        """;

    private const string Constants = """
        cbuffer Scene : register(b0)
        {
            float2 Size; float Ppd; float Time;
            float Level; float Energy; float Count; float Grade;
            float PrimaryCenter; float SecondaryCenter; float Amplitude; float MaxHeight;
            float Rise; float Spread; float Lean; float Decay;
            float WaveAmp; float WaveFreq; float WavePhase; float Age;
            float Width; float BaseY; float CenterX; float Fade;
            float4 Family[3];
            float4 HazeCore;
            float4 Spark[3];
            float4 Pool[4];
            float EchoGain; float Whirl; float Darken;
        };

        Texture2D<float4> Source : register(t0);
        SamplerState Linear : register(s0);
        """;

    private const string DrawPs = Constants + """

        static const float AlphaLadder[8] = { .06, .09, .13, .18, .25, .33, .43, .56 };
        static const float FamilyScale[3] = { 1.0, .90, .62 };

        float Hash01(int x, int seed)
        {
            uint h = ((uint)x * 374761393u) + ((uint)seed * 668265263u);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0x00FFFFFF) / 16777215.0;
        }

        float ValueNoise(float x, int seed)
        {
            float f0 = floor(x);
            int i = (int)f0;
            float f = x - f0;
            float s = f * f * (3 - (2 * f));
            float a = Hash01(i, seed);
            return a + ((Hash01(i + 1, seed) - a) * s);
        }

        // premultiplied "over": the newer colour on top
        float4 Over(float4 top, float4 under) { return top + (under * (1 - top.a)); }

        float4 Premul(float4 c) { return float4(c.rgb * c.a, c.a); }

        // the haze brush: the colour at alpha .34, .18, .06 and 0 out to the rim
        float4 Haze(float3 color, float r, float opacity)
        {
            float a = r < .34 ? lerp(.34, .18, r / .34) : r < .68 ? lerp(.18, .06, (r - .34) / .34) : r < 1 ? lerp(.06, 0, (r - .68) / .32) : 0;
            return float4(color * a, a) * opacity;
        }

        float4 SparkAt(float r, float opacity)
        {
            float4 c = r < .30 ? lerp(Spark[0], Spark[1], r / .30) : r < .64 ? lerp(Spark[1], Spark[2], (r - .30) / .34)
                : r < 1 ? lerp(Spark[2], float4(Spark[2].rgb, 0), (r - .64) / .36) : 0;
            return Premul(c) * opacity;
        }

        float4 PoolAt(float r, float opacity)
        {
            float4 c = r < .22 ? lerp(Pool[0], Pool[1], r / .22) : r < .52 ? lerp(Pool[1], Pool[2], (r - .22) / .30)
                : r < .80 ? lerp(Pool[2], Pool[3], (r - .52) / .28) : r < 1 ? lerp(Pool[3], float4(Pool[3].rgb, 0), (r - .80) / .20) : 0;
            return Premul(c) * opacity;
        }

        float Ellipse(float2 p, float2 c, float rx, float ry) { return length((p - c) / float2(rx, ry)); }

        // the strand sprite's base-to-tip fade: 1 at the base, .68, .28, .07, then 0 at the tip
        float Fade01(float v)
        {
            return v < .30 ? lerp(1, .68, v / .30) : v < .62 ? lerp(.68, .28, (v - .30) / .32)
                : v < .85 ? lerp(.28, .07, (v - .62) / .23) : lerp(.07, 0, saturate((v - .85) / .15));
        }

        // the flame in two parts: the soft light (haze and pool) and the sharp detail (strands, tips, sparks); only
        // the detail leaves echoes, so the trails stay fine lines instead of a fog
        float4 Hologram(float2 p, out float4 detail)
        {
            float4 acc = 0;
            detail = 0;
            float cx0 = CenterX;

            // the wide bloom behind the strands
            acc = Over(Haze(Family[1].rgb, Ellipse(p, float2(cx0, BaseY - (MaxHeight * .30)), Width * .37, MaxHeight * .64), .11 + (.27 * Energy)), acc);
            acc = Over(Haze(HazeCore.rgb, Ellipse(p, float2(cx0, BaseY - (MaxHeight * .36)), Width * .125, MaxHeight * .62), .05 + (.14 * Energy)), acc);
            acc = Over(Haze(HazeCore.rgb, Ellipse(p, float2(cx0, BaseY - (MaxHeight * .44)), Width * .042, MaxHeight * .54), .03 + (.09 * Energy)), acc);

            // the filaments near this pixel, left to right as the CPU look draws them
            int count = (int)Count;
            float spacing = Width / Count;
            int home = (int)floor(p.x / spacing);
            [loop] for (int k = -3; k <= 3; k++)
            {
                int i = home + k;
                if (i < 0 || i >= count) continue;
                float jitter = Hash01(i, 0x51ED27) - .5;
                float xn = ((i + .5) / Count) - .5;
                float x = clamp(((i + .5) * spacing) + (jitter * spacing * .70), .75, Width - .75);

                float dp = xn - PrimaryCenter, ds = xn - SecondaryCenter;
                float envelope = min(1.12, (exp(-(dp * dp) / (2 * .22 * .22)) * .92) + (exp(-(ds * ds) / (2 * .085 * .085)) * .34));

                float seed = Hash01(i, 0x2F91A3);
                float flicker = .55 + (1.65 * Hash01(i, 0x4D8C61));
                float noise = saturate((.62 * ValueNoise((Time * flicker) + (seed * 63), (int)(seed * 100003)))
                    + (.38 * ValueNoise((Time * ((flicker * 2.3) + .4)) + (seed * 17), (int)(seed * 70001))));
                float flow = ValueNoise((xn * 12) - (Time * .32) + 8.1, 0x48A2);
                float h = envelope * MaxHeight * Amplitude * (.30 + (.42 * noise) + (.28 * flow));
                float clump = .72 + (.50 * ValueNoise((xn * 9) + (Time * .18) + 4.7, 0x1F3D));
                if (Hash01(i, 0x6B12) > .90) clump *= 1.22;
                h = min(MaxHeight, h * clamp(clump, .45, 1.24));
                if (h < .8) continue;

                float ratio = h / MaxHeight;
                float sway = sin((Time * (.35 + (1.25 * Hash01(i, 0x7C3B19)))) + (6.283 * Hash01(i, 0x19AF43))) * 1.5 * ratio * (.35 + (.65 * noise));
                float y0 = BaseY - (abs(Hash01(((i * 7) + 3) % 640, 0x51ED27) - .5) * .8);
                float distance = abs(xn - PrimaryCenter);
                int family = distance < .055 && flow > .48 ? 0 : distance < .26 ? 1 : 2;
                bool heavy = Hash01(i, 0x33A7) > .86;
                int grade = clamp((int)round(Grade * (.66 + (.34 * flow))), 0, 7);

                // the sprite: 10 DIP wide, from just above the tip down to the base
                float v = (y0 - p.y) / (h + 1);
                float dx = abs(p.x - (x + (sway * .5)));
                if (v >= 0 && v <= 1 && dx < 5)
                {
                    float a0 = AlphaLadder[grade] * FamilyScale[family] * Fade01(v);
                    float clear = (1 - (saturate(a0 * .10) * saturate(4.25 - dx + .5)))
                        * (1 - (saturate(a0 * .32) * saturate(2.3 - dx + .5)))
                        * (1 - (saturate(a0) * saturate(.575 - dx + .5)));
                    if (heavy) clear *= 1 - (saturate(a0 * .55) * saturate(1.2 - dx + .5));
                    float a = 1 - clear;
                    acc = Over(float4(Family[family].rgb * a, a), acc);
                    detail = Over(float4(Family[family].rgb * a, a), detail);
                }

                // the bright tip
                float tipAlpha = (.06 + (.24 * noise)) * Energy * (.35 + (.65 * ratio));
                if (tipAlpha > .15)
                {
                    float4 tip = SparkAt(length(p - float2(x + sway, y0 - h)) / 1.05, tipAlpha);
                    acc = Over(tip, acc);
                    detail = Over(tip, detail);
                }
            }

            // the pool along the bottom line, and the white-hot line inside it
            float pool = saturate(.30 + (.45 * sqrt(Level)));
            acc = Over(Haze(Family[1].rgb, Ellipse(p, float2(cx0, BaseY + 1.5), Width * .48, 8.5), pool * .46), acc);
            acc = Over(PoolAt(Ellipse(p, float2(cx0, BaseY + .5), Width * .36, 4.2 + (3.4 * Level)), pool * .72), acc);
            acc = Over(SparkAt(Ellipse(p, float2(cx0, BaseY), Width * .20, 1.6), pool * .36), acc);

            // a few faint sparks drifting up
            float span = min(MaxHeight * 1.02, BaseY - 4);
            [loop] for (int j = 0; j < 20; j++)
            {
                float phase = Hash01(j, 0x38E4A1);
                float progress = frac(phase + (Time * (.018 + (.030 * Hash01(j, 0x5FA0C7)))));
                float alpha = pow(sin(3.14159 * progress), 1.6) * .42 * Energy;
                float2 c = float2(cx0 + ((Hash01(j, 0x71B2D9) - .5) * Width * .78) + (sin((Time * (.12 + (.38 * Hash01(j, 0x27C6E5)))) + (phase * 9)) * 3),
                    BaseY - 4 - (progress * span));
                float size = .55 + (1.05 * Hash01(j, 0x6A14B3));
                if (abs(p.y - c.y) < size && abs(p.x - c.x) < size)
                {
                    float4 spark = SparkAt(length(p - c) / size, alpha);
                    acc = Over(spark, acc);
                    detail = Over(spark, detail);
                }
            }

            return acc;
        }

        // the trail keeps the echoes at full strength for the next frame; the strip shows them dimmer
        struct Output
        {
            float4 Trail : SV_Target0;
            float4 Shown : SV_Target1;
        };

        Output main(float4 position : SV_Position)
        {
            float2 p = position.xy / Ppd;

            // MilkDrop: where this pixel's echo came from a frame ago. The echoes rise, spread out from the
            // middle (or draw in), curl outward on both halves and ripple sideways.
            float2 o = float2(CenterX, BaseY);
            float2 q = p - o;
            q.x /= 1 + Spread;
            float2 back = o + q;
            back.x -= sign(q.x) * Lean * max(0, BaseY - p.y);
            back.x += sin((p.y * WaveFreq * 6.283) + WavePhase + (p.x * .004)) * WaveAmp;
            back.y += Rise;
            [unroll] for (int w = 0; w < 3; w++)
            {
                float2 heart = float2(CenterX + (sin((Time * (.11 + (.04 * w))) + (w * 2.1)) * Width * .3), BaseY - (MaxHeight * .45));
                float2 d = back - heart;
                float a = Whirl * (w == 1 ? -1 : 1) * exp(-dot(d, d) / (45 * 45));
                float sa = sin(a), ca = cos(a);
                back = heart + float2((d.x * ca) - (d.y * sa), (d.x * sa) + (d.y * ca));
            }

            back = back * Ppd / Size;
            float inside = all(back >= 0) && all(back <= 1) ? 1 : 0;
            float4 echo = Source.SampleLevel(Linear, back, 0) * Decay * inside;

            // and the faintest of them go out at once, so only the bright lines leave trails, not a haze
            echo = max(0, echo - (Darken * echo.a / max(echo.a, 1e-3)));

            // older echoes sink into the tint's deeper edge colour
            echo.rgb = lerp(echo.rgb, Family[2].rgb * echo.a, Age);

            // the fresh flame stays on top at its own strength
            float4 detail;
            float4 fresh = Hologram(p, detail);
            Output result;
            result.Trail = max(echo, detail);
            result.Shown = max(echo * EchoGain, fresh);
            return result;
        }
        """;

    private const string OutPs = Constants + """

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float4 c = Source.Load(int3(position.xy, 0));
            return saturate(c) * Fade;
        }
        """;
}
