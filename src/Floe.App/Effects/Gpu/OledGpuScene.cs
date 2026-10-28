using Floe.App.Services;
using Floe.Core.Interop;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// The OLED visualizer: the whole strip, edge to edge, becomes one long black monochrome panel like the
/// SSD1306 screens on ESP32 boards, playing a MilkDrop-style feedback show on its chunky white pixels.
/// <para>
/// Each frame takes the last one, warps it a little (zoom, flow, whirl, each driven by the bass, the mids and
/// the beat) and dims it, then draws the music's own waveform on top: the warp smears every trace into
/// tunnels, rivers and spirals that move with the song. That runs on the GPU at three samples per panel pixel;
/// a second pass averages each panel pixel's samples into a few grey levels, and the last one draws the cells
/// as rounded dots with a faint glow. The presets take turns every <see cref="CycleSeconds"/>, changing on a
/// kick: five bend the waveform into shapes (0..4), five draw something else (6..10: spectrum bars, sonar rings,
/// a starfield, rain and plasma blobs). A Tron theme lights the dots in its colours and adds the grid floor (5),
/// which plays alone when the theme comes on. Three switches dress the panel up further: the user's own colours
/// across the strip, an RGB triad in every cell like a video wall, and glass drops that swell in on every kick.
/// </para>
/// </summary>
public sealed class OledGpuScene : IGpuScene
{
    private const int Bands = 24;
    private const int Presets = 11;

    // the grid floor, which only joins the cycle while a Tron theme runs
    private const int GridPreset = 5;
    private const float GridSeconds = 20;

    // one panel pixel every 2 DIP, as chunky as the little screens; the feedback runs at 3x3 samples per pixel
    private const float PitchDip = 2;
    // an RGB triad packs three emitters into one cell, so the wall gets finer when it is on
    private const float TriadPitchDip = 1.5f;

    // the bezel along the top and the sides, and how round its top corners are
    private const float BezelDip = 8;
    private const float CornerDip = 12;
    private const int Samples = 3;

    // the trails: how much of the last frame survives, per 60 fps frame
    private const float Decay = .9f;
    // a change of preset dims the old one out, then brings the new one up: they never draw over each other
    private const float BlendSeconds = 1.2f;

    // the glass drops: four at most on the glass, each swelling in on a kick and then fading out
    private const int MaxLenses = 4;
    private const float LensSwell = .15f;
    private const float LensLife = 1.05f;
    private const float LensRadius = .45f;

    private readonly float[] _bands = new float[Bands];
    private float _beat, _flash, _level, _bass, _mid, _treble, _hat;
    private float _phase, _time;
    private bool _kickArmed = true;
    private float _kickCooldown;
    private readonly Random _random = new(1);

    private ID3D11Device? _device;
    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _warpPs, _cellPs, _panelPs;
    private ID3D11Buffer? _warpConstants, _panelConstants;
    private ID3D11BlendState? _opaque;
    private ID3D11RasterizerState? _raster;
    private ID3D11DepthStencilState? _noDepth;
    private ID3D11SamplerState? _linear;
    private Target? _feedback0, _feedback1, _cells;
    private ID3D11Texture2D? _audioTexture;
    private ID3D11ShaderResourceView? _audioView;
    private ID3D11Texture2D? _overlayTexture;
    private ID3D11ShaderResourceView? _overlayView;

    // the song and the robot, drawn into the cells on top of the show
    private readonly OledOverlay _overlay = new();
    private float[] _overlayValue = Array.Empty<float>(), _overlayMask = Array.Empty<float>(), _overlayPacked = Array.Empty<float>();
    private float _activity;
    private bool _needClear;
    private int _ping;

    private readonly ID3D11Buffer[] _constantBuffer = new ID3D11Buffer[1];
    private readonly ID3D11SamplerState[] _sampler = new ID3D11SamplerState[1];
    private readonly ID3D11ShaderResourceView[] _inputs = new ID3D11ShaderResourceView[2];
    private readonly ID3D11ShaderResourceView[] _noInputs = new ID3D11ShaderResourceView[2];
    private readonly float[] _warpCb = new float[20];
    // the cells, the dots' off, lit and hot colours as three float4, the user's 16 colours, then four glass drops
    private readonly float[] _panelCb = new float[24 + (GpuFrame.PaletteSize * 4) + (MaxLenses * 4)];
    // row 0: the waveform; row 1: the bands
    private readonly float[] _audio = new float[SpectrumFrame.WaveLength * 2];
    private Viewport[] _feedbackViewport = Array.Empty<Viewport>(), _cellViewport = Array.Empty<Viewport>(), _viewport = Array.Empty<Viewport>();

    private int _pixelW, _pixelH;
    private float _pixelsPerDip = 1;
    private float _pitch, _bezel;
    private int _cols, _rows;
    private bool _subpixel;

    private int _preset, _previous;
    private float _presetTime, _blend = 1;
    private DockTheme _theme;
    private float _tronTime;

    // where the drops sit and how old they are, in cells; an age past their life means they are gone
    private readonly float[] _lensX = new float[MaxLenses], _lensY = new float[MaxLenses];
    private readonly float[] _lensAge = { 9, 9, 9, 9 };

    /// <summary>The theme whose light the dots carry; set from the UI, read again on the render thread every frame.</summary>
    public DockTheme Theme { get; set; }

    /// <summary>The dots take the user's colours, picked by how far they sit from the middle of the strip.</summary>
    public bool Colour { get; set; }

    /// <summary>Each cell becomes an RGB triad, like the emitters of a real LED video wall.</summary>
    public bool Subpixels { get; set; }

    /// <summary>Glass drops swell in on every kick, magnifying the cells under them.</summary>
    public bool Lens { get; set; }

