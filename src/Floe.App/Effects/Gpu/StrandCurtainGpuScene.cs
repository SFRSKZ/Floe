using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Floe.App.Services;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// A curtain of vertical dotted strands across the strip. A drifting light, pushed by the music, runs through it:
/// where it is lit the strands swell sideways, ripple, and glow red -> magenta -> pale pink with a blue copy just
/// off their far side. Each strand is a bundle of a few lines; on some strands the lines come apart and wander out
/// of step, and a sudden change in the music makes more of them do it. Two dimmer curtains stand behind, out of
/// focus, so the strip has depth.
/// <para>
/// Ported from the tel-perde.html prototype: the dots are instanced quads summed into a float target, then the
/// picture gets a soft glow at two sizes and a tone map that runs saturated red into pale pink, as an overexposed
/// sensor does. Every method runs on the GPU host's render thread.
/// </para>
/// </summary>
public sealed class StrandCurtainGpuScene : IGpuScene, IThemedGpuScene
{
    // the three curtains: strands, lines per strand, dots per line; keep in step with the shader's tables
    private const int Front = 150 * 3 * 72, Middle = 110 * 2 * 54, Back = 80 * 1 * 36;
    private const int Dots = Front + Middle + Back;
    // the desktop's one curtain, as in the prototype: strands, lines per strand, dots per line
    private const int DesktopDots = 150 * 3 * 1600;
    private const int ConstantFloats = 72;
    private const float Exposure = 8f;

    private readonly float[] _data = new float[ConstantFloats];
    private readonly float[] _pass = new float[8];
    private readonly ID3D11Buffer[] _cb = new ID3D11Buffer[2];
    private readonly ID3D11SamplerState[] _sampler = new ID3D11SamplerState[1];
    private readonly ID3D11ShaderResourceView[] _one = new ID3D11ShaderResourceView[1];
    private readonly ID3D11ShaderResourceView[] _three = new ID3D11ShaderResourceView[3];
    private readonly ID3D11ShaderResourceView[] _none = new ID3D11ShaderResourceView[3];

    private ID3D11Device? _device;
    private ID3D11VertexShader? _dotVs, _fullVs;
    private ID3D11PixelShader? _dotPs, _downPs, _blurPs, _finalPs;
    private ID3D11Buffer? _constants, _passConstants;
    private ID3D11BlendState? _additive, _opaque;
    private ID3D11RasterizerState? _raster;
    private ID3D11DepthStencilState? _noDepth;
    private ID3D11SamplerState? _linear;
    private Target? _hdr, _q1, _q2, _e1, _e2;
    private int _width, _height;

    // the music, as the strands feel it
    private float _flow, _scatter, _swell, _lastLevel, _lastBass, _lastKick, _lastSnare, _gap;

    // the desktop: the prototype's camera, cutting between its shots
    private readonly bool _desktop;
    private float _clock, _shotClock, _music;
    private bool _started;

    /// <param name="desktop">
    /// Draws the whole screen as the tel-perde prototype does (one curtain in 3D, a camera cutting between shots)
    /// instead of the strip's flat three curtains.
    /// </param>
    public StrandCurtainGpuScene(bool desktop = false) => _desktop = desktop;

    public CyberLook? Theme { get; set; }

    public void Initialize(ID3D11Device device)
    {
        _device = device;
        _dotVs = device.CreateVertexShader(Compile("vs_5_0", Shared + Noise + (_desktop ? DesktopVs : DotVs)).Span, null);
        _dotPs = device.CreatePixelShader(Compile("ps_5_0", Shared + DotPs).Span, null);
        _fullVs = device.CreateVertexShader(Compile("vs_5_0", FullVs).Span, null);
        _downPs = device.CreatePixelShader(Compile("ps_5_0", PassHeader + DownPs).Span, null);
        _blurPs = device.CreatePixelShader(Compile("ps_5_0", PassHeader + BlurPs).Span, null);
        _finalPs = device.CreatePixelShader(Compile("ps_5_0", Shared + FinalPs).Span, null);
        _constants = device.CreateBuffer(new BufferDescription(ConstantFloats * 4, BindFlags.ConstantBuffer, ResourceUsage.Default));
        _passConstants = device.CreateBuffer(new BufferDescription((uint)_pass.Length * 4, BindFlags.ConstantBuffer, ResourceUsage.Default));
        _cb[0] = _constants;
        _cb[1] = _passConstants;

        _additive = device.CreateBlendState(new BlendDescription(Blend.One, Blend.One, Blend.One, Blend.One));
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
        _hdr = new Target(_device, width, height);
        int qw = Math.Max(1, width / 4), qh = Math.Max(1, height / 4);
        int ew = Math.Max(1, width / 16), eh = Math.Max(1, height / 16);
        _q1 = new Target(_device, qw, qh);
        _q2 = new Target(_device, qw, qh);
        _e1 = new Target(_device, ew, eh);
        _e2 = new Target(_device, ew, eh);
    }

