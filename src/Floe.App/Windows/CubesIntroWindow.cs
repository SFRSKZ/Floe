using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floe.App.Services;
using Floe.Core.Interop;

namespace Floe.App.Windows;

/// <summary>
/// Click-through overlay above the dock that plays the "now playing" cubes intro. The dock opens its own gap for
/// the section as always; clear glass cubes settle into that gap from the middle out, laying the cover strip and
/// the previous / pause / next buttons where the real ones sit, then hand over to the real section. When the
/// music stops the section turns back into cubes that drift off while the two halves of the dock close up.
/// With <c>dust</c> the same choreography plays with glowing grains of star dust that whirl above the slot.
/// </summary>
internal sealed class CubesIntroWindow : Window
{
    /// <summary>Overlay size and where the now-playing slot's centre sits inside it (DIP).</summary>
    public const double OverlayWidth = 760;
    public const double OverlayHeight = 380;
    public const double SlotY = 290;

    private readonly CubesIntroVisual _visual = new();
    private readonly bool _excludeFromCapture;
    private IntPtr _hwnd;
    private bool _followSlot;

    public CubesIntroWindow(bool excludeFromCapture)
    {
        _excludeFromCapture = excludeFromCapture;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        Title = "Floe Intro";
        Width = OverlayWidth;
        Height = OverlayHeight;
        Content = _visual;
    }

    public static double ImpactTime => CubesIntroVisual.ImpactTime;

    /// <summary>How much of the real now-playing section should show: 0 while the cubes stand in for it.</summary>
    public double ContentVeil => IsVisible ? _visual.ContentVeil : 1;

    protected override void OnClosed(EventArgs e)
    {
        CaptureReveal.Forget(_hwnd);
        base.OnClosed(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        WindowStyling.MakeClickThrough(_hwnd);
        if (_excludeFromCapture)
        {
            CaptureReveal.Exclude(_hwnd);
        }
    }

    /// <summary>
    /// Starts filling the slot centred on <paramref name="slot"/> (screen DIP); <paramref name="scale"/> is how large the
    /// dock draws its sections on screen, below 1 while it is shrunk.
    /// </summary>
    public void Start(Point slot, ImageSource? art, bool playing, double scale, bool dust)
    {
        Place(slot);
        _followSlot = true;
        _visual.Scale = scale;
        _visual.Dust = dust;
        _visual.Reset(OverlayWidth / 2, art, playing);
        Present();
    }

    /// <summary>Breaks the section apart into cubes that scatter away from <paramref name="slot"/>.</summary>
    public void StartOutro(Point slot, ImageSource? art, bool playing, double scale, bool dust)
    {
        if (!IsVisible || !_visual.Running)
        {
            Place(slot);
            _visual.Scale = scale;
            _visual.Dust = dust;
            _visual.Prepare(OverlayWidth / 2, art, playing);
        }

        // flying cubes keep their screen positions, so the overlay stops tracking the dock
        _followSlot = false;
        _visual.StartOutro();
        Present();
    }

    /// <summary>Advances the effect; <paramref name="slotX"/> follows the dock as it widens. False once finished.</summary>
    public bool Advance(double dt, double slotX, double scale)
    {
        if (_followSlot)
        {
            _visual.Scale = scale;
        }

        var alive = _visual.Advance(dt, _followSlot ? slotX - Left : double.NaN);
        if (!alive)
        {
            Hide();
        }

        return alive;
    }

    private void Place(Point slot)
    {
        Left = slot.X - (OverlayWidth / 2);
        Top = slot.Y - SlotY;
    }

    private void Present()
    {
        if (!IsVisible)
        {
            Show();
        }

        WindowStyling.BringToTop(_hwnd);
    }
}

/// <summary>The glass cubes that fill the opening now-playing slot, drawn in the overlay's coordinates.</summary>
internal sealed class CubesIntroVisual : FrameworkElement
{
    // Timeline (seconds). A wave from left to right brings the cubes into a loose cloud above the opening gap,
    // mostly facing forward (or, as dust, into a slow whirl); the cover paints across them, then they fly down
    // into the slot from the middle out, the buttons last, and the real section takes over.
    public const double ImpactTime = 0;
    private const double AppearWave = 0.55;
    private const double PaintStart = 0.45;
    private const double GatherStart = 1.0;
    private const double LayFlight = 0.7;
    // The hand-over is slow and staggered: the cubes take on the cover's real colours, the section fades in
    // under them, and they melt away from the middle out.
    private const double HandoffStart = 2.1;
    private const double CubeFade = 0.5;
    private const double HandoffEnd = HandoffStart + 0.15 + 0.35 + CubeFade;
    internal const double EndTime = HandoffEnd + 0.1;
    // Stopping sends the same kind of wave across: each one holds until it arrives, then drifts off.
    private const double OutroWave = 0.6;
    private const double OutroLife = 1.0;
    internal const double OutroSeconds = OutroWave + 0.1 + OutroLife;