    /// <summary>How long each preset plays before the next one blends in.</summary>
    public float CycleSeconds { get; set; } = 20;

    /// <summary>Plays one preset for good instead of taking turns (0..5, the last the grid floor); null takes turns.</summary>
    public int? ForcedEffect { get; set; }

    /// <summary>Whether music plays: the panel stays on without it, the show rests and the robot comes out.</summary>
    public bool MusicPlaying { get; set; }

    /// <summary>The song for the left wing; cover is <see cref="OledOverlay.CoverPixels"/> square BGRA, or null.</summary>
    public void SetMedia(string title, string artist, double position, double duration, bool playing, byte[]? cover) =>
        _overlay.SetMedia(title, artist, position, duration, playing, cover);

    /// <summary>The dock's capsule across the strip, 0..1 from the left; the media keeps to the left of it.</summary>
    public void SetDock(float left, float right) => _overlay.SetDock(left, right);

    // ---- lifecycle --------------------------------------------------------------------------------

    public void Initialize(ID3D11Device device)
    {
        _device = device;
        _vertexShader = device.CreateVertexShader(Compile("vs_4_0", FullscreenVs).Span, null);
        _warpPs = device.CreatePixelShader(Compile("ps_4_0", WarpPs).Span, null);
        _cellPs = device.CreatePixelShader(Compile("ps_4_0", CellPs).Span, null);
        _panelPs = device.CreatePixelShader(Compile("ps_4_0", PanelPs).Span, null);
        _warpConstants = device.CreateBuffer(new BufferDescription((uint)(_warpCb.Length * 4), BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
        _panelConstants = device.CreateBuffer(new BufferDescription((uint)(_panelCb.Length * 4), BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
        _opaque = device.CreateBlendState(BlendDescription.Opaque);
        _raster = device.CreateRasterizerState(RasterizerDescription.CullNone);
        _noDepth = device.CreateDepthStencilState(DepthStencilDescription.None);
        _linear = device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Clamp, 0, 1, ComparisonFunction.Never, 0, float.MaxValue));
        _sampler[0] = _linear;
        _audioTexture = device.CreateTexture2D(new Texture2DDescription(Format.R32_Float, SpectrumFrame.WaveLength, 2, 1, 1,
            BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
        _audioView = device.CreateShaderResourceView(_audioTexture, null);
    }

    public void Resize(int width, int height) => Build(width, height, _pixelsPerDip);

    /// <summary>Drops the GPU objects; the show starts over from black with a new device.</summary>
    public void ReleaseDevice()
    {
        ReleaseSized();
        _vertexShader?.Dispose(); _vertexShader = null;
        _warpPs?.Dispose(); _warpPs = null;
        _cellPs?.Dispose(); _cellPs = null;
        _panelPs?.Dispose(); _panelPs = null;
        _warpConstants?.Dispose(); _warpConstants = null;
        _panelConstants?.Dispose(); _panelConstants = null;
        _constantBuffer[0] = null!;
        _opaque?.Dispose(); _raster?.Dispose(); _noDepth?.Dispose(); _linear?.Dispose();
        _opaque = null; _raster = null; _noDepth = null; _linear = null;
        _sampler[0] = null!;
        _audioView?.Dispose(); _audioView = null;
        _audioTexture?.Dispose(); _audioTexture = null;
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
        _subpixel = Subpixels;
        _pitch = (Subpixels ? TriadPitchDip : PitchDip) * _pixelsPerDip;
        _bezel = BezelDip * _pixelsPerDip;
        _cols = Math.Max(8, (int)((width - (2 * _bezel)) / _pitch));
        _rows = Math.Max(4, (int)((height - _bezel) / _pitch));

        _feedback0 = new Target(_device, _cols * Samples, _rows * Samples);
        _feedback1 = new Target(_device, _cols * Samples, _rows * Samples);
        _cells = new Target(_device, _cols, _rows);
        _overlayTexture = _device.CreateTexture2D(new Texture2DDescription(Format.R32G32_Float, (uint)_cols, (uint)_rows, 1, 1,
            BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
        _overlayView = _device.CreateShaderResourceView(_overlayTexture, null);
        _overlayValue = new float[_cols * _rows];
        _overlayMask = new float[_cols * _rows];
        _overlayPacked = new float[_cols * _rows * 2];
        _feedbackViewport = new[] { new Viewport(0, 0, _cols * Samples, _rows * Samples, 0, 1) };
        _cellViewport = new[] { new Viewport(0, 0, _cols, _rows, 0, 1) };
        _viewport = new[] { new Viewport(0, 0, width, height, 0, 1) };
        _needClear = true;
    }

    private void ReleaseSized()
    {
        _feedback0?.Dispose(); _feedback0 = null;
        _feedback1?.Dispose(); _feedback1 = null;
        _cells?.Dispose(); _cells = null;
        _overlayView?.Dispose(); _overlayView = null;
        _overlayTexture?.Dispose(); _overlayTexture = null;
        _pixelW = _pixelH = 0;
    }

    // ---- one frame --------------------------------------------------------------------------------

    public void Render(ID3D11DeviceContext context, ID3D11RenderTargetView target, GpuFrame frame)
    {
        if (_device is null || _warpPs is null || frame.Width < 2 || frame.Height < 2) return;

        // the host knows the DPI only with the frame, so the grid is built here the first time
        var ppd = frame.PixelsPerDip > 0 ? frame.PixelsPerDip : 1;
        if (frame.Width != _pixelW || frame.Height != _pixelH || MathF.Abs(ppd - _pixelsPerDip) > .001f || _subpixel != Subpixels)
        {
            Build(frame.Width, frame.Height, ppd);
        }

        if (_feedback0 is null || _feedback1 is null || _cells is null) return;

        float dt = Math.Clamp(frame.Dt, 0, .05f);
        var theme = Theme;
        bool tron = theme is DockTheme.TronLegacy or DockTheme.TronAres;
        TrackTheme(theme, dt);
        bool kick = UpdateAudio(dt, frame.Audio);
        Advance(dt, kick, tron);
        _activity += ((MusicPlaying ? 1 : 0) - _activity) * (1 - MathF.Exp(-dt * 2));
        // a Tron theme keeps its own light, whatever the user asked for
        bool colour = Colour && !tron;
        UpdateLenses(dt, kick);

        _overlay.Draw(_overlayValue, _overlayMask, _cols, _rows, dt, _time);
        for (int i = 0; i < _overlayValue.Length; i++)
        {
            _overlayPacked[2 * i] = _overlayValue[i];
            _overlayPacked[(2 * i) + 1] = _overlayMask[i];
        }

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

        // the music, for the shader to draw: the scope trace and the bands
        Array.Copy(frame.Audio.Wave, _audio, SpectrumFrame.WaveLength);
        Array.Copy(_bands, 0, _audio, SpectrumFrame.WaveLength, Bands);
        context.UpdateSubresource(new ReadOnlySpan<float>(_audio), _audioTexture!, 0, SpectrumFrame.WaveLength * sizeof(float));

        // 1: last frame, warped and dimmed, with the waveform drawn on top
        var from = _ping == 0 ? _feedback0 : _feedback1;
        var to = _ping == 0 ? _feedback1 : _feedback0;
        _ping ^= 1;
        _warpCb[0] = from.Width; _warpCb[1] = from.Height;
        _warpCb[2] = from.Width / (float)from.Height;
        _warpCb[3] = dt * 60;
        _warpCb[4] = _previous; _warpCb[5] = _preset;
        _warpCb[6] = Smooth(_blend);
        _warpCb[7] = MathF.Pow(Decay, dt * 60);
        _warpCb[8] = _bass; _warpCb[9] = _mid; _warpCb[10] = _treble; _warpCb[11] = _beat;
        _warpCb[12] = _level; _warpCb[13] = _phase; _warpCb[14] = _flash; _warpCb[15] = _hat;
        _warpCb[16] = _time; _warpCb[17] = Samples;
        _warpCb[18] = _activity;
        _warpCb[19] = 0;
        Pass(context, _warpPs, to.View, _feedbackViewport, _warpConstants!, _warpCb, from.Srv, _audioView);

        // 2: every panel pixel, the average of its samples, in a few grey levels
        context.UpdateSubresource(new ReadOnlySpan<float>(_overlayPacked), _overlayTexture!, 0, (uint)(_cols * 2 * sizeof(float)));
        Pass(context, _cellPs!, _cells.View, _cellViewport, _warpConstants!, _warpCb, to.Srv, _overlayView);

        // 3: the cells as dots on the black glass
        _panelCb[0] = _pixelW; _panelCb[1] = _pixelH;
        _panelCb[2] = _cols; _panelCb[3] = _rows;
        float originX = (_pixelW - (_cols * _pitch)) * .5f;
        // each triad bar is about a pixel wide, so the cells have to sit on the pixel grid to read as three
        if (Subpixels) originX = MathF.Floor(originX);
        _panelCb[4] = originX;
        _panelCb[5] = _pixelH - (_rows * _pitch);
        _panelCb[6] = _pitch;
        _panelCb[7] = Math.Clamp(frame.Fade, 0, 1);
        _panelCb[8] = _bezel;
        _panelCb[9] = CornerDip * _pixelsPerDip;
        _panelCb[10] = _pixelsPerDip;
        _panelCb[11] = (colour ? 1 : 0) | (Subpixels ? 2 : 0) | (Lens ? 4 : 0);
        SetDotColors(theme, tron);
        SetPalette(frame);
        SetLenses();
        Pass(context, _panelPs!, target, _viewport, _panelConstants!, _panelCb, _cells.Srv, null);
        context.PSSetShaderResources(0, _noInputs);
    }

    private void Pass(ID3D11DeviceContext context, ID3D11PixelShader shader, ID3D11RenderTargetView destination, Viewport[] viewport,
        ID3D11Buffer constants, float[] values, ID3D11ShaderResourceView source, ID3D11ShaderResourceView? second)
    {
        // nothing may still be bound as an input while it is the target
        context.PSSetShaderResources(0, _noInputs);
        context.RSSetViewports(viewport);
        context.OMSetRenderTargets(destination, null);
        context.PSSetShader(shader);
        context.UpdateSubresource(values, constants);
        _constantBuffer[0] = constants;
        context.PSSetConstantBuffers(0, _constantBuffer);
        _inputs[0] = source;
        _inputs[1] = second!;
        context.PSSetShaderResources(0, _inputs);
        context.Draw(3, 0);
    }

    // A Tron theme brings the grid floor in at once, lights the dots in its colours and keeps the grid alone for
    // its first moments; other themes keep the monochrome show and the five presets.
    private void TrackTheme(DockTheme theme, float dt)
    {
        bool tron = theme is DockTheme.TronLegacy or DockTheme.TronAres;
        if (tron != (_theme is DockTheme.TronLegacy or DockTheme.TronAres))
        {
            _tronTime = 0;
            if (tron)
            {
                _previous = _blend < 1 ? _previous : _preset;
                _preset = GridPreset;
                _presetTime = 0;
                _blend = 0;
            }
        }

        _theme = theme;
        if (tron) _tronTime += dt;
    }

    // Takes turns between the presets: when one has played its time, the next kick brings in the next one.
    private void Advance(float dt, bool kick, bool tron)
    {
        _blend = MathF.Min(1, _blend + (dt / BlendSeconds));
        _presetTime += dt;
        if (!tron && _preset == GridPreset && ForcedEffect != GridPreset)
        {
            // the grid leaves with its theme; the blend still draws it on its way out
            _previous = _blend < 1 ? _previous : _preset;
            _preset = 0;
            _presetTime = 0;
            _blend = 0;
        }

        int wanted = _preset;
        if (ForcedEffect is int forced)
        {
            wanted = Math.Clamp(forced, 0, Presets - 1);
        }
        else if (tron && _tronTime < GridSeconds)
        {
            wanted = GridPreset;
        }
        else if (_presetTime >= CycleSeconds && (kick || _presetTime >= CycleSeconds + 4))
        {
            // any other preset; the grid floor only with its theme
            do wanted = _random.Next(Presets);
            while (wanted == _preset || (!tron && wanted == GridPreset));
        }

        if (wanted == _preset) return;
        _previous = _blend < 1 ? _previous : _preset;
        _preset = wanted;
        _presetTime = 0;
        _blend = 0;
    }

    // The panel's three dot colours: the monochrome look as it always was, or the theme's dim glass, lines and heat.
    private void SetDotColors(DockTheme theme, bool tron)
    {
        if (tron)
        {
            var palette = ThemePalette.For(theme);
            _panelCb[12] = (palette.Dim.R / 255f * .25f) + .02f;
            _panelCb[13] = (palette.Dim.G / 255f * .25f) + .02f;
            _panelCb[14] = (palette.Dim.B / 255f * .25f) + .02f;
            _panelCb[15] = 1;
            _panelCb[16] = palette.Line.R / 255f;
            _panelCb[17] = palette.Line.G / 255f;
            _panelCb[18] = palette.Line.B / 255f;
            _panelCb[19] = 1;
            _panelCb[20] = palette.Hot.R / 255f;
            _panelCb[21] = palette.Hot.G / 255f;
            _panelCb[22] = palette.Hot.B / 255f;
            _panelCb[23] = 1;
            return;
        }

        _panelCb[12] = .063f; _panelCb[13] = .067f; _panelCb[14] = .078f; _panelCb[15] = 1;
        _panelCb[16] = .90f; _panelCb[17] = .95f; _panelCb[18] = 1; _panelCb[19] = 1;
        _panelCb[20] = .90f; _panelCb[21] = .95f; _panelCb[22] = 1; _panelCb[23] = 1;
    }

    // The user's colours, from the middle of the strip out to its ends, as 16 float4 for the panel shader.
    private void SetPalette(GpuFrame frame)
    {
        for (int i = 0; i < GpuFrame.PaletteSize; i++)
        {
            var colour = frame.Palette[i];
            int at = 24 + (i * 4);
            _panelCb[at] = colour.X;
            _panelCb[at + 1] = colour.Y;
            _panelCb[at + 2] = colour.Z;
            _panelCb[at + 3] = 1;
        }
    }

    // The drops age every frame; a kick brings a new one in at a random spot along the strip. When all four
    // are still on the glass the oldest one makes way, so the show never loses the music's pulse.
    private void UpdateLenses(float dt, bool kick)
    {
        for (int i = 0; i < MaxLenses; i++) _lensAge[i] += dt;
        if (!Lens || !kick) return;

        int oldest = 0;
        for (int i = 1; i < MaxLenses; i++) if (_lensAge[i] > _lensAge[oldest]) oldest = i;
        _lensAge[oldest] = 0;
        _lensX[oldest] = (.06f + (.88f * (float)_random.NextDouble())) * _cols;
        _lensY[oldest] = _rows * .5f;
    }

    // The drops as the shader wants them: centre, radius and strength in cells, strength 0 once they have gone.
    private void SetLenses()
    {
        for (int i = 0; i < MaxLenses; i++)
        {
            int at = 24 + (GpuFrame.PaletteSize * 4) + (i * 4);
            float age = _lensAge[i];
            if (age >= LensLife)
            {
                _panelCb[at] = _panelCb[at + 1] = _panelCb[at + 2] = _panelCb[at + 3] = 0;
                continue;
            }

            // it swells in over a moment, then fades away slowly while it magnifies
            float swell = Smooth(Math.Clamp(age / LensSwell, 0, 1));
            float fade = 1 - Smooth(Math.Clamp((age - LensSwell) / (LensLife - LensSwell), 0, 1));
            _panelCb[at] = _lensX[i];
            _panelCb[at + 1] = _lensY[i];
            _panelCb[at + 2] = LensRadius * _rows * swell;
            _panelCb[at + 3] = fade * swell;
        }
    }

    private static float Smooth(float t) => t * t * (3 - (2 * t));

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

        _bass += (Math.Clamp(audio.Bass, 0, 1) - _bass) * smooth;
        _mid += (Math.Clamp(audio.Mid, 0, 1) - _mid) * smooth;
        _treble += (Math.Clamp(audio.Treble, 0, 1) - _treble) * smooth;
        _level += (Math.Clamp(audio.Level, 0, 1) - _level) * smooth;
        _hat = MathF.Max(_hat * MathF.Exp(-dt * 12), Math.Clamp(audio.Hat, 0, 1));
        _beat = MathF.Max(_beat * MathF.Exp(-dt * 5), Math.Clamp(audio.Kick, 0, 1));
        _flash = MathF.Max(_flash * MathF.Exp(-dt * 10), Math.Clamp(audio.Kick, 0, 1));

        // the shapes move faster the busier the music is
        _time += dt;
        _phase += dt * (.4f + (1.6f * _mid) + (1.2f * _beat));

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

    /// <summary>One float texture: its render target and its shader view, always together.</summary>
    private sealed class Target : IDisposable
    {
        private readonly ID3D11Texture2D _texture;

        public Target(ID3D11Device device, int width, int height)
        {
            Width = width;
            Height = height;
            _texture = device.CreateTexture2D(new Texture2DDescription(Format.R16_Float, (uint)width, (uint)height, 1, 1,
                BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
            View = device.CreateRenderTargetView(_texture, null);
            Srv = device.CreateShaderResourceView(_texture, null);
        }

        public int Width { get; }
        public int Height { get; }
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

    private const string WarpConstants = """
        cbuffer Warp : register(b0)
        {
            float2 Size;
            float Aspect;
            float Step;
            float PresetA;
            float PresetB;
            float Blend;
            float Decay;
            float Bass;
            float Mid;
            float Treble;
            float Beat;
            float Level;
            float Phase;
            float Flash;
            float Hat;
            float Time;
            float SamplesPerCell;
            float Activity;
        };
        """;

    // Positions are in strip heights from the middle of the strip, x to the right and y up: the strip runs
    // from about -12 to 12 across and from -.5 to .5 up. Source() says where a point's light was a frame ago.
    private const string WarpPs = WarpConstants + """

        Texture2D<float> Previous : register(t0);
        Texture2D<float> Music : register(t1);
        SamplerState Linear : register(s0);

        float2 Turn(float2 q, float a)
        {
            float s = sin(a), c = cos(a);
            return float2((c * q.x) - (s * q.y), (s * q.x) + (c * q.y));
        }

        float Hash(float2 c)
        {
            return frac(sin(dot(c, float2(12.9898, 78.233))) * 43758.5453);
        }

        // the sonar's three centres along the strip, and the one nearest p
        float2 SonarCentre(float2 p)
        {
            float gap = Aspect / 3;
            return float2(clamp(round(p.x / gap), -1, 1) * gap, 0);
        }

        // the sonar's rings are ellipses four times wider than tall, so they run out along the strip
        static const float2 SonarShape = float2(.25, 1);

        // one panel pixel, in strip heights
        float CellSize()
        {
            return SamplesPerCell / Size.y;
        }

        float2 Source(int preset, float2 p)
        {
            float k = Step;
            if (preset == 6)
            {
                // spectrum: what the bars leave above them sinks slowly, so the peaks fall
                return p + float2(0, .005 * k);
            }

            // the sonar, the starfield, the rain and the plasma draw themselves whole every frame
            if (preset >= 7) return p;
            if (preset == 0)
            {
                // tunnel: everything streams out of the middle, bursting on the beat
                float2 zoom = float2(.0012 + (.003 * Bass) + (.01 * Beat), .018 + (.03 * Bass) + (.07 * Beat)) * k;
                float2 s = p * (1 - zoom);
                s.y += .006 * k * sin((p.x * 1.3) - (Phase * 2));
                return s;
            }

            if (preset == 1)
            {
                // river: the light flows out to both ends, wobbling on the mids
                float2 s = p;
                s.x -= sign(p.x) * (.012 + (.04 * Bass) + (.12 * Beat)) * k;
                s.y = (s.y * (1 - (.008 * k))) + (.012 * k * sin((p.x * 1.7) - (Phase * 3)) * (.3 + Mid));
                return s;
            }

            if (preset == 2)
            {
                // whirlpools: a row of them, every other one turning the other way, pulling the light in
                const float width = 1.6;
                float t = floor((p.x / width) + .5);
                float2 q = p - float2(t * width, 0);
                float turn = (fmod(abs(t), 2) < 1 ? 1 : -1) * (.025 + (.05 * Mid) + (.14 * Beat)) * k;
                q = Turn(q, turn * saturate(1 - (length(q) / .8))) * (1 + (.006 * k));
                return q + float2(t * width, 0);
            }

            if (preset == 3)
            {
                // smoke: the spectrum rises off the bottom edge and curls on the highs
                float2 s = p;
                s.y -= (.01 + (.025 * Level) + (.03 * Beat)) * k;
                s.x += .012 * k * sin((p.y * 9) + (Phase * 2) + (p.x * .7)) * (.4 + Treble);
                return s;
            }

            // the grid floor draws itself whole every frame; its light only fades where it was
            if (preset == 5) return p;

            // echo: the traces fold in towards the middle line, ring after ring
            float2 s = p;
            s.y *= 1 + ((.022 + (.05 * Bass) + (.1 * Beat)) * k);
            s.x *= 1 + (.0008 * k);
            return s;
        }

        float Wave(float x)
        {
            return Music.SampleLevel(Linear, float2(saturate(x), .25), 0);
        }

        float Band(float x)
        {
            float b = saturate(x) * 23;
            int i = (int)b;
            return lerp(Music.Load(int3(i, 1, 0)), Music.Load(int3(min(i + 1, 23), 1, 0)), b - i);
        }

        // a line of the given half width (in strip heights) at distance d
        float Line(float d, float width)
        {
            return saturate(1.5 - (abs(d) / width));
        }

        float Draw(int preset, float2 p, float2 position)
        {
            float px = 1 / Size.y;
            float width = (1.1 + (1.5 * Flash)) * px;
            float half = Aspect * .5;
            float mirror = abs(p.x) / half;
            float amp = .12 + (.25 * Level) + (.12 * Flash);

            if (preset == 0)
            {
                // a ring of the trace in the middle, and the trace along the strip
                float r = length(p);
                float a = atan2(p.y, p.x);
                float ring = .26 + (.1 * Wave(abs(a) / 3.1416));
                return max(Line(r - ring, width), Line(p.y - (amp * Wave(mirror)), width));
            }

            if (preset == 1) return Line(p.y - (amp * 1.3 * Wave(mirror)), width);

            if (preset == 2) return Line(p.y - (amp * 1.2 * Wave((p.x / Aspect) + .5)), width);

            if (preset == 3)
            {
                // the bands along the bottom edge, the bass out at the ends; a faint trace above them
                float bottom = p.y + .5;
                float bars = step(bottom, .05 + (.4 * pow(Band(1 - mirror), 1.5))) * step(.5, frac(abs(p.x) * 6));
                return max(bars, .5 * Line(p.y + .15 - (amp * .6 * Wave(mirror)), width));
            }

            if (preset == 6)
            {
                // spectrum: LED bars standing on the bottom edge, the bass in the middle, the highs out at the ends
                const float bars = 56;
                float x = ((p.x / Aspect) + .5) * bars;
                float band = abs(floor(x) - ((bars / 2) - .5)) / (bars / 2);
                float h = .04 + (.92 * saturate(pow(Band(band), .9) * (1.1 + (.8 * Level))));
                float bottom = p.y + .5;
                float lit = step(bottom, h) * step(frac(x), .78) * step(frac(bottom * 14), .72);
                return lit * (bottom > h - (1.0 / 14) ? 1 : .7);
            }

            if (preset == 7)
            {
                // sonar: three centres keep sending rings out along the strip, quicker with the music; each ring
                // thins and dims as it spreads, and the beat lights them all up
                float2 c = SonarCentre(p);
                float r = length((p - c) * SonarShape);
                float run = (Time * .22) + (Phase * .12) + (c.x * .05);
                float rings = 0;
                [unroll] for (int j = 0; j < 4; j++)
                {
                    float age = frac(run + (j * .25));
                    float reach = age * 1.15;
                    rings = max(rings, Line(r - reach, width * (1.6 - age)) * pow(1 - age, 1.3));
                }

                float core = step(length(p - c), .04 + (.05 * Bass));
                return max(rings * (.55 + (.45 * saturate(Beat + Level))), core * .9);
            }

            if (preset == 8)
            {
                // starfield at warp: stars streak out from the middle to both ends, speeding up as they go,
                // their tails longer and the whole rush faster the louder the music
                float cell = CellSize();
                float row = floor(p.y / cell);
                float half = Aspect * .5;
                float lit = 0;
                [unroll] for (int i = 0; i < 4; i++)
                {
                    float h = Hash(float2(row, i));
                    float side = Hash(float2(row, i + 7)) > .5 ? 1 : -1;
                    float run = frac((h * 7.3) + (Time * (.05 + (.1 * h))) + (Phase * .05 * (.5 + h)));
                    float head = run * run * half;
                    float tail = (.2 + (run * 3)) * (.5 + Level + Beat);
                    float back = head - (p.x * side);
                    float along = step(0, back) * saturate(1 - (back / tail));
                    lit = max(lit, along * along * (.3 + (.7 * run)));
                }

                return lit;
            }

            if (preset == 9)
            {
                // rain: a drop falls down every other column with a fading trail, faster and brighter where
                // that column's band is loud
                float cell = CellSize();
                float column = floor(p.x / cell);
                if (fmod(abs(column), 2) >= 1) return 0;
                float band = Band(abs(((column * cell) / Aspect) * 2));
                float h = Hash(float2(column, 3));
                float fall = frac((h * 13.7) + (Time * (.35 + (.5 * h))) + (Phase * (.08 + (.1 * h))));
                float head = .7 - (fall * 1.6);
                float tail = .25 + (.35 * h);
                float back = p.y - head;
                float trail = step(0, back) * pow(saturate(1 - (back / tail)), 1.5);
                return trail * (.35 + (.65 * saturate(band * 1.5 + Beat)));
            }

            if (preset == 10)
            {
                // plasma: five blobs drift and swell with the bass, melting together where they meet,
                // outlined, ringed inside and filled with a dim glow
                float field = 0;
                float swell = .3 + (.12 * Bass) + (.15 * Beat);
                [unroll] for (int i = 0; i < 6; i++)
                {
                    float2 c = float2(sin((Time * (.09 + (.04 * i))) + (i * 2.1) + (Phase * .3)) * Aspect * .4,
                        sin((Time * (.6 + (.17 * i))) + (i * 1.3)) * .3);
                    float2 d = p - c;
                    field += (swell * swell) / max(dot(d, d), 1e-4);
                }

                float edge = max(fwidth(field), 1e-4);
                float outline = saturate(1.5 - (abs(field - 1) / (edge * 1.5)));
                float inner = saturate(1.5 - (abs(field - 2.5) / (edge * 1.5))) * .6;
                return max(max(outline, inner), step(1, field) * .3);
            }

            if (preset == 5)
            {
                // The Tron floor in perspective: the horizon just above the middle, rows rushing at the viewer with
                // the music, columns running to the vanishing point, and two light cycles racing along the lines.
                // Lines keep a few samples' width wherever they are (fwidth), and fade where they would crowd.
                const float horizon = .1;
                const float eye = .32;
                float down = max(horizon - p.y, .004);
                float ground = step(.004, horizon - p.y);
                float z = eye / down;
                float x = p.x * z / 1.6;
                float row = z + (Phase * 1.4) + (Time * .35);
                float stroke = 2.2 + (1.2 * Flash);
                float fade = ground * saturate((horizon - p.y) / .05);
                float2 spread = max(float2(fwidth(x), fwidth(row)), 1e-4);
                float2 dist = abs(frac(float2(x, row) - .5) - .5) / spread;
                float2 lines = saturate(1 - (dist / stroke)) * saturate(1.2 - (spread * 2.5));
                // out at the strip's far ends the columns lie almost flat and break into stairs: only the middle keeps them
                lines.x *= saturate((7 - abs(p.x)) / 3);
                float grid = max(lines.x, lines.y) * fade;
                float glow = Line(p.y - horizon, width * 2) * .55;

                float trails = 0;
                float zSpread = max(fwidth(z), 1e-4);
                [unroll] for (int i = 0; i < 2; i++)
                {
                    float t = (Time * (.45 + (.3 * Beat))) + (i * 3.3);
                    float leg = floor(t / 3);
                    float head = frac(t / 3);
                    float pick = frac(sin((leg * 12.9898) + (i * 78.233)) * 43758.5453);
                    float tail = 3 + (2.5 * Level);
                    if (fmod(leg + i, 2) < 1)
                    {
                        // across the floor along a row, its trail bright behind it
                        float laneZ = 1 + floor(pick * 4);
                        float side = pick > .5 ? 1 : -1;
                        float headX = side * lerp(-12, 12, head);
                        float back = (headX - x) * side;
                        float along = step(0, back) * saturate(1 - (back / tail));
                        float lane = abs(z - laneZ) / zSpread;
                        trails = max(trails, along * saturate(1 - (lane / (stroke * 2))) * fade);
                    }
                    else
                    {
                        // down a column towards the viewer
                        float laneX = floor(pick * 11) - 5;
                        float headZ = lerp(16, .5, head);
                        float behind = z - headZ;
                        float along = step(0, behind) * saturate(1 - (behind / (tail * 2.5)));
                        float lane = abs(x - laneX) / spread.x;
                        trails = max(trails, along * saturate(1 - (lane / (stroke * 2))) * fade);
                    }
                }

                return max(glow, max(grid * .6, trails));
            }

            // the trace twice, mirrored above and below the middle line
            float w = Wave(mirror);
            return max(Line(abs(p.y) - (.3 + (amp * .5 * w)), width), Line(p.y, width * .8) * Flash);
        }

        float main(float4 position : SV_Position) : SV_Target
        {
            float2 uv = position.xy / Size;
            float2 p = float2((uv.x - .5) * Aspect, .5 - uv.y);

            float2 a = Source((int)PresetA, p);
            float2 b = Source((int)PresetB, p);
            // the old preset warps and draws alone for the first half of a change, the new one for the second;
            // the trace dims to nothing at the hand-over and the light left over fades fast through it
            bool second = Blend >= .5;
            float2 s = second ? b : a;
            float through = 1 - abs((2 * Blend) - 1);
            float old = Previous.SampleLevel(Linear, float2((s.x / Aspect) + .5, .5 - s.y), 0);
            // rain and stars keep long trails, the plasma almost none
            int now = second ? (int)PresetB : (int)PresetA;
            float keep = now >= 7 ? .5 : 1;
            float v = saturate(max(0, (old * Decay * keep * (1 - (.25 * through))) - .012));

            float drawn = (second ? Draw((int)PresetB, p, position.xy) : Draw((int)PresetA, p, position.xy)) * (1 - through);
            v = max(v, drawn * (.85 + (.3 * Flash)) * Activity);

            // the hats sparkle single pixels anywhere on the panel
            float2 cell = floor(position.xy / SamplesPerCell);
            if (Hash(cell + floor(Time * 30) * float2(7.13, 3.71)) < Hat * Activity * .0015) v = 1;
            return v;
        }
        """;

    // Every panel pixel is the average of its samples, lifted a little and cut to five grey levels.
    private const string CellPs = WarpConstants + """

        Texture2D<float> Feedback : register(t0);
        Texture2D<float2> Overlay : register(t1);

        float main(float4 position : SV_Position) : SV_Target
        {
            int n = (int)SamplesPerCell;
            int2 origin = (int2)floor(position.xy) * n;
            float sum = 0;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    sum += Feedback.Load(int3(origin + int2(x, y), 0));
                }
            }

            float v = saturate(sum / (n * n));
            float2 overlay = Overlay.Load(int3(position.xy, 0));
            return lerp(floor((v * 4) + .15) / 4, overlay.x, overlay.y);
        }
        """;

    // Each cell is a rounded dot: off pixels stay a faint grid on the black glass, lit ones glow cool white or the
    // theme's light lines, and the hottest ones heat up to its hot colour. A graphite bezel frames the glass along
    // the top and the sides; the bottom runs on to the screen's edge. Three extras sit on top of that: the user's
    // colours across the strip, an RGB triad per cell like a video wall, and glass drops that swell in on a kick.
    private const string PanelPs = """
        cbuffer Params : register(b0)
        {
            float2 Size;
            float2 Grid;
            float2 Origin;
            float Pitch;
            float Fade;
            float Bezel;
            float Corner;
            float PixelsPerDip;
            float Modes;
            float4 DotOff;
            float4 DotLit;
            float4 DotHot;
            float4 Palette[16];
            // x, y, radius and strength of the glass drops, in cells
            float4 Lens[4];
        };

        // distance to a box open at the bottom with rounded top corners, negative inside
        float Box(float2 p, float left, float top, float right, float radius)
        {
            float2 c = float2(clamp(p.x, left + radius, right - radius), top + radius);
            if (p.y < c.y && (p.x < c.x || p.x > c.x)) return length(p - c) - radius;
            return max(max(left - p.x, p.x - right), top - p.y);
        }

        Texture2D<float> Cells : register(t0);

        float CellAt(int2 c)
        {
            return all(c >= 0) && all(c < (int2)Grid) ? Cells.Load(int3(c, 0)) : 0;
        }

        float CellAt(float2 p)
        {
            return CellAt((int2)floor(p));
        }

        // a rounded vertical bar in the cell, centred on x: one of the triad's three emitters
        float Bar(float2 local, float x)
        {
            float2 d = abs(local - float2(x, .5)) - float2(.10, .30);
            float sd = (length(max(d, 0)) + min(max(d.x, d.y), 0) - .10) * Pitch;
            return saturate(.5 - (sd / .6));
        }

        // A glass drop pulls the lookup point towards its middle, strongest at the centre (a convex lens). Each
        // colour channel bends a little more than the one before, so the rim splits into a thin rainbow.
        float2 Bend(float2 p, float channel)
        {
            [unroll] for (int i = 0; i < 4; i++)
            {
                float4 lens = Lens[i];
                if (lens.w <= 0) continue;
                float2 d = p - lens.xy;
                float k = saturate(length(d) / max(lens.z, .001));
                float pull = lens.w * sqrt(1 - (k * k)) * (.34 + (.09 * channel));
                p = lens.xy + (d * (1 - pull));
            }
            return p;
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            // Modes bits: 1 the user's colours, 2 the RGB triads, 4 the glass drops
            int modes = (int)Modes;
            bool colour = (modes & 1) != 0;
            bool triads = (modes & 2) != 0;
            bool drops = (modes & 4) != 0;

            float2 p = (position.xy - Origin) / Pitch;
            int2 cell = (int2)floor(p);
            float2 local = p - cell;

            // a rounded square 78% of the pitch, its edge softened over about half a pixel
            float2 d = abs(local - .5) - (.39 - .12);
            float sd = (length(max(d, 0)) + min(max(d.x, d.y), 0) - .12) * Pitch;
            bool inside = all(cell >= 0) && all(cell < (int2)Grid);
            float dotMask = inside ? saturate(.5 - (sd / .6)) : 0;

            // a faint glow from the lit neighbours, as the real panels bleed a little light into the glass
            float bloom = 0;
            [unroll] for (int y = -1; y <= 1; y++)
            {
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 c = cell + int2(x, y) + .5;
                    float2 o = p - c;
                    bloom += saturate(CellAt(cell + int2(x, y))) * exp(-dot(o, o) * 2.2);
                }
            }

            // the triads: three bars in the cell, red, green and blue, each lit by its own channel
            float3 mask = dotMask.xxx;
            if (triads)
            {
                mask = 0;
                if (inside)
                {
                    mask = float3(Bar(local, 1.0 / 6), Bar(local, .5), Bar(local, 5.0 / 6));
                }
            }

            float v = saturate(CellAt(cell));

            // under a drop the lookup point is bent per channel, so the cells there read as if through glass
            float3 vv = v.xxx;
            if (drops) vv = float3(CellAt(Bend(p, 0)), CellAt(Bend(p, 1)), CellAt(Bend(p, 2)));

            float3 off = DotOff.rgb;
            float3 lit = DotLit.rgb;
            float3 hot = DotHot.rgb;
            if (colour)
            {
                // the user's colours, picked by how far the cell sits from the middle of the strip
                float at = saturate(abs(((cell.x + .5) / Grid.x) - .5) * 2) * 15;
                int low = (int)floor(at);
                lit = lerp(Palette[low].rgb, Palette[min(low + 1, 15)].rgb, at - low);
                hot = lerp(lit, float3(1, 1, 1), .85);
            }

            float3 c;
            if (colour)
            {
                // dim cells keep a darker shade of their colour; the hottest run towards white
                float3 tint = lerp(lit * .16, lit, saturate((vv - .15) / .6));
                tint = lerp(tint, float3(1, 1, 1), saturate((vv - .8) * 5) * .8);
                c = lerp(off, tint, saturate(vv * 2.5));
            }
            else
            {
                c = lerp(off, lit, vv);
                // the brightest cells run hotter than the lines, up to the theme's heat
                c = lerp(c, hot, saturate((vv - .8) * 5));
            }

            c *= mask;
            // each bar is a third of the cell and lights one channel: pushed up, the wall reads as bright as the dots
            if (triads) c = saturate(c * 2.8);
            float3 bleed = triads ? mask : float3(1, 1, 1);
            c += lit * bloom * .16 * bleed;
            c += float3(.008, .008, .012) * (1 - mask);

            // the drops lie on the glass: a soft highlight towards the upper left, and a faint bright rim
            float3 glare = 0;
            if (drops)
            {
                [unroll] for (int i = 0; i < 4; i++)
                {
                    float4 lens = Lens[i];
                    if (lens.w <= 0) continue;
                    float r = max(lens.z, .001);
                    float2 o = p - lens.xy;
                    float k = length(o) / r;
                    float2 spot = o - (float2(-.36, -.34) * r);
                    float spec = exp(-dot(spot, spot) / (r * r * .05)) * lens.w;
                    float rim = saturate(1 - (abs(k - .94) / .06)) * lens.w;
                    glare += (float3(.85, .92, 1) * spec * .55) + (float3(.7, .85, 1) * rim * .18);
                }
            }

            // the bezel: its outer edge catches a little light, its inner lip is dark where it meets the glass
            float outer = Box(position.xy, 0, 0, Size.x, Corner);
            float inner = Box(position.xy, Bezel, Bezel, Size.x - Bezel, max(1, Corner - Bezel));
            float shape = saturate(.5 - outer);
            float glass = saturate(.5 - inner);
            // a bevel: light graphite at the outer edge, darker towards the glass, a bright rim catching the light
            float depth = saturate(-outer / max(1, Bezel));
            float3 frame = lerp(float3(.36, .37, .40), float3(.15, .155, .17), sqrt(depth));
            frame *= lerp(1.1, .8, saturate(position.y / Size.y));
            frame += float3(.30, .31, .33) * saturate(1 - abs(outer + (.6 * PixelsPerDip)) / PixelsPerDip);
            frame *= lerp(.25, 1, saturate(inner / (1.5 * PixelsPerDip)));
            c = lerp(frame, c, glass);
            c += glare * glass;

            float a = .95 * Fade * shape;
            return float4(saturate(c) * a, a);
        }
        """;
}
