using Floe.Core.Interop;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// The OLED visualizer: the whole strip, edge to edge, becomes one long black monochrome panel like the
/// SSD1306 screens on ESP32 boards, its chunky white pixels playing music-driven pixel animations.
/// <para>
/// The animations run on the CPU on the panel's cell grid (a few thousand cells), into one brightness per
/// cell that fades like an OLED's afterglow; the GPU only draws the cells as rounded dots with a faint glow.
/// Three effects take turns, each for <see cref="CycleSeconds"/>, changing over through a pixel dissolve:
/// fireworks bursts, a Matrix-like rain, and Conway's Game of Life.
/// </para>
/// </summary>
public sealed class OledGpuScene : IGpuScene
{
    private const int Bands = 24;
    private const int Effects = 3;

    // one panel pixel every 3 DIP, as chunky as the little screens
    private const float PitchDip = 3;

    // OLED afterglow per 60 fps frame, and the change-over between effects
    private const float Afterglow = .80f;
    private const float Dissolve = .6f;

    private const int MaxParticles = 3000;

    private readonly float[] _bands = new float[Bands];
    private float _beat;
    private bool _kickArmed = true;
    private float _kickCooldown;
    private readonly Random _random = new(1);

    private ID3D11Device? _device;
    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _panelPs;
    private ID3D11Buffer? _constants;
    private ID3D11BlendState? _opaque;
    private ID3D11RasterizerState? _raster;
    private ID3D11DepthStencilState? _noDepth;
    private ID3D11Texture2D? _cellTexture;
    private ID3D11ShaderResourceView? _cellView;

    private readonly ID3D11Buffer[] _constantBuffer = new ID3D11Buffer[1];
    private readonly ID3D11ShaderResourceView[] _inputs = new ID3D11ShaderResourceView[1];
    private readonly float[] _cb = new float[8];
    private Viewport[] _viewport = Array.Empty<Viewport>();

    private int _pixelW, _pixelH;
    private float _pixelsPerDip = 1;
    private float _pitch;
    private int _cols, _rows;

    // the panel: what the effects draw, what was on screen when the effects changed over, and what goes up
    private float[] _lum = Array.Empty<float>();
    private float[] _old = Array.Empty<float>();
    private float[] _shown = Array.Empty<float>();
    private byte[] _dissolveOrder = Array.Empty<byte>();

    private int _effect;
    private float _effectTime;
    private float _transition;

    // burst
    private readonly float[] _px = new float[MaxParticles], _py = new float[MaxParticles];
    private readonly float[] _vx = new float[MaxParticles], _vy = new float[MaxParticles];
    private readonly float[] _life = new float[MaxParticles], _maxLife = new float[MaxParticles];
    private readonly bool[] _bounced = new bool[MaxParticles];
    private int _particles;
    private float _sparkDebt;

    // rain: one streak per column
    private float[] _head = Array.Empty<float>(), _speed = Array.Empty<float>(), _tail = Array.Empty<float>();
    private bool[] _falling = Array.Empty<bool>();

    // life
    private byte[] _cells = Array.Empty<byte>(), _next = Array.Empty<byte>();
    private float _stepDebt, _seedDebt;

    // the R-pentomino, the glider and the acorn: small seeds that grow for a long while
    private static readonly sbyte[][] Seeds =
    {
        new sbyte[] { 1, 0, 2, 0, 0, 1, 1, 1, 1, 2 },
        new sbyte[] { 1, 0, 2, 1, 0, 2, 1, 2, 2, 2 },
        new sbyte[] { 1, 0, 3, 1, 0, 2, 1, 2, 4, 2, 5, 2, 6, 2 },
    };

    /// <summary>How long each effect plays before the next one dissolves in.</summary>
    public float CycleSeconds { get; set; } = 24;

    /// <summary>Preview only: plays one effect for good instead of taking turns.</summary>
    public int? ForcedEffect { get; set; }

    // ---- lifecycle --------------------------------------------------------------------------------