    // The now-playing slot, matching DockWindow's section: the cover strip bleeds to the dividers
    // and nearly the bar's height, the buttons sit in the last 90 DIP.
    private const double MediaWidth = 250;
    private const double StripWidth = 260;
    private const double StripHeight = 56;
    private const int StripRows = 7;
    private const int StripCols = 36;
    private const int GlyphCells = 12;
    private const int DustCols = 60;
    private const int DustRows = 12;
    private const int MaxCubes = 900;

    // Settled orientation: a touch from above so every cube shows its depth.
    private const double SlotPitch = 0.16;

    // Below this edge a cube is too small to read as glass and is drawn as a solid dot of light.
    private const double TinyCube = 3.2;

    // Clear glass: the faces carry only a breath of the art's colour, the rest is what lies behind.
    private static readonly Color ClearGlass = Color.FromRgb(0xF4, 0xF7, 0xFA);
    private static readonly Color GlyphGlass = Color.FromRgb(0xFF, 0xFF, 0xFF);
    // Dust before the cover colours it: the pale star dust of the visualizer.
    private static readonly Color DustLight = Color.FromRgb(0xE4, 0xEE, 0xFF);

    private static readonly double[,] Corners =
    {
        { -.5, -.5, -.5 }, { .5, -.5, -.5 }, { .5, .5, -.5 }, { -.5, .5, -.5 },
        { -.5, -.5, .5 }, { .5, -.5, .5 }, { .5, .5, .5 }, { -.5, .5, .5 },
    };

    // z points away from the viewer, y down: face 0 is the front.
    private static readonly int[][] Faces =
    {
        new[] { 0, 1, 2, 3 }, new[] { 5, 4, 7, 6 }, new[] { 1, 5, 6, 2 },
        new[] { 4, 0, 3, 7 }, new[] { 3, 2, 6, 7 }, new[] { 4, 5, 1, 0 },
    };

    private static readonly double[,] Normals =
    {
        { 0, 0, -1 }, { 0, 0, 1 }, { 1, 0, 0 }, { -1, 0, 0 }, { 0, 1, 0 }, { 0, -1, 0 },
    };

    // towards the light: up, left and in front
    private static readonly double[] Light = Normalize(-0.35, -0.75, -0.55);

    // Thin, quiet edges that just mark where one block of glass ends and the next begins.
    private static readonly Pen[] EdgePens = BuildPens(0x58, 0.5);

    private enum Mode { Idle, Intro, Outro }