    public void Render(ID3D11DeviceContext context, ID3D11RenderTargetView target, GpuFrame frame)
    {
        if (_device is null || _dotVs is null || _dotPs is null || _fullVs is null || _downPs is null || _blurPs is null || _finalPs is null) return;
        if (frame.Width < 2 || frame.Height < 2) return;
        if (frame.Width != _width || frame.Height != _height) Resize(frame.Width, frame.Height);
        if (_hdr is null || _q1 is null || _q2 is null || _e1 is null || _e2 is null) return;

        Advance(frame);
        Fill(frame);
        context.UpdateSubresource(_data, _constants!);

        context.RSSetState(_raster);
        context.OMSetDepthStencilState(_noDepth, 0);
        context.IASetInputLayout(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetConstantBuffers(0, _cb);
        context.PSSetConstantBuffers(0, _cb);
        context.PSSetSamplers(0, _sampler);

        // 1: every dot, and its blue ghost, summed into the float target
        context.PSSetShaderResources(0, _none);
        context.RSSetViewport(new Viewport(0, 0, _width, _height, 0, 1));
        context.ClearRenderTargetView(_hdr.Rtv, new Color4(0, 0, 0, 0));
        context.OMSetRenderTargets(_hdr.Rtv, null);
        context.OMSetBlendState(_additive);
        context.VSSetShader(_dotVs);
        context.PSSetShader(_dotPs);
        context.DrawInstanced(6, (uint)((_desktop ? DesktopDots : Dots) * 2), 0, 0);

        // 2: the glow, the picture blurred at a quarter and a sixteenth of its size
        context.OMSetBlendState(_opaque);
        context.VSSetShader(_fullVs);
        Pass(context, _downPs, _hdr, _q1, 0, 0);
        Pass(context, _blurPs, _q1, _q2, 1, 0);
        Pass(context, _blurPs, _q2, _q1, 0, 1);
        Pass(context, _downPs, _q1, _e1, 0, 0);
        Pass(context, _blurPs, _e1, _e2, 1, 0);
        Pass(context, _blurPs, _e2, _e1, 0, 1);

        // 3: tone map onto the strip
        context.OMSetRenderTargets(target, null);
        context.RSSetViewport(new Viewport(0, 0, _width, _height, 0, 1));
        context.PSSetShader(_finalPs);
        _three[0] = _hdr.Srv;
        _three[1] = _q1.Srv;
        _three[2] = _e1.Srv;
        context.PSSetShaderResources(0, _three);
        context.Draw(3, 0);
        context.PSSetShaderResources(0, _none);
    }

    private void Pass(ID3D11DeviceContext context, ID3D11PixelShader shader, Target source, Target destination, float dirX, float dirY)
    {
        context.PSSetShaderResources(0, _none);
        _pass[0] = dirX / source.Width;
        _pass[1] = dirY / source.Height;
        _pass[2] = 0;
        _pass[3] = 0;
        _pass[4] = destination.Width;
        _pass[5] = destination.Height;
        _pass[6] = 0;
        _pass[7] = 0;
        context.UpdateSubresource(_pass, _passConstants!);
        context.OMSetRenderTargets(destination.Rtv, null);
        context.RSSetViewport(new Viewport(0, 0, destination.Width, destination.Height, 0, 1));
        context.PSSetShader(shader);
        _one[0] = source.Srv;
        context.PSSetShaderResources(0, _one);
        context.Draw(3, 0);
    }

    // The music moves the strands three ways: loudness speeds the ripples, a kick swells the lit parts, and a sudden
    // jump or drop makes more strands come apart into their lines for a while.
    private void Advance(GpuFrame frame)
    {
        float dt = Math.Clamp(frame.Dt, 0f, .1f);
        var a = frame.Audio;
        if (!_started)
        {
            // start the camera where the host's clock is, so an offline render can pick its shot by its start time
            _started = true;
            _shotClock = _clock = frame.Time;
        }

        bool kick = a.Kick > .12f && _lastKick <= .12f;
        bool snare = a.Snare > .12f && _lastSnare <= .12f;
        _lastKick = a.Kick;
        _lastSnare = a.Snare;
        _gap += dt;
        bool hit = (kick || snare) && _gap > .22f;
        if (hit) _gap = 0;

        _flow += dt * (_desktop ? .6f + (.4f * _music) + (1.2f * a.Level) : 1f + (1.6f * a.Level));

        float step = Math.Max(dt, 1e-3f);
        float rate = Math.Max(Math.Abs(a.Level - _lastLevel), Math.Abs(a.Bass - _lastBass)) / step;
        _lastLevel = a.Level;
        _lastBass = a.Bass;
        if (rate > 2.5f || kick) _scatter = Math.Max(_scatter, Math.Clamp(rate / 8f, .35f, 1f));
        _scatter *= MathF.Exp(-dt * 2.2f);

        _swell = hit ? Math.Max(_swell, kick ? 1f : .6f) : _swell * MathF.Exp(-dt * 6f);

        // with no music the curtain keeps going, slower and dimmer; the music brings it up over a second
        float present = a.Level > .02f ? 1f : 0f;
        _music += (present - _music) * (1f - MathF.Exp(-dt * (present > _music ? 2f : .5f)));
        _clock += dt * (.6f + (.4f * _music));
        _shotClock += dt;
    }

    // The prototype's shots: eye and target at the start and end of each, vertical fov, focus as a share of the
    // distance (x2), seed, how much of the curtain is lit, and the band the shot wants its light in.
    private readonly record struct Shot(float Dur, float[] E0, float[] E1, float[] C0, float[] C1, float Fov, float F, float Seed, float Fill, float[] Band);

    private static readonly Shot[] Shots =
    {
        new(7.5f, new[] { -1.55f, .1f, 1.9f }, new[] { -1.4f, -.05f, 1.8f }, new[] { .7f, 0f, -.1f }, new[] { .8f, .05f, -.1f }, 62, 2f, 0f, .55f, new[] { 0f, 0f, 1f, 0f }),
        new(6f, new[] { -.15f, .6f, .8f }, new[] { 0f, .52f, .72f }, new[] { -.15f, .6f, 0f }, new[] { 0f, .52f, 0f }, 55, 2f, 4.1f, -.15f, new[] { -.638f, .57f, .09f, 1.15f }),
        new(9.5f, new[] { 0f, 0f, .62f }, new[] { 0f, -.03f, .55f }, new[] { 0f, 0f, 0f }, new[] { 0f, -.03f, 0f }, 60, 1.9f, 11.7f, -.35f, new[] { 0f, -.02f, .13f, 1.05f }),
        new(6.5f, new[] { 1.55f, -.3f, 1.85f }, new[] { 1.4f, -.2f, 1.75f }, new[] { -.7f, -.1f, -.1f }, new[] { -.8f, -.05f, -.1f }, 62, 2f, 23.3f, .55f, new[] { 0f, 0f, 1f, 0f }),
    };

    // How long the move from one shot to the next takes, for the moves that blend (glide, flight, dive).
    private const float Link = 3f;
    private const float DiveTime = 2.4f;
    private const float FocusPull = 1.4f;

    private (float, float) _flightSeeds;

    // where the camera is and what it shows at one moment of a shot
    private readonly record struct Pose(float[] E, float[] C, float Fov, float F, float Fill, float[] Band);

    private static Pose PoseAt(Shot k, float ease) => new(Lerp(k.E0, k.E1, ease), Lerp(k.C0, k.C1, ease), k.Fov, k.F, k.Fill, k.Band);

    private static Pose BlendPose(Pose a, Pose b, float g)
    {
        // a shot without a band fades the other's in or out instead of swinging it across
        bool fromNone = a.Band[3] == 0, toNone = b.Band[3] == 0;
        var band = new float[4];
        for (int i = 0; i < 3; i++) band[i] = fromNone ? b.Band[i] : toNone ? a.Band[i] : Mix(a.Band[i], b.Band[i], g);
        band[3] = Mix(a.Band[3], b.Band[3], g);
        return new(Lerp(a.E, b.E, g), Lerp(a.C, b.C, g), Mix(a.Fov, b.Fov, g), Mix(a.F, b.F, g), Mix(a.Fill, b.Fill, g), band);
    }

    private static float Smooth(float x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - (2 * x));
    }