    public void Initialize(ID3D11Device device)
    {
        _device = device;
        _vertexShader = device.CreateVertexShader(Compile("vs_4_0", FullscreenVs).Span, null);
        _panelPs = device.CreatePixelShader(Compile("ps_4_0", PanelPs).Span, null);
        _constants = device.CreateBuffer(new BufferDescription(32, BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
        _constantBuffer[0] = _constants;
        _opaque = device.CreateBlendState(BlendDescription.Opaque);
        _raster = device.CreateRasterizerState(RasterizerDescription.CullNone);
        _noDepth = device.CreateDepthStencilState(DepthStencilDescription.None);
    }

    public void Resize(int width, int height) => Build(width, height, _pixelsPerDip);

    /// <summary>Drops the GPU objects; the panel and its animations carry on with a new device.</summary>
    public void ReleaseDevice()
    {
        ReleaseSized();
        _vertexShader?.Dispose(); _vertexShader = null;
        _panelPs?.Dispose(); _panelPs = null;
        _constants?.Dispose(); _constants = null;
        _constantBuffer[0] = null!;
        _opaque?.Dispose(); _raster?.Dispose(); _noDepth?.Dispose();
        _opaque = null; _raster = null; _noDepth = null;
        _device = null;
    }

    public void Dispose() => ReleaseDevice();

    private void Build(int width, int height, float pixelsPerDip)
    {
        if (_device is null || width < 2 || height < 2) return;
        ReleaseSized();
        _pixelW = width;
        _pixelH = height;
        _pixelsPerDip = pixelsPerDip > 0 ? pixelsPerDip : 1;
        _pitch = PitchDip * _pixelsPerDip;

        int cols = Math.Max(8, (int)(width / _pitch));
        int rows = Math.Max(4, (int)(height / _pitch));
        if (cols != _cols || rows != _rows)
        {
            _cols = cols;
            _rows = rows;
            int n = cols * rows;
            _lum = new float[n];
            _old = new float[n];
            _shown = new float[n];
            _dissolveOrder = new byte[n];
            _random.NextBytes(_dissolveOrder);
            _head = new float[cols];
            _speed = new float[cols];
            _tail = new float[cols];
            _falling = new bool[cols];
            _cells = new byte[n];
            _next = new byte[n];
            _particles = 0;
        }

        _cellTexture = _device.CreateTexture2D(new Texture2DDescription(Format.R32_Float, (uint)_cols, (uint)_rows, 1, 1,
            BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
        _cellView = _device.CreateShaderResourceView(_cellTexture, null);
        _viewport = new[] { new Viewport(0, 0, width, height, 0, 1) };
    }

    private void ReleaseSized()
    {
        _cellView?.Dispose(); _cellView = null;
        _cellTexture?.Dispose(); _cellTexture = null;
        _pixelW = _pixelH = 0;
    }

    // ---- one frame --------------------------------------------------------------------------------

    public void Render(ID3D11DeviceContext context, ID3D11RenderTargetView target, GpuFrame frame)
    {
        if (_device is null || _panelPs is null || frame.Width < 2 || frame.Height < 2) return;

        // the host knows the DPI only with the frame, so the grid is built here the first time
        var ppd = frame.PixelsPerDip > 0 ? frame.PixelsPerDip : 1;
        if (frame.Width != _pixelW || frame.Height != _pixelH || MathF.Abs(ppd - _pixelsPerDip) > .001f)
        {
            Build(frame.Width, frame.Height, ppd);
        }

        if (_cellTexture is null || _cellView is null) return;

        float dt = Math.Clamp(frame.Dt, 0, .05f);
        bool kick = UpdateAudio(dt, frame.Audio);
        Advance(dt);

        float glow = MathF.Pow(Afterglow, dt * 60);
        for (int i = 0; i < _lum.Length; i++) _lum[i] *= glow;

        switch (_effect)
        {
            case 0: Burst(dt, kick); break;
            case 1: Rain(dt, kick); break;
            default: Life(dt, kick); break;
        }

        Compose();
        context.UpdateSubresource(new ReadOnlySpan<float>(_shown), _cellTexture, 0, (uint)(_cols * sizeof(float)));

        _cb[0] = _pixelW; _cb[1] = _pixelH;
        _cb[2] = _cols; _cb[3] = _rows;
        _cb[4] = (_pixelW - (_cols * _pitch)) * .5f;
        _cb[5] = _pixelH - (_rows * _pitch);
        _cb[6] = _pitch;
        _cb[7] = Math.Clamp(frame.Fade, 0, 1);

        context.RSSetState(_raster);
        context.OMSetDepthStencilState(_noDepth, 0);
        context.OMSetBlendState(_opaque);
        context.RSSetViewports(_viewport);
        context.OMSetRenderTargets(target, null);
        context.IASetInputLayout(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(_vertexShader);
        context.PSSetShader(_panelPs);
        context.PSSetConstantBuffers(0, _constantBuffer);
        _inputs[0] = _cellView;
        context.PSSetShaderResources(0, _inputs);
        context.UpdateSubresource(_cb, _constants!);
        context.Draw(3, 0);
    }

    // Takes turns between the effects; the outgoing panel is kept to dissolve away pixel by pixel.
    private void Advance(float dt)
    {
        _transition = MathF.Max(0, _transition - dt);
        int wanted = ForcedEffect is int forced ? Math.Clamp(forced, 0, Effects - 1) : _effect;
        _effectTime += dt;
        if (ForcedEffect is null && _effectTime >= CycleSeconds) wanted = (_effect + 1) % Effects;
        if (wanted == _effect) return;

        Array.Copy(_lum, _old, _lum.Length);
        Array.Clear(_lum);
        _effect = wanted;
        _effectTime = 0;
        _transition = Dissolve;
        _particles = 0;
        Array.Clear(_falling);
        Array.Clear(_cells);
    }

    private void Compose()
    {
        if (_transition <= 0)
        {
            Array.Copy(_lum, _shown, _lum.Length);
            return;
        }

        // every cell switches over at its own random moment
        int threshold = (int)((1 - (_transition / Dissolve)) * 256);
        for (int i = 0; i < _shown.Length; i++)
        {
            _shown[i] = _dissolveOrder[i] < threshold ? _lum[i] : _old[i];
        }
    }

    private void Plot(int x, int y, float value)
    {
        if ((uint)x >= (uint)_cols || (uint)y >= (uint)_rows) return;
        int i = (y * _cols) + x;
        if (value > _lum[i]) _lum[i] = value;
    }

    // ---- 0: bursts ---------------------------------------------------------------------------------

    // Fireworks: each kick bursts a few rings of pixels at random points along the panel; sparks pop in between.
    private void Burst(float dt, bool kick)
    {
        if (kick)
        {
            int bursts = 3 + _random.Next(4);
            for (int k = 0; k < bursts; k++)
            {
                float x = _random.NextSingle() * _cols;
                float y = _rows * (.15f + (.5f * _random.NextSingle()));
                int count = 60 + _random.Next(61);
                float speed = 10 + (16 * _random.NextSingle()) + (10 * _beat);
                for (int p = 0; p < count; p++)
                {
                    float angle = _random.NextSingle() * MathF.Tau;
                    float s = speed * (.6f + (.4f * _random.NextSingle()));
                    Spawn(x, y, MathF.Cos(angle) * s, MathF.Sin(angle) * s * .8f, 1 + _random.NextSingle());
                }
            }
        }

        // between the kicks the bass keeps sparks coming up from the bottom row
        _sparkDebt += dt * (8 + (90 * _bands[0] * _bands[0]));
        while (_sparkDebt >= 1)
        {
            _sparkDebt -= 1;
            Spawn(_random.NextSingle() * _cols, _rows - 1, (_random.NextSingle() - .5f) * 6, -(14 + (14 * _random.NextSingle())), .6f + (.6f * _random.NextSingle()));
        }

        const float Gravity = 38;
        for (int p = 0; p < _particles; p++)
        {
            _life[p] -= dt;
            if (_life[p] <= 0)
            {
                Remove(p--);
                continue;
            }

            _vy[p] += Gravity * dt;
            _px[p] += _vx[p] * dt;
            _py[p] += _vy[p] * dt;
            if (_py[p] >= _rows - 1 && _vy[p] > 0)
            {
                // one bounce off the bottom edge, then gone
                if (_bounced[p])
                {
                    Remove(p--);
                    continue;
                }

                _bounced[p] = true;
                _py[p] = _rows - 1;
                _vy[p] *= -.45f;
                _vx[p] *= .7f;
            }

            Plot((int)_px[p], (int)_py[p], .35f + (.65f * (_life[p] / _maxLife[p])));
        }
    }

    private void Spawn(float x, float y, float vx, float vy, float life)
    {
        if (_particles >= MaxParticles) return;
        int p = _particles++;
        _px[p] = x; _py[p] = y; _vx[p] = vx; _vy[p] = vy;
        _life[p] = _maxLife[p] = life;
        _bounced[p] = false;
    }

    private void Remove(int p)
    {
        int last = --_particles;
        _px[p] = _px[last]; _py[p] = _py[last]; _vx[p] = _vx[last]; _vy[p] = _vy[last];
        _life[p] = _life[last]; _maxLife[p] = _maxLife[last]; _bounced[p] = _bounced[last];
    }

    // ---- 1: rain -----------------------------------------------------------------------------------

    // Matrix-like streaks; the panel's width is the spectrum, bass on the left, treble on the right.
    private void Rain(float dt, bool kick)
    {
        for (int x = 0; x < _cols; x++)
        {
            float band = _bands[Math.Min(Bands - 1, x * Bands / _cols)];
            if (!_falling[x])
            {
                bool start = _random.NextSingle() < dt * (.08f + (3.5f * band * band));
                if (kick && _random.NextSingle() < .5f) start = true;
                if (!start) continue;
                _falling[x] = true;
                _head[x] = -_random.NextSingle() * 3;
                _speed[x] = 8 + (26 * band) + (6 * _random.NextSingle());
                _tail[x] = 6 + (8 * _random.NextSingle());
            }

            _head[x] += _speed[x] * dt;
            float tail = _tail[x];
            int head = (int)_head[x];
            for (int k = 0; k <= tail; k++)
            {
                float t = 1 - (k / tail);
                Plot(x, head - k, k == 0 ? 1 : .8f * t * t);
            }

            if (_head[x] - tail > _rows) _falling[x] = false;
        }
    }

    // ---- 2: life -----------------------------------------------------------------------------------

    // Conway's rules at 12 steps a second; the bass sows small seeds, a kick drops a handful of live cells.
    private void Life(float dt, bool kick)
    {
        _seedDebt += dt * (.5f + (8 * _bands[0] * _bands[0]));
        while (_seedDebt >= 1)
        {
            _seedDebt -= 1;
            Sow(Seeds[_random.Next(Seeds.Length)], _random.Next(_cols), _random.Next(Math.Max(1, _rows - 3)));
        }

        if (kick)
        {
            int cx = _random.Next(_cols), cy = _random.Next(_rows);
            for (int k = 0; k < 40; k++)
            {
                int x = cx + _random.Next(-6, 7), y = cy + _random.Next(-3, 4);
                if ((uint)y < (uint)_rows) _cells[(y * _cols) + Wrap(x)] = 1;
            }
        }

        _stepDebt += dt * 12;
        while (_stepDebt >= 1)
        {
            _stepDebt -= 1;
            Step();
        }

        int alive = 0;
        for (int i = 0; i < _cells.Length; i++)
        {
            if (_cells[i] == 0) continue;
            alive++;
            _lum[i] = 1;
        }

        // a dying colony is sown again, so the panel never goes dark for long
        if (alive < _cells.Length / 50)
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                if (_random.NextSingle() < .08f) _cells[i] = 1;
            }
        }
    }

    private void Sow(sbyte[] seed, int x, int y)
    {
        for (int k = 0; k + 1 < seed.Length; k += 2)
        {
            int sy = y + seed[k + 1];
            if ((uint)sy < (uint)_rows) _cells[(sy * _cols) + Wrap(x + seed[k])] = 1;
        }
    }

    private void Step()
    {
        for (int y = 0; y < _rows; y++)
        {
            for (int x = 0; x < _cols; x++)
            {
                int n = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int yy = y + dy;
                    if ((uint)yy >= (uint)_rows) continue;
                    int row = yy * _cols;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        n += _cells[row + Wrap(x + dx)];
                    }
                }

                int i = (y * _cols) + x;
                _next[i] = (byte)(n == 3 || (n == 2 && _cells[i] != 0) ? 1 : 0);
            }
        }

        (_cells, _next) = (_next, _cells);
    }

    private int Wrap(int x) => ((x % _cols) + _cols) % _cols;

    // ---- audio ------------------------------------------------------------------------------------

    private bool UpdateAudio(float dt, SpectrumFrame audio)
    {
        var source = audio.Bands;
        float smooth = 1 - MathF.Exp(-dt * 14);
        for (int i = 0; i < Bands; i++)
        {
            float target = i < source.Length ? Math.Clamp(source[i], 0, 1) : 0;
            _bands[i] += (target - _bands[i]) * smooth;
        }

        _beat = MathF.Max(_beat * MathF.Exp(-dt * 5), Math.Clamp(audio.Kick, 0, 1));
        _kickCooldown = MathF.Max(0, _kickCooldown - dt);
        bool kick = false;
        if (audio.Kick > .3f)
        {
            if (_kickArmed && _kickCooldown <= 0)
            {
                kick = true;
                _kickCooldown = .15f;
            }

            _kickArmed = false;
        }
        else
        {
            _kickArmed = true;
        }

        return kick;
    }

    // ---- shaders ----------------------------------------------------------------------------------

    private static ReadOnlyMemory<byte> Compile(string profile, string source) =>
        Compiler.Compile(source, "main", "Oled", profile, ShaderFlags.OptimizationLevel3, EffectFlags.None);

    private const string FullscreenVs = """
        float4 main(uint id : SV_VertexID) : SV_Position
        {
            float2 uv = float2((id << 1) & 2, id & 2);
            return float4((uv.x * 2) - 1, 1 - (uv.y * 2), 0, 1);
        }
        """;

    // Each cell is a rounded dot: off pixels stay a faint grid on the black glass, lit ones glow cool white.
    private const string PanelPs = """
        cbuffer Params : register(b0)
        {
            float2 Size;
            float2 Grid;
            float2 Origin;
            float Pitch;
            float Fade;
        };

        Texture2D<float> Cells : register(t0);

        float Cell(int2 c)
        {
            return all(c >= 0) && all(c < (int2)Grid) ? Cells.Load(int3(c, 0)) : 0;
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 p = (position.xy - Origin) / Pitch;
            int2 cell = (int2)floor(p);
            float2 local = p - cell;

            // a rounded square 78% of the pitch, its edge softened over about half a pixel
            float2 d = abs(local - .5) - (.39 - .12);
            float sd = (length(max(d, 0)) + min(max(d.x, d.y), 0) - .12) * Pitch;
            bool inside = all(cell >= 0) && all(cell < (int2)Grid);
            float dotMask = inside ? saturate(.5 - (sd / .6)) : 0;

            float v = saturate(Cell(cell));

            // a faint glow from the lit neighbours, as the real panels bleed a little light into the glass
            float bloom = 0;
            [unroll] for (int y = -1; y <= 1; y++)
            {
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 c = cell + int2(x, y) + .5;
                    float2 o = p - c;
                    bloom += saturate(Cell(cell + int2(x, y))) * exp(-dot(o, o) * 2.2);
                }
            }

            float3 off = float3(.063, .067, .078);
            float3 lit = float3(.90, .95, 1);
            float3 c = lerp(off, lit, v) * dotMask;
            c += lit * bloom * .16;
            c += float3(.008, .008, .012) * (1 - dotMask);

            // the glass covers the strip edge to edge
            float a = .95 * Fade;
            return float4(saturate(c) * a, a);
        }
        """;
}