    // Per cube, relative to the slot centre.
    private readonly double[] _targetX = new double[MaxCubes];
    private readonly double[] _targetY = new double[MaxCubes];
    private readonly double[] _targetSize = new double[MaxCubes];
    private readonly double[] _delay = new double[MaxCubes];
    private readonly double[] _dropX = new double[MaxCubes];
    private readonly double[] _dropY = new double[MaxCubes];
    private readonly double[] _tilt = new double[MaxCubes];
    private readonly double[] _yaw = new double[MaxCubes];
    private readonly double[] _roll = new double[MaxCubes];
    private readonly double[] _appear = new double[MaxCubes];
    private readonly double[] _paint = new double[MaxCubes];
    // the dust whirl: each grain's orbit, phase, angular speed and depth (0 far and crisp, 1 near and soft)
    private readonly double[] _orbitR = new double[MaxCubes];
    private readonly double[] _orbitA = new double[MaxCubes];
    private readonly double[] _spin = new double[MaxCubes];
    private readonly double[] _depth = new double[MaxCubes];
    private readonly Dictionary<int, Brush> _grains = new();
    private readonly bool[] _glyph = new bool[MaxCubes];
    private readonly Color[] _color = new Color[MaxCubes];
    private readonly Color[] _cover = new Color[MaxCubes];
    private readonly double[] _reach = new double[MaxCubes];

    // What was last drawn, so the outro can take over from wherever the cubes are.
    private readonly double[] _curX = new double[MaxCubes];
    private readonly double[] _curY = new double[MaxCubes];
    private readonly double[] _curSize = new double[MaxCubes];
    private readonly double[] _curPitch = new double[MaxCubes];
    private readonly double[] _curAlpha = new double[MaxCubes];
    private bool _drawn;

    private readonly double[] _velX = new double[MaxCubes];
    private readonly double[] _velY = new double[MaxCubes];

    // scratch for one cube's projected corners
    private readonly Point[] _projected = new Point[8];

    private int _count;
    private Mode _mode = Mode.Idle;
    private double _time = -1;
    private double _x;
    private ImageSource? _art;
    private bool _playing;
    private bool _laidOut;
    private bool _laidDust;

    internal CubesIntroVisual()
    {
        IsHitTestVisible = false;
    }

    public bool Running => _mode != Mode.Idle;

    /// <summary>Glowing grains of dust instead of glass cubes; takes effect at the next <see cref="Prepare"/>.</summary>
    internal bool Dust { get; set; }

    /// <summary>0 while the cubes stand in for the now-playing section, 1 once it is handed over.</summary>
    public double ContentVeil => _mode switch
    {
        Mode.Intro => SmoothStep(HandoffStart, HandoffStart + 0.8, _time),
        Mode.Outro => 0,
        _ => 1,
    };

    internal void Reset(double x, ImageSource? art, bool playing)
    {
        Prepare(x, art, playing);
        _mode = Mode.Intro;
        _time = 0;
        _drawn = false;
        InvalidateVisual();
    }

    /// <summary>Lays the cubes out for <paramref name="art"/> without starting anything.</summary>
    internal void Prepare(double x, ImageSource? art, bool playing)
    {
        _x = x;
        if (!_laidOut || !ReferenceEquals(_art, art) || _playing != playing || _laidDust != Dust)
        {
            _laidDust = Dust;
            _art = art;
            _playing = playing;
            Layout();
            _laidOut = true;
        }
    }

    internal void StartOutro()
    {
        bool fromDrawn = _mode == Mode.Intro && _drawn;
        for (int i = 0; i < _count; i++)
        {
            if (!fromDrawn)
            {
                _curX[i] = _x + _targetX[i];
                _curY[i] = CubesIntroWindow.SlotY + _targetY[i];
                _curSize[i] = _targetSize[i];
                _curPitch[i] = SlotPitch;
                _curAlpha[i] = 1;
            }

            // the closing halves push the cubes up and out of the gap: a gentle lift, a little spread
            double side = (_curX[i] - _x) / (StripWidth / 2);
            _velX[i] = (side * 26) + ((Hash01(i, 0x13F7) - 0.5) * 30);
            _velY[i] = -(30 + (55 * Hash01(i, 0x4E29)));
        }

        _mode = Mode.Outro;
        _time = 0;
        InvalidateVisual();
    }