    private void FillCamera(GpuFrame frame)
    {
        float total = 0;
        foreach (var shot in Shots) total += shot.Dur;
        float m = ((_shotClock % total) + total) % total;
        int index = 0;
        while (index < Shots.Length - 1 && m >= Shots[index].Dur) m -= Shots[index++].Dur;
        var k = Shots[index];
        var next = Shots[(index + 1) % Shots.Length];
        var cur = PoseAt(k, Smooth(m / k.Dur));
        float g = 0, defocus = 0;
        float seedA = k.Seed, seedB = next.Seed;
        Pose pose;
        var motion = GlassSettings.StrandWallpaperMotion;
        int fixedShot = GlassSettings.StrandWallpaperAngle;
        if (fixedShot is >= 1 and <= 4)
        {
            // one chosen angle: the camera drifts back and forth through that shot and never leaves it
            k = Shots[fixedShot - 1];
            float sway = .5f - (.5f * MathF.Cos(MathF.PI * _shotClock / k.Dur));
            cur = PoseAt(k, sway);
            seedA = seedB = k.Seed;
            motion = (StrandMotion)(-1);
        }

        switch (motion)
        {
            case (StrandMotion)(-1):
                pose = cur;
                break;

            case StrandMotion.Focus:
                // a cut hidden in a focus pull: the curtain melts into bokeh, the shot changes while nothing is
                // sharp, and the new one comes into focus
                pose = cur;
                defocus = Smooth(Math.Max(1 - ((k.Dur - m) / FocusPull), 1 - (m / FocusPull)));
                break;

            case StrandMotion.Dive:
            {
                // the camera throws itself at the curtain, grazes the strands and comes out in the next shot
                g = Smooth((m - (k.Dur - DiveTime)) / DiveTime);
                var to = PoseAt(next, 0);
                pose = BlendPose(cur, to, g);
                var through = new[] { (cur.C[0] + to.C[0]) / 2, (cur.C[1] + to.C[1]) / 2, -.5f };
                float a = (1 - g) * (1 - g), b = 2 * (1 - g) * g, c = g * g;
                var eye = new[]
                {
                    (a * cur.E[0]) + (b * through[0]) + (c * to.E[0]),
                    (a * cur.E[1]) + (b * through[1]) + (c * to.E[1]),
                    (a * cur.E[2]) + (b * through[2]) + (c * to.E[2]),
                };
                // looking along a blend of the two shots' directions, so passing the target never flips the view
                var look = Lerp(Direction(cur.E, cur.C), Direction(to.E, to.C), g);
                pose = pose with { E = eye, C = new[] { eye[0] + look[0], eye[1] + look[1], eye[2] + look[2] } };
                defocus = MathF.Sin(MathF.PI * g) * .35f;
                break;
            }

            case StrandMotion.Flight:
            case StrandMotion.FocusFlight:
                pose = Flight(_shotClock, total, out g, out float bump);
                (seedA, seedB) = _flightSeeds;
                if (motion == StrandMotion.FocusFlight) defocus = bump;
                break;

            default:
                g = Smooth((m - (k.Dur - Link)) / Link);
                pose = BlendPose(cur, PoseAt(next, 0), g);
                break;
        }

        var e = pose.E;
        var ctr = pose.C;
        float aspect = frame.Width / (float)Math.Max(1, frame.Height);
        float fov = pose.Fov * MathF.PI / 180 * (aspect > 1 ? .78f : 1);
        var vp = Multiply(Perspective(fov, aspect, .05f, 30), LookAt(e, ctr));
        float dist = MathF.Sqrt(((e[0] - ctr[0]) * (e[0] - ctr[0])) + ((e[1] - ctr[1]) * (e[1] - ctr[1])) + ((e[2] - ctr[2]) * (e[2] - ctr[2])));
        // diving, the aim is one unit ahead; focus on the strands rushing past instead
        if (motion == StrandMotion.Dive) dist = Mix(dist * (pose.F / 2f), .6f, MathF.Sin(MathF.PI * g)) / (pose.F / 2f);

        var d = _data;
        // the column-major GL matrix as four rows, so the shader can dot them
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 4; col++) d[40 + (row * 4) + col] = vp[(col * 4) + row];
        }

        d[56] = dist * (pose.F / 2f);
        d[57] = frame.Height / 1300f;
        d[58] = seedA;
        d[59] = pose.Fill;
        for (int i = 0; i < 4; i++) d[60 + i] = pose.Band[i];
        d[64] = 1;
        d[65] = _music;
        d[66] = _clock;
        d[67] = g;
        d[68] = seedB;
        d[69] = defocus;
        d[70] = 0;
        d[71] = 0;
    }

    /// <summary>
    /// One unbroken path through every shot: a closed Catmull-Rom curve through each shot's start and end, the
    /// camera never stopping. Between shots the lens and light blend as in the glide.
    /// </summary>
    private Pose Flight(float clock, float total, out float g, out float bump)
    {
        int n = Shots.Length;
        var eyes = new float[n * 2][];
        var aims = new float[n * 2][];
        var times = new float[n * 2];
        for (int s = 0; s < n; s++)
        {
            eyes[2 * s] = Shots[s].E0;
            eyes[(2 * s) + 1] = Shots[s].E1;
            aims[2 * s] = Shots[s].C0;
            aims[(2 * s) + 1] = Shots[s].C1;
            times[2 * s] = Shots[s].Dur - Link;
            times[(2 * s) + 1] = Link;
        }

        float m = ((clock % total) + total) % total;
        int seg = 0;
        while (seg < times.Length - 1 && m >= times[seg]) m -= times[seg++];
        float f = Math.Clamp(m / times[seg], 0, 1);
        int count = eyes.Length;
        float[] At(float[][] p, int i) => p[((i % count) + count) % count];
        var eye = CatmullRom(At(eyes, seg - 1), At(eyes, seg), At(eyes, seg + 1), At(eyes, seg + 2), f);
        var aim = CatmullRom(At(aims, seg - 1), At(aims, seg), At(aims, seg + 1), At(aims, seg + 2), f);

        int shot = seg / 2;
        var k = Shots[shot];
        var next = Shots[(shot + 1) % n];
        bool linking = seg % 2 == 1;
        g = linking ? Smooth(f) : 0;
        bump = linking ? MathF.Pow(MathF.Sin(MathF.PI * f), 2) : 0;
        _flightSeeds = (k.Seed, next.Seed);
        var lens = BlendPose(PoseAt(k, 1), PoseAt(next, 0), g);
        return lens with { E = eye, C = aim };
    }

    private static float[] CatmullRom(float[] p0, float[] p1, float[] p2, float[] p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        var o = new float[3];
        for (int i = 0; i < 3; i++)
        {
            o[i] = .5f * ((2 * p1[i]) + ((-p0[i] + p2[i]) * t) + (((2 * p0[i]) - (5 * p1[i]) + (4 * p2[i]) - p3[i]) * t2)
                + ((-p0[i] + (3 * p1[i]) - (3 * p2[i]) + p3[i]) * t3));
        }

        return o;
    }

    private static float[] Direction(float[] from, float[] to)
    {
        float x = to[0] - from[0], y = to[1] - from[1], z = to[2] - from[2];
        float l = MathF.Max(1e-4f, MathF.Sqrt((x * x) + (y * y) + (z * z)));
        return new[] { x / l, y / l, z / l };
    }

    private static float Mix(float a, float b, float t) => a + ((b - a) * t);

    private static float[] Lerp(float[] a, float[] b, float t) => new[] { a[0] + ((b[0] - a[0]) * t), a[1] + ((b[1] - a[1]) * t), a[2] + ((b[2] - a[2]) * t) };

    private static float[] Perspective(float fov, float aspect, float near, float far)
    {
        float t = 1 / MathF.Tan(fov / 2);
        return new[] { t / aspect, 0, 0, 0, 0, t, 0, 0, 0, 0, (far + near) / (near - far), -1, 0, 0, 2 * far * near / (near - far), 0 };
    }

    private static float[] LookAt(float[] eye, float[] centre)
    {
        static float[] Norm(float[] a) { float l = MathF.Sqrt((a[0] * a[0]) + (a[1] * a[1]) + (a[2] * a[2])); return new[] { a[0] / l, a[1] / l, a[2] / l }; }
        static float[] Cross(float[] a, float[] b) => new[] { (a[1] * b[2]) - (a[2] * b[1]), (a[2] * b[0]) - (a[0] * b[2]), (a[0] * b[1]) - (a[1] * b[0]) };
        static float Dot(float[] a, float[] b) => (a[0] * b[0]) + (a[1] * b[1]) + (a[2] * b[2]);
        var z = Norm(new[] { eye[0] - centre[0], eye[1] - centre[1], eye[2] - centre[2] });
        var x = Norm(Cross(new[] { 0f, 1f, 0f }, z));
        var y = Cross(z, x);
        return new[] { x[0], y[0], z[0], 0, x[1], y[1], z[1], 0, x[2], y[2], z[2], 0, -Dot(x, eye), -Dot(y, eye), -Dot(z, eye), 1 };
    }

    private static float[] Multiply(float[] a, float[] b)
    {
        var o = new float[16];
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                for (int k = 0; k < 4; k++) o[(j * 4) + i] += a[(k * 4) + i] * b[(j * 4) + k];
            }
        }

        return o;
    }

    private void Fill(GpuFrame frame)
    {
        var d = _data;
        var a = frame.Audio;
        d[0] = frame.Width;
        d[1] = frame.Height;
        d[2] = Math.Max(.5f, frame.PixelsPerDip);
        d[3] = Math.Clamp(frame.Fade, 0, 1);
        d[4] = frame.Time;
        d[5] = Math.Clamp(a.Level, 0, 1);
        d[6] = Math.Clamp(a.Bass, 0, 1);
        d[7] = _swell;
        d[8] = Math.Clamp(Math.Max(a.Treble, a.Hat), 0, 1);
        d[9] = frame.DockLeft;
        d[10] = frame.DockRight;
        d[11] = Exposure;
        d[12] = _scatter;
        d[13] = _swell;
        d[14] = _flow;
        d[15] = 0;
        var bands = a.Bands;
        for (int i = 0; i < 24; i++) d[16 + i] = bands.Length == 0 ? 0 : Math.Clamp(bands[i * bands.Length / 24], 0, 1);
        if (_desktop) FillCamera(frame);
    }

    public void ReleaseDevice()
    {
        ReleaseSized();
        _dotVs?.Dispose(); _dotVs = null;
        _dotPs?.Dispose(); _dotPs = null;
        _fullVs?.Dispose(); _fullVs = null;
        _downPs?.Dispose(); _downPs = null;
        _blurPs?.Dispose(); _blurPs = null;
        _finalPs?.Dispose(); _finalPs = null;
        _constants?.Dispose(); _constants = null;
        _passConstants?.Dispose(); _passConstants = null;
        _cb[0] = null!;
        _cb[1] = null!;
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
        _hdr?.Dispose(); _hdr = null;
        _q1?.Dispose(); _q1 = null;
        _q2?.Dispose(); _q2 = null;
        _e1?.Dispose(); _e1 = null;
        _e2?.Dispose(); _e2 = null;
        _width = _height = 0;
    }

    public void Dispose() => ReleaseDevice();

    private static ReadOnlyMemory<byte> Compile(string profile, string source) =>
        Compiler.Compile(source, "main", "StrandCurtain", profile, ShaderFlags.OptimizationLevel3, EffectFlags.None);

    private sealed class Target : IDisposable
    {
        public Target(ID3D11Device device, int width, int height)
        {
            Width = width;
            Height = height;
            Texture = device.CreateTexture2D(new Texture2DDescription(Format.R16G16B16A16_Float, (uint)width, (uint)height, 1, 1,
                BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceUsage.Default));
            Rtv = device.CreateRenderTargetView(Texture, null);
            Srv = device.CreateShaderResourceView(Texture, null);
        }

        public int Width { get; }
        public int Height { get; }
        public ID3D11Texture2D Texture { get; }
        public ID3D11RenderTargetView Rtv { get; }
        public ID3D11ShaderResourceView Srv { get; }

        public void Dispose()
        {
            Srv.Dispose();
            Rtv.Dispose();
            Texture.Dispose();
        }
    }

    private const string Shared = """
        cbuffer Strand : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float Time;
            float Level;
            float Bass;
            float Pulse;
            float Spark;
            float DockLeft;
            float DockRight;
            float Expo;
            float Scatter;
            float Swell;
            float Flow;
            float Pad;
            float4 Bands[6];
            float4 VP0;
            float4 VP1;
            float4 VP2;
            float4 VP3;
            float Focus;
            float Px;
            float Seed;
            float Fill;
            float4 ShotBand;
            float Desktop;
            float Music;
            float ShotTime;
            float Glide;
            float NextSeed;
            float Defocus;
            float2 Pad5;
        };

        struct DotOut
        {
            float4 Pos : SV_Position;
            float2 Uv : TEXCOORD0;
            float3 Color : COLOR0;
            float Sharp : TEXCOORD1;
        };

        """;

    private const string Noise = """
        float Hash(float2 p)
        {
            float3 q = frac(p.xyx * .1031);
            q += dot(q, q.yzx + 33.33);
            return frac((q.x + q.y) * q.z);
        }

        float3 Mod289(float3 x) { return x - floor(x * (1. / 289.)) * 289.; }
        float4 Mod289(float4 x) { return x - floor(x * (1. / 289.)) * 289.; }
        float4 Perm(float4 x) { return Mod289(((x * 34.) + 1.) * x); }
        float4 Tis(float4 r) { return 1.79284291400159 - .85373472095314 * r; }

        // 3D simplex noise, about -1..1
        float Snoise(float3 v)
        {
            const float2 C = float2(1. / 6., 1. / 3.);
            const float4 D = float4(0., .5, 1., 2.);
            float3 i = floor(v + dot(v, C.yyy));
            float3 x0 = v - i + dot(i, C.xxx);
            float3 g = step(x0.yzx, x0.xyz);
            float3 l = 1. - g;
            float3 i1 = min(g.xyz, l.zxy);
            float3 i2 = max(g.xyz, l.zxy);
            float3 x1 = x0 - i1 + C.xxx;
            float3 x2 = x0 - i2 + C.yyy;
            float3 x3 = x0 - D.yyy;
            i = Mod289(i);
            float4 p = Perm(Perm(Perm(i.z + float4(0., i1.z, i2.z, 1.)) + i.y + float4(0., i1.y, i2.y, 1.)) + i.x + float4(0., i1.x, i2.x, 1.));
            float n_ = .142857142857;
            float3 ns = n_ * D.wyz - D.xzx;
            float4 j = p - 49. * floor(p * ns.z * ns.z);
            float4 x_ = floor(j * ns.z);
            float4 y_ = floor(j - 7. * x_);
            float4 x = x_ * ns.x + ns.yyyy;
            float4 y = y_ * ns.x + ns.yyyy;
            float4 h = 1. - abs(x) - abs(y);
            float4 b0 = float4(x.xy, y.xy);
            float4 b1 = float4(x.zw, y.zw);
            float4 s0 = floor(b0) * 2. + 1.;
            float4 s1 = floor(b1) * 2. + 1.;
            float4 sh = -step(h, 0.);
            float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
            float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;
            float3 p0 = float3(a0.xy, h.x);
            float3 p1 = float3(a0.zw, h.y);
            float3 p2 = float3(a1.xy, h.z);
            float3 p3 = float3(a1.zw, h.w);
            float4 nm = Tis(float4(dot(p0, p0), dot(p1, p1), dot(p2, p2), dot(p3, p3)));
            p0 *= nm.x; p1 *= nm.y; p2 *= nm.z; p3 *= nm.w;
            float4 m = max(.6 - float4(dot(x0, x0), dot(x1, x1), dot(x2, x2), dot(x3, x3)), 0.);
            m = m * m;
            return 42. * dot(m * m, float4(dot(p0, x0), dot(p1, x1), dot(p2, x2), dot(p3, x3)));
        }

        float Band(float u)
        {
            float f = saturate(u) * 22.99;
            int i = (int)f;
            int j = min(i + 1, 23);
            return lerp(Bands[i / 4][i % 4], Bands[j / 4][j % 4], f - i);
        }

        """;

    private const string DotVs = """
        // the three curtains, front to back
        static const uint LNS[3] = { 150, 110, 80 };
        static const uint LNK[3] = { 3, 2, 1 };
        static const uint LND[3] = { 72, 54, 36 };
        static const float LDim[3] = { 1., .5, .3 };
        static const float LBlur[3] = { 0., 4.5, 10. };
        static const float LSeed[3] = { 0., 17.3, 41.9 };
        static const uint Front = 150 * 3 * 72;
        static const uint Middle = 110 * 2 * 54;
        static const uint Total = Front + Middle + 80 * 36;

        static const float2 Corners[6] =
        {
            float2(-1, -1), float2(1, -1), float2(-1, 1),
            float2(-1, 1), float2(1, -1), float2(1, 1),
        };

        // DIPs per unit of the prototype's world: one strand every ~15 DIP, as in its close shots
        static const float World = 660.;

        DotOut main(uint vid : SV_VertexID, uint iid : SV_InstanceID)
        {
            DotOut o;
            bool ghost = iid >= Total;
            uint id = ghost ? iid - Total : iid;
            uint L = 0;
            if (id >= Front) { id -= Front; L = 1; if (id >= Middle) { id -= Middle; L = 2; } }
            uint ND = LND[L], NK = LNK[L], NS = LNS[L];
            uint d = id % ND;
            uint k = (id / ND) % NK;
            uint s = id / (ND * NK);

            float jit = (Hash(float2(s, L * 7 + 1)) - .5) * .25;
            float u = (s + .5 + jit) / NS;
            float v = ((d + .5 + (k % 2) * .5) / ND) * 1.3 - .15;      // 0 top .. 1 bottom, a little past both
            float kz = NK > 1 ? (float)k / (NK - 1) * 2. - 1. : 0.;

            float x = u * Size.x / Dip / World;
            float y = (1. - v) * Size.y / Dip / World;
            float t = Time;

            // the light: slow drifting blobs, pushed up where the band under the strand is loud
            float3 q = float3(x * .55 + LSeed[L], y * .34 - t * .045, t * .07);
            float n = Snoise(q) * .62 + Snoise(q * 2.1 + 7.3) * .26 + Snoise(q * 6.3 + 2.1) * .12;
            float e = Band(abs(u - .5) * 2.);
            n = n * .7 + (e - .28) * 1.5 + (Level - .4) * .25 + Swell * .22;
            float I = smoothstep(-.05, .55, n);
            float I2 = I * I;

            // where it is lit the strand swells sideways and ripples in small, dense waves
            float rip = sin(y * 42. - Flow * 2.4 + x * 11.) * .4 + Snoise(float3(x * 9., y * 16., t * .6)) * .6;
            float sway = Snoise(float3(x * 2.4, y * 4.5, t * .25 + 3.));

            // each line of the bundle has its own wave, out of step with the others; only some strands come
            // apart, which ones changes slowly, and a sudden change in the music pulls more of them apart
            float ks = Hash(float2(s * 1.37 + k * 7.1, L + 3.));
            float own = Snoise(float3(x * 9. + ks * 40., y * (7. + 7. * ks), t * (.45 + .5 * ks)));
            float ownRip = sin(y * (30. + 25. * ks) - t * (1.3 + 1.6 * ks) + ks * 6.283);
            float pick = smoothstep(.2 - .35 * Scatter, .5 - .2 * Scatter, Snoise(float3((u * 2. - 1.) * 6.3, t * .11, 5. + L)));
            float spread = I * (.5 + .5 * I) * pick * (1. + .8 * Scatter);
            float amp = 1. + Swell * 1.2 + Level * .3;
            float dx = kz * .0012 + I * (.0075 * rip + .016 * sway) * amp + spread * (.0075 * own + .004 * ownRip);
            float z = I * .10 * sway + spread * .03 * own + .05 * Snoise(float3(x, y * .5, t * .1 + 9.));

            float px = u * Size.x + dx * World * Dip;
            float py = v * Size.y;

            // depth of field: the dot grows as it leaves the focus, and keeps its energy while it does
            float coc = (abs(z) * 23. + LBlur[L]) * Dip;
            float dot0 = 1.6 * Dip;
            float size = dot0 + coc;

            float3 base = float3(.9, 0., .07) * .045;
            float3 hot = lerp(float3(1., 0., .07), float3(1., .04, .30), smoothstep(.45, .9, I));
            float3 col = (base + hot * (I2 * 1.5 + I * .3)) * (2. / 3.) * LDim[L];
            if (ghost)
            {
                // the blue ghost: a little off to one side and lower, spread wider, mostly on the lit region's edge
                px += (2.6 + 4.9 * I) * Dip;
                py += (1.15 + 2.2 * I) * Dip;
                size *= 1.35;
                float rim = smoothstep(.04, .28, I) * (1. - smoothstep(.38, .7, I));
                col = float3(.07, .04, 1.) * (rim * .06 + I2 * .012 + .0002) * LDim[L];
            }
            size = clamp(size, 1., 90.);
            float energy = dot0 * dot0 / (size * size);

            float2 c = Corners[min(vid, 5u)];
            float r = size * .5;
            float2 p = float2(px, py) + c * r;
            if (px < -r || px > Size.x + r) p = float2(-1e4, -1e4);
            o.Pos = float4((p.x / Size.x) * 2. - 1., 1. - (p.y / Size.y) * 2., 0., 1.);
            o.Uv = c;
            o.Color = col * energy;
            o.Sharp = saturate(1. - coc / (dot0 * 2.));
            return o;
        }
        """;

    // The prototype's vertex shader, line for line, with the music laid on: the band under a strand pushes its
    // light, a kick swells it, a sudden change pulls more strands apart, and the ripples run with the loudness.
    private const string DesktopVs = """
        static const uint NS = 150, NK = 3, ND = 1600;
        static const uint Total = NS * NK * ND;

        static const float2 Corners[6] =
        {
            float2(-1, -1), float2(1, -1), float2(-1, 1),
            float2(-1, 1), float2(1, -1), float2(1, 1),
        };

        DotOut main(uint vid : SV_VertexID, uint iid : SV_InstanceID)
        {
            DotOut o;
            bool ghost = iid >= Total;
            uint id = ghost ? iid - Total : iid;
            uint j = id % ND;
            uint m = (id / ND) % NK;
            uint i = id / (ND * NK);

            float jx = (Hash(float2(i, 3.7)) - .5) * .25 / NS;
            float ax = (i + .5) / NS * 2. - 1. + jx;
            float ay = (j + .5 + (m % 2) * .5) / ND * 2. - 1.;
            float az = (float)m / (NK - 1) * 2. - 1.;

            float x = ax * 1.7, y = ay * 1.6;
            float t = ShotTime;
            float3 q = float3(x * .55 + Seed, y * .34 - t * .045, t * .07);
            float n = Snoise(q) * .62 + Snoise(q * 2.1 + 7.3) * .26 + Snoise(q * 6.3 + 2.1) * .12;
            if (Glide > 0.)
            {
                // between shots the light melts from this shot's pattern into the next one's
                float3 q2 = float3(x * .55 + NextSeed, q.yz);
                float n2 = Snoise(q2) * .62 + Snoise(q2 * 2.1 + 7.3) * .26 + Snoise(q2 * 6.3 + 2.1) * .12;
                // two unrelated patterns averaged go flat; scaled back so the curtain stays as lit mid-glide
                n = lerp(n, n2, Glide) / sqrt((1. - Glide) * (1. - Glide) + Glide * Glide);
            }
            float2 bn = float2(-sin(ShotBand.x), cos(ShotBand.x));
            float bd = (dot(float2(x, y), bn) - ShotBand.y + .25 * ShotBand.z * sin(t * .3 + x * 9.)) / ShotBand.z;
            n += ShotBand.w * (exp(-bd * bd) - .35);
            float e = Band(abs(x) / 1.7);
            n += Music * ((e - .3) * .9 + Swell * .22);
            float I = smoothstep(-Fill * .5, .55 - Fill * .3, n);
            float I2 = I * I;

            float rip = sin(y * 42. - Flow * 2.4 + x * 11.) * .4 + Snoise(float3(x * 9., y * 16., t * .6)) * .6;
            float sway = Snoise(float3(x * 2.4, y * 4.5, t * .25 + 3.));
            float ks = frac(sin(ax * 91.7 + az * 37.3) * 43758.5453);
            float own = Snoise(float3(x * 9. + ks * 40., y * (7. + 7. * ks), t * (.45 + .5 * ks)));
            float ownRip = sin(y * (30. + 25. * ks) - t * (1.3 + 1.6 * ks) + ks * 6.283);
            float pick = smoothstep(.2 - .35 * Scatter, .5 - .2 * Scatter, Snoise(float3(ax * 6.3, t * .11, 5.)));
            float spread = I * (.5 + .5 * I) * pick * (1. + .8 * Scatter);
            float amp = 1. + Swell * 1.2 * Music;
            float4 P = float4(x + az * .0012 + I * (.0075 * rip + .016 * sway) * amp + spread * (.0075 * own + .004 * ownRip), y,
                              I * .10 * sway + spread * .03 * own + .05 * Snoise(float3(x, y * .5, t * .1 + 9.)), 1.);

            float4 c = float4(dot(VP0, P), dot(VP1, P), dot(VP2, P), dot(VP3, P));
            float d = c.w;
            float coc = abs(d - Focus) / max(d, .01) * 38. * Px + Defocus * 46. * Px;
            float dot0 = 2.6 * Px / max(d, .01);
            float sz = dot0 + coc;

            float bright = .55 + .45 * Music;
            float3 base = float3(.9, 0., .07) * .045;
            float3 hot = lerp(float3(1., 0., .07), float3(1., .04, .30), smoothstep(.45, .9, I));
            float3 col = (base + hot * (I2 * 1.5 + I * .12)) * (2. / NK) * bright;
            if (ghost)
            {
                float side = .016 + .03 * I;
                c.xy += float2(side, -side * .3) * c.w;
                sz *= 1.35;
                float rim = smoothstep(.04, .28, I) * (1. - smoothstep(.38, .7, I));
                col = float3(.07, .04, 1.) * (rim * .14 + I2 * .025 + .0008) * bright;
            }
            sz = clamp(sz, 1., 90.);
            // out of focus the light spreads into bokeh; a little extra keeps the pull from going dark
            float energy = dot0 * dot0 / (sz * sz) * (1. + Defocus);

            float2 corner = Corners[min(vid, 5u)];
            c.xy += corner * sz / Size * c.w;
            c.z = .5 * c.w;
            if (d < .05) c = float4(0., 0., -1., 1.);
            o.Pos = c;
            o.Uv = corner;
            o.Color = col * energy;
            o.Sharp = saturate(1. - coc / (dot0 * 2.));
            return o;
        }
        """;

    private const string DotPs = """
        float4 main(DotOut i) : SV_Target
        {
            float2 d = i.Uv;
            d.y *= lerp(1., 2.6, i.Sharp);      // in focus a dot is a short horizontal tick
            float r2 = dot(d, d);
            if (r2 > 1.) discard;
            float soft = exp(-r2 * 3.);
            float hard = 1. - smoothstep(.45, .8, r2);
            float a = lerp(soft, hard, i.Sharp);
            return float4(i.Color * a, 0.);
        }
        """;

    private const string FullVs = """
        float4 main(uint id : SV_VertexID) : SV_Position
        {
            float2 uv = float2((id << 1) & 2, id & 2);
            return float4((uv.x * 2) - 1, 1 - (uv.y * 2), 0, 1);
        }
        """;

    private const string PassHeader = """
        cbuffer PassInfo : register(b1)
        {
            float2 Dir;
            float2 Pad2;
            float2 Dst;
            float2 Pad3;
        };
        Texture2D Src : register(t0);
        SamplerState Linear : register(s0);

        """;

    // a 4x smaller copy: four bilinear taps cover the 4x4 source pixels under each new one
    private const string DownPs = """
        float4 main(float4 pos : SV_Position) : SV_Target
        {
            float2 uv = pos.xy / Dst;
            float2 o = .25 / Dst;
            float3 c = Src.Sample(Linear, uv + float2(-o.x, -o.y)).rgb + Src.Sample(Linear, uv + float2(o.x, -o.y)).rgb
                     + Src.Sample(Linear, uv + float2(-o.x, o.y)).rgb + Src.Sample(Linear, uv + float2(o.x, o.y)).rgb;
            return float4(c * .25, 1.);
        }
        """;

    private const string BlurPs = """
        float4 main(float4 pos : SV_Position) : SV_Target
        {
            float2 uv = pos.xy / Dst;
            float3 c = 0.;
            float w = 0.;
            [unroll] for (int i = -8; i <= 8; i++)
            {
                float k = exp(-(float)(i * i) / 22.);
                c += Src.Sample(Linear, uv + Dir * i).rgb * k;
                w += k;
            }
            return float4(c / w, 1.);
        }
        """;

    private const string FinalPs = """
        Texture2D Hdr : register(t0);
        Texture2D Quarter : register(t1);
        Texture2D Sixteenth : register(t2);
        SamplerState Linear : register(s0);

        float4 main(float4 pos : SV_Position) : SV_Target
        {
            float2 uv = pos.xy / Size;
            float3 c = Hdr.Load(int3((int2)pos.xy, 0)).rgb + Quarter.Sample(Linear, uv).rgb * .55 + Sixteenth.Sample(Linear, uv).rgb * .35;
            c *= Expo;
            float3 m = 1. - exp(-c);
            // where red is far past full it bleeds toward pale pink, like an overexposed sensor
            float over = max(c.r - 1.4, 0.);
            m = lerp(m, float3(1., .82, .96), 1. - exp(-over * .35));
            m = pow(max(m, 0.), .95);

            if (Desktop > .5) return float4(m * Fade, 1.);

            // the dock's own span stays dark, with a soft edge, and the top melts into the desktop
            float ramp = 40. * Dip / Size.x;
            float gap = smoothstep(DockLeft - ramp, DockLeft + ramp * .3, uv.x) * (1. - smoothstep(DockRight - ramp * .3, DockRight + ramp, uv.x));
            m *= (1. - .9 * gap) * smoothstep(0., .3, uv.y) * Fade;
            float a = max(m.r, max(m.g, m.b));
            return float4(m, a);
        }
        """;
}