    /// <summary>Advances the clock; a NaN <paramref name="x"/> keeps the slot where it was.</summary>
    public bool Advance(double dt, double x)
    {
        if (_mode == Mode.Idle)
        {
            return false;
        }

        _time += dt;
        if (!double.IsNaN(x))
        {
            _x = x;
        }

        InvalidateVisual();
        if (_time > (_mode == Mode.Intro ? EndTime : OutroSeconds))
        {
            _mode = Mode.Idle;
            _time = -1;
            return false;
        }

        return true;
    }

    /// <summary>Diagnostic hook: paint one fixed moment of the intro, or of the outro, without running the clock.</summary>
    internal void SetTimeForPreview(double time, bool outro)
    {
        if (outro)
        {
            _mode = Mode.Idle;
            StartOutro();
        }
        else
        {
            _mode = Mode.Intro;
        }

        _time = time;
        InvalidateVisual();
    }

    /// <summary>How large the dock draws its sections on screen; the cubes are laid out at 1 and scaled about the slot.</summary>
    internal double Scale { get; set; } = 1;

    protected override void OnRender(DrawingContext dc)
    {
        dc.PushTransform(new ScaleTransform(Scale, Scale, _x, CubesIntroWindow.SlotY));
        if (_mode == Mode.Intro && _time >= 0)
        {
            RenderIntro(dc, _time);
        }
        else if (_mode == Mode.Outro && _time >= 0)
        {
            RenderOutro(dc, _time);
        }

        dc.Pop();
    }

    private void RenderIntro(DrawingContext dc, double t)
    {
        double slotY = CubesIntroWindow.SlotY;
        double tint = SmoothStep(HandoffStart - 0.6, HandoffStart + 0.3, t);
        for (int i = 0; i < _count; i++)
        {
            // a wave from left to right brings each one in, rising softly into place
            double show = SmoothStep(_appear[i], _appear[i] + 0.45, t);
            if (show <= 0)
            {
                _curAlpha[i] = 0;
                continue;
            }

            double u = Math.Clamp((t - _delay[i]) / LayFlight, 0, 1);
            double e = EaseGlide(u);
            double rem = 1 - e;
            double fadeAt = HandoffStart + 0.15 + (0.35 * _reach[i]);
            double alpha = show * (1 - SmoothStep(fadeAt, fadeAt + CubeFade, t));

            // clear until the cover sweeps across the cloud from left to right
            double paint = SmoothStep(_paint[i], _paint[i] + 0.3, t);
            var color = Mix(Mix(Dust ? DustLight : ClearGlass, _color[i], paint), _cover[i], tint);

            double x, y, size;
            if (Dust)
            {
                // each grain rises out of the slot into a slow whirl above it, then spirals back down to its place
                double rise = SmoothStep(_appear[i], _appear[i] + 0.9, t);
                double angle = _orbitA[i] + (_spin[i] * t);
                double cloudX = _x + (_orbitR[i] * Math.Cos(angle));
                double cloudY = slotY - 82 + (_orbitR[i] * 0.3 * Math.Sin(angle)) - (14 * _depth[i]);
                double homeX = _x + _targetX[i];
                double homeY = slotY + _targetY[i];
                cloudX = homeX + ((cloudX - homeX) * rise);
                cloudY = homeY + ((cloudY - homeY) * rise);
                x = homeX + ((cloudX - homeX) * rem);
                y = homeY + ((cloudY - homeY) * rem);
                // near grains are a little larger in the whirl; every grain stays sharp
                size = _targetSize[i] * (1 + (0.5 * _depth[i] * rem));
                alpha *= 1 - (0.25 * _depth[i] * rem);
            }
            else
            {
                double bob = Math.Sin((t * 1.6) + (_tilt[i] * 7)) * 2.5 * rem;
                x = _x + _targetX[i] + (_dropX[i] * rem);
                y = slotY + _targetY[i] + (_dropY[i] * rem) + bob + ((1 - show) * 12);
                size = _targetSize[i] * (0.85 + (0.15 * e));
                // the painted cloud is denser glass so the cover reads across the gaps; it clears as the cubes land
                alpha *= 1 + (0.9 * paint * rem);
            }

            _curX[i] = x;
            _curY[i] = y;
            _curSize[i] = size;
            _curPitch[i] = SlotPitch + (_tilt[i] * rem);
            _curAlpha[i] = alpha;
            if (alpha < 0.01)
            {
                continue;
            }

            if (Dust)
            {
                DrawGrain(dc, x, y, size, color, alpha);
            }
            else
            {
                DrawCube(dc, x, y, size, _curPitch[i], _yaw[i] * rem, _roll[i] * rem, color, alpha);
            }
        }

        _drawn = true;
    }

    private void RenderOutro(DrawingContext dc, double t)
    {
        const double drag = 2.0;
        for (int i = 0; i < _count; i++)
        {
            double a0 = _curAlpha[i];
            if (a0 < 0.01)
            {
                continue;
            }

            // the same wave takes them away from left to right; until it reaches one it holds its place
            double start = (OutroWave * WaveAt(_curX[i])) + (0.1 * Hash01(i, 0x5A3D));
            double tl = Math.Max(0, t - start);
            double u = Math.Min(1, tl / OutroLife);
            double travel = (1 - Math.Exp(-drag * tl)) / drag;
            double alpha = a0 * (1 - SmoothStep(0.2 + (0.4 * Hash01(i, 0x2D93)), 1, u));
            if (alpha < 0.01)
            {
                continue;
            }

            if (Dust)
            {
                // the grains lift off like dust caught in a draught, curling as they go
                double x = _curX[i] + (_velX[i] * travel * 0.7) + (Math.Sin((tl * 4) + (_orbitA[i] * 3)) * 14 * u);
                double y = _curY[i] + (_velY[i] * travel * 1.1);
                double size = _curSize[i] * (1 - (0.3 * u));
                DrawGrain(dc, x, y, size, Mix(_cover[i], DustLight, SmoothStep(0, 0.6, u) * 0.4), alpha);
            }
            else
            {
                double x = _curX[i] + (_velX[i] * travel);
                double y = _curY[i] + (_velY[i] * travel);
                double size = _curSize[i] * (1 - (0.35 * u));
                double pitch = _curPitch[i] + (_tilt[i] * 0.8 * u);
                double roll = (Hash01(i, 0x6D2F) - 0.5) * 0.6 * u;
                var color = Mix(_color[i], ClearGlass, SmoothStep(0.1, 0.8, u) * 0.5);
                DrawCube(dc, x, y, size, pitch, 0, roll, color, alpha);
            }
        }
    }

    /// <summary>0 at the slot's left end, 1 at its right: where a wave across it has got to.</summary>
    private double WaveAt(double x) => Math.Clamp(((x - _x) / StripWidth) + 0.5, 0, 1);

    /// <summary>One grain of dust: a crisp solid dot without glow, its brush cached by colour and strength.</summary>
    private void DrawGrain(DrawingContext dc, double x, double y, double radius, Color color, double alpha)
    {
        int level = Math.Clamp((int)((alpha * 15) + 0.5), 0, 15);
        if (level == 0)
        {
            return;
        }

        int key = (((color.R >> 3) << 10) | ((color.G >> 3) << 5) | (color.B >> 3)) << 4 | level;
        if (!_grains.TryGetValue(key, out var brush))
        {
            brush = Frozen(new SolidColorBrush(Color.FromArgb((byte)(level * 17),
                (byte)((color.R & 0xF8) | 4), (byte)((color.G & 0xF8) | 4), (byte)((color.B & 0xF8) | 4))));
            _grains[key] = brush;
        }

        dc.DrawEllipse(brush, null, new Point(x, y), radius * 0.85, radius * 0.85);
    }

    /// <summary>One block of clear glass: tinted faces shaded by the light and thin edges, without shine.</summary>
    private void DrawCube(DrawingContext dc, double x, double y, double size, double pitch, double yaw, double roll, Color color, double alpha)
    {
        double cp = Math.Cos(pitch), sp = Math.Sin(pitch);
        double cy = Math.Cos(yaw), sy = Math.Sin(yaw);
        double cr = Math.Cos(roll), sr = Math.Sin(roll);

        for (int k = 0; k < 8; k++)
        {
            Rotate(Corners[k, 0], Corners[k, 1], Corners[k, 2], cp, sp, cy, sy, cr, sr, out var rx, out var ry, out _);
            _projected[k] = new Point(x + (rx * size), y + (ry * size));
        }

        bool tiny = size < TinyCube;
        int level = Math.Clamp((int)(alpha * EdgePens.Length) - 1, 0, EdgePens.Length - 1);

        for (int f = 0; f < 6; f++)
        {
            Rotate(Normals[f, 0], Normals[f, 1], Normals[f, 2], cp, sp, cy, sy, cr, sr, out var nx, out var ny, out var nz);
            if (nz > -0.02)
            {
                continue;
            }

            double lambert = Math.Max(0, (nx * Light[0]) + (ny * Light[1]) + (nz * Light[2]));

            // a solid block of clear glass: every face filled, the faces turned from the light denser and darker,
            // so the cube reads as a body rather than a hollow outline; no shine anywhere
            double body = tiny ? 0.85 : 0.30 + (0.22 * lambert);
            double shade = 0.78 + (0.22 * lambert);
            var brush = new SolidColorBrush(Color.FromArgb(
                (byte)Math.Clamp(body * alpha * 255, 0, 255),
                (byte)(color.R * shade), (byte)(color.G * shade), (byte)(color.B * shade)));
            brush.Freeze();
            dc.DrawGeometry(brush, tiny ? null : EdgePens[level], Face(f));
        }
    }

    private StreamGeometry Face(int f)
    {
        var face = Faces[f];
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(_projected[face[0]], true, true);
            c.LineTo(_projected[face[1]], true, false);
            c.LineTo(_projected[face[2]], true, false);
            c.LineTo(_projected[face[3]], true, false);
        }

        g.Freeze();
        return g;
    }

    /// <summary>Pitch about x, then yaw about y, then roll about z.</summary>
    private static void Rotate(double x, double y, double z, double cp, double sp, double cy, double sy, double cr, double sr,
        out double ox, out double oy, out double oz)
    {
        double y1 = (y * cp) - (z * sp);
        double z1 = (y * sp) + (z * cp);
        double x2 = (x * cy) + (z1 * sy);
        double z2 = (-x * sy) + (z1 * cy);
        ox = (x2 * cr) - (y1 * sr);
        oy = (x2 * sr) + (y1 * cr);
        oz = z2;
    }

    private void Layout()
    {
        _count = 0;

        // The cover strip first, so the buttons draw on top of it.
        if (Dust)
        {
            LayDust();
        }
        else
        {
            LayCubes();
        }

        double right = MediaWidth / 2;
        AddGlyph("previous", right - 75, 20);
        AddGlyph(_playing ? "pause" : "play", right - 45, 22);
        AddGlyph("next", right - 15, 20);

        for (int i = 0; i < _count; i++)
        {
            // the cloud is the strip blown apart and lifted; the middle lands first, the buttons last
            double reach = Math.Abs(_targetX[i]) / (StripWidth / 2);
            _reach[i] = Math.Clamp(reach, 0, 1);
            double wave = Math.Clamp((_targetX[i] / StripWidth) + 0.5, 0, 1);
            _appear[i] = (AppearWave * wave) + (0.1 * Hash01(i, 0x4B19));
            _paint[i] = PaintStart + (0.35 * wave) + (0.08 * Hash01(i, 0x3A57));
            _delay[i] = GatherStart + (0.3 * reach) + (0.08 * Hash01(i, 0x7E31)) + (_glyph[i] ? 0.15 : 0);
            _dropX[i] = (_targetX[i] * 0.3) + ((Hash01(i, 0x1D3A) - 0.5) * 14);
            _dropY[i] = (_targetY[i] * 0.6) - (62 + (22 * Hash01(i, 0x5C71)));
            _tilt[i] = (Hash01(i, 0x77E1) - 0.5) * 0.5;
            _yaw[i] = (Hash01(i, 0x2F61) - 0.5) * 0.6;
            _roll[i] = (Hash01(i, 0x6B03) - 0.5) * 0.4;
            _orbitR[i] = 35 + (125 * Hash01(i, 0x1F9B));
            _orbitA[i] = Math.PI * 2 * Hash01(i, 0x6C2D);
            _spin[i] = 2.4 / (0.6 + (_orbitR[i] / 60));
            _depth[i] = Hash01(i, 0x3E85);
        }
    }

    private void LayCubes()
    {
        var strip = Sample(_art, StripCols * 3, StripRows * 3, StripWidth / StripHeight);
        double cellW = StripWidth / StripCols;
        double cellH = StripHeight / StripRows;
        for (int row = 0; row < StripRows; row++)
        {
            for (int col = 0; col < StripCols; col++)
            {
                // the art washed towards clear glass, so a dark cover still reads as glass rather than a dark grid
                var c = Average(strip, StripCols * 3, col * 3, row * 3);
                Add(((col + 0.5) * cellW) - (StripWidth / 2), ((row + 0.5) * cellH) - (StripHeight / 2),
                    Math.Min(cellW, cellH) * 0.8, false, Mix(Boost(c, 1.2), ClearGlass, 0.2), Boost(c, 0.75));
            }
        }
    }

    /// <summary>A loose, slightly jittered field of grains over the strip, so it reads as dust, not a grid.</summary>
    private void LayDust()
    {
        var strip = Sample(_art, DustCols, DustRows, StripWidth / StripHeight);
        double cellW = StripWidth / DustCols;
        double cellH = StripHeight / DustRows;
        for (int row = 0; row < DustRows; row++)
        {
            for (int col = 0; col < DustCols; col++)
            {
                int n = (row * DustCols) + col;
                var c = strip[n];
                double jx = (Hash01(n, 0x2B71) - 0.5) * 0.7 * cellW;
                double jy = (Hash01(n, 0x7D13) - 0.5) * 0.7 * cellH;
                Add(((col + 0.5) * cellW) - (StripWidth / 2) + jx, ((row + 0.5) * cellH) - (StripHeight / 2) + jy,
                    Math.Min(cellW, cellH) * 0.42, false, Mix(Boost(c, 1.25), DustLight, 0.25), Boost(c, 0.85));
            }
        }
    }

    private void Add(double x, double y, double size, bool glyph, Color color, Color? cover = null)
    {
        if (_count >= MaxCubes)
        {
            return;
        }

        _targetX[_count] = x;
        _targetY[_count] = y;
        _targetSize[_count] = size;
        _glyph[_count] = glyph;
        _color[_count] = color;
        _cover[_count] = cover ?? color;
        _count++;
    }

    /// <summary>A dot-matrix glyph from the dock's icon set, halved to a 12-cell grid; one cube per lit cell.</summary>
    private void AddGlyph(string key, double centerX, double size)
    {
        if (!DotMatrixIcons.TryGetGrid(key, out var grid))
        {
            return;
        }

        int block = DotMatrixIcons.GridSize / GlyphCells;
        double cell = size / GlyphCells;
        for (int gy = 0; gy < GlyphCells; gy++)
        {
            for (int gx = 0; gx < GlyphCells; gx++)
            {
                int lit = 0;
                for (int y = 0; y < block; y++)
                {
                    for (int x = 0; x < block; x++)
                    {
                        var row = grid[(gy * block) + y];
                        int col = (gx * block) + x;
                        if (col < row.Length && row[col] == '#')
                        {
                            lit++;
                        }
                    }
                }

                if (lit * 2 >= block * block)
                {
                    Add(centerX - (size / 2) + ((gx + 0.5) * cell), ((gy + 0.5) * cell) - (size / 2), cell * (Dust ? 0.5 : 0.95), true, GlyphGlass);
                }
            }
        }
    }

    /// <summary>The art filled into a w x h grid of colours (cropped to <paramref name="aspect"/>), or plain glass without art.</summary>
    private static Color[] Sample(ImageSource? art, int w, int h, double aspect = 1)
    {
        var colors = new Color[w * h];
        if (art is not null)
        {
            try
            {
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(new ImageBrush(art) { Stretch = Stretch.UniformToFill }, null, new Rect(0, 0, w * 4, h * 4));
                }

                var rendered = new RenderTargetBitmap(w * 4, h * 4, 96, 96, PixelFormats.Pbgra32);
                rendered.Render(visual);
                var pixels = new byte[w * 4 * h * 4 * 4];
                rendered.CopyPixels(pixels, w * 4 * 4, 0);
                int stride = w * 4;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        double r = 0, g = 0, b = 0, a = 0;
                        for (int yy = 0; yy < 4; yy++)
                        {
                            for (int xx = 0; xx < 4; xx++)
                            {
                                int o = ((((y * 4) + yy) * stride) + (x * 4) + xx) * 4;
                                b += pixels[o];
                                g += pixels[o + 1];
                                r += pixels[o + 2];
                                a += pixels[o + 3];
                            }
                        }

                        // premultiplied: dividing by the coverage brings back the colour of see-through art
                        double k = a > 0 ? 255.0 / a : 0;
                        colors[(y * w) + x] = Color.FromRgb(
                            (byte)Math.Min(255, r * k), (byte)Math.Min(255, g * k), (byte)Math.Min(255, b * k));
                    }
                }

                return colors;
            }
            catch
            {
                // odd sources: fall through to plain glass rather than throwing mid-intro
            }
        }

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double l = 0.55 + (0.35 * (1 - ((double)y / h))) + (0.1 * Math.Sin((x * aspect / w) * Math.PI));
                colors[(y * w) + x] = Mix(Color.FromRgb(0x60, 0x66, 0x70), ClearGlass, l);
            }
        }

        return colors;
    }

    private static Color Average(Color[] colors, int width, int x0, int y0)
    {
        double r = 0, g = 0, b = 0;
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                var c = colors[((y0 + y) * width) + x0 + x];
                r += c.R;
                g += c.G;
                b += c.B;
            }
        }

        return Color.FromRgb((byte)(r / 9), (byte)(g / 9), (byte)(b / 9));
    }

    private static Pen[] BuildPens(int peak, double width)
    {
        var pens = new Pen[8];
        for (int i = 0; i < pens.Length; i++)
        {
            byte a = (byte)(peak * (i + 1) / pens.Length);
            pens[i] = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(a, 0xFF, 0xFF, 0xFF)), width)
            {
                LineJoin = PenLineJoin.Miter,
            });
        }

        return pens;
    }

    private static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)(a.R + ((b.R - a.R) * t)),
            (byte)(a.G + ((b.G - a.G) * t)),
            (byte)(a.B + ((b.B - a.B) * t)));
    }

    private static Color Boost(Color c, double k) => Color.FromRgb(
        (byte)Math.Min(255, c.R * k), (byte)Math.Min(255, c.G * k), (byte)Math.Min(255, c.B * k));

    private static double SmoothStep(double edge0, double edge1, double x)
    {
        double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - (2 * t));
    }

    /// <summary>Starts unhurried and glides to a stop with no overshoot.</summary>
    private static double EaseGlide(double t)
    {
        double s = t * t * (3 - (2 * t));
        double u = 1 - t;
        return (s * 0.5) + ((1 - (u * u * u * u * u)) * 0.5);
    }

    private static double Hash01(int x, int seed)
    {
        unchecked
        {
            uint h = (uint)((x * 374761393) + (seed * 668265263));
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0x00FFFFFF) / 16777215.0;
        }
    }

    private static double[] Normalize(double x, double y, double z)
    {
        double l = Math.Sqrt((x * x) + (y * y) + (z * z));
        return new[] { x / l, y / l, z / l };
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
