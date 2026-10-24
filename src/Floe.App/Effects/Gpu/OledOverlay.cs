using Floe.Core.Visualization;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// What the OLED panel shows on top of the visualizer, drawn on the CPU straight into the panel's cells: the
/// song that plays (cover, title, artist, controls, progress) in the strip's left wing, and a little CRT-headed
/// robot who walks the whole panel while nothing plays, looking around with the eye on his screen.
/// <para>
/// <see cref="SetMedia"/> and <see cref="SetDock"/> come from the UI thread; <see cref="Draw"/> runs on the
/// render thread and writes a brightness and a cover mask per cell (1 = this cell shows the overlay, not the
/// visualizer under it).
/// </para>
/// </summary>
internal sealed class OledOverlay
{
    private const int CoverSource = 64;
    private const float ScrollSpeed = 10, ScrollGap = 24, ScrollPause = 2;
    private const float RobotSpeed = 7, RobotRunSpeed = 22;

    private readonly object _gate = new();
    private readonly Random _random = new(7);

    // from the UI thread
    private string _title = string.Empty, _artist = string.Empty;
    private double _position, _duration;
    private bool _playing;
    private byte[]? _coverPixels;
    private int _coverVersion;
    private float _dockLeft = .5f, _dockRight = .5f;

    // render thread
    private float[] _cover = Array.Empty<float>();
    private int _coverSize, _coverBuiltFrom = -1;
    private float _scroll;
    private float _sinceMusic = 99;
    private readonly Robot _robot = new();

    public void SetMedia(string title, string artist, double position, double duration, bool playing, byte[]? cover)
    {
        lock (_gate)
        {
            _title = title;
            _artist = artist;
            _position = position;
            _duration = duration;
            _playing = playing;
            if (!ReferenceEquals(cover, _coverPixels))
            {
                _coverPixels = cover;
                _coverVersion++;
            }
        }
    }

    /// <summary>The dock's capsule across the strip, 0..1 from the left.</summary>
    public void SetDock(float left, float right)
    {
        lock (_gate)
        {
            _dockLeft = left;
            _dockRight = right;
        }
    }

    /// <summary>The square size the cover is converted from: <see cref="SetMedia"/> takes it as BGRA, this many pixels a side.</summary>
    public static int CoverPixels => CoverSource;

    public void Draw(float[] value, float[] mask, int cols, int rows, float dt, float time)
    {
        Array.Clear(value);
        Array.Clear(mask);

        string title, artist;
        double position, duration;
        bool playing;
        float dockLeft;
        byte[]? pixels;
        int version;
        lock (_gate)
        {
            title = _title;
            artist = _artist;
            if (_playing) _position += dt;
            position = _duration > 0 ? Math.Min(_position, _duration) : _position;
            duration = _duration;
            playing = _playing;
            dockLeft = _dockLeft;
            pixels = _coverPixels;
            version = _coverVersion;
        }

        _sinceMusic = playing ? 0 : _sinceMusic + dt;

        // the media lives in the left wing, from the bezel to just short of the dock
        int wingEnd = Math.Min(cols, (int)(dockLeft * cols) - 3);
        bool media = title.Length > 0 && wingEnd > 40;
        if (media)
        {
            BuildCover(pixels, version, rows - 2);
            DrawMedia(value, mask, cols, rows, wingEnd, title, artist, position, duration, playing, dt);
        }

        // he keeps off the song in the left wing
        _robot.Update(dt, time, cols, media ? wingEnd + 14 : 10, wanted: _sinceMusic > 1.5f, _random);
        _robot.Draw(value, mask, cols, rows, time);
    }

    private void BuildCover(byte[]? pixels, int version, int size)
    {
        if (version == _coverBuiltFrom && size == _coverSize) return;
        _coverBuiltFrom = version;
        _coverSize = size;
        _cover = pixels is null || size < 4
            ? Array.Empty<float>()
            : CoverDither.Convert(pixels, CoverSource, CoverSource, CoverSource * 4, size);
    }

    private void DrawMedia(float[] value, float[] mask, int cols, int rows, int wingEnd, string title, string artist,
        double position, double duration, bool playing, float dt)
    {
        // paused or stopped, the last song stays on the panel, dimmed
        float bright = playing ? 1 : .42f;
        int x = 1;
        Fill(mask, cols, rows, 0, 0, wingEnd + 2, rows, 1);

        if (_cover.Length == _coverSize * _coverSize && _coverSize > 0)
        {
            for (int y = 0; y < _coverSize; y++)
            {
                for (int c = 0; c < _coverSize; c++)
                {
                    Plot(value, cols, rows, x + c, 1 + y, _cover[(y * _coverSize) + c] * bright);
                }
            }

            x += _coverSize + 4;
        }

        int textEnd = wingEnd;
        if (textEnd - x < 24) return;

        // the title scrolls when it does not fit, resting a moment at the start of every pass
        int width = PixelFont.Measure(title);
        int room = textEnd - x;
        if (width <= room)
        {
            _scroll = 0;
            PixelFont.Draw(value, cols, rows, title, x, 1, bright);
        }
        else
        {
            float loop = width + ScrollGap;
            _scroll += dt * ScrollSpeed;
            if (_scroll > loop + (ScrollPause * ScrollSpeed)) _scroll = 0;
            int offset = (int)Math.Max(0, _scroll - (ScrollPause * ScrollSpeed));
            PixelFont.Draw(value, cols, rows, title, x - offset, 1, bright, x, textEnd);
            PixelFont.Draw(value, cols, rows, title, x - offset + (int)loop, 1, bright, x, textEnd);
        }

        // the artist, and the time on the same line at the right
        string time = duration > 0 ? $"{Clock(position)}/{Clock(duration)}" : Clock(position);
        int timeWidth = PixelFont.Measure(time);
        int artistEnd = textEnd - timeWidth - 6;
        PixelFont.Draw(value, cols, rows, artist, x, 9, .66f * bright, x, artistEnd);
        PixelFont.Draw(value, cols, rows, time, textEnd - timeWidth, 9, .55f * bright);

        // controls and the progress bar along the bottom
        int iconsY = Math.Min(rows - 5, 17);
        Icon(value, cols, rows, Previous, x, iconsY, .8f * bright);
        Icon(value, cols, rows, playing ? Pause : Play, x + 8, iconsY, bright);
        Icon(value, cols, rows, Next, x + 16, iconsY, .8f * bright);
        int bar0 = x + 26, bar1 = textEnd - 1, barY = iconsY + 2;
        if (bar1 - bar0 < 10) return;
        float p = duration > 0 ? (float)Math.Clamp(position / duration, 0, 1) : 0;
        int knob = bar0 + (int)MathF.Round((bar1 - bar0) * p);
        for (int c = bar0; c <= bar1; c++) Plot(value, cols, rows, c, barY, (c <= knob ? 1 : .3f) * bright);
        Fill(value, cols, rows, knob - 1, barY - 1, 3, 3, bright);
    }

    private static string Clock(double seconds)
    {
        int s = (int)Math.Max(0, seconds);
        return $"{s / 60}:{s % 60:00}";
    }

    // 5x5 icons, bit 4 = leftmost column
    private static readonly byte[] Previous = { 0b10001, 0b10011, 0b10111, 0b10011, 0b10001 };
    private static readonly byte[] Next = { 0b10001, 0b11001, 0b11101, 0b11001, 0b10001 };
    private static readonly byte[] Play = { 0b10000, 0b11000, 0b11100, 0b11000, 0b10000 };
    private static readonly byte[] Pause = { 0b11011, 0b11011, 0b11011, 0b11011, 0b11011 };

    private static void Icon(float[] value, int cols, int rows, byte[] icon, int x, int y, float v)
    {
        for (int r = 0; r < icon.Length; r++)
        {
            for (int c = 0; c < 5; c++)
            {
                if (((icon[r] >> (4 - c)) & 1) != 0) Plot(value, cols, rows, x + c, y + r, v);
            }
        }
    }

    private static void Plot(float[] grid, int cols, int rows, int x, int y, float v)
    {
        if ((uint)x >= (uint)cols || (uint)y >= (uint)rows) return;
        int i = (y * cols) + x;
        if (v > grid[i]) grid[i] = v;
    }

    private static void Set(float[] grid, int cols, int rows, int x, int y, float v)
    {
        if ((uint)x >= (uint)cols || (uint)y >= (uint)rows) return;
        grid[(y * cols) + x] = v;
    }

    private static void Fill(float[] grid, int cols, int rows, int x, int y, int w, int h, float v)
    {
        for (int r = y; r < y + h; r++)
        {
            for (int c = x; c < x + w; c++) Set(grid, cols, rows, c, r, v);
        }
    }

    // ---- the robot --------------------------------------------------------------------------------

    /// <summary>
    /// A CRT monitor for a head, a little body, arms and legs. He walks in from an edge when the music stops,
    /// strolls about, stops to look around (the eye on his screen wanders and blinks), waves now and then, and
    /// runs off the nearest edge when the music starts again.
    /// </summary>
    private sealed class Robot
    {
        private enum Mode { Away, Walk, Look, Wave, Leave }

        private Mode _mode = Mode.Away;
        private float _x = -20, _dir = 1, _until, _lookUntil;
        private float _eyeX, _eyeY, _eyeToX, _eyeToY, _blink = -1;
        private float _step;

        public void Update(float dt, float time, int cols, int minX, bool wanted, Random random)
        {
            if (!wanted && _mode is not Mode.Away and not Mode.Leave)
            {
                // the music is back: a quick wave, then off the nearest edge he is allowed to
                _mode = Mode.Leave;
                _dir = minX <= 10 && _x < cols / 2f ? -1 : 1;
                _until = time + .6f;
            }

            switch (_mode)
            {
                case Mode.Away:
                    if (!wanted) return;
                    _dir = minX <= 10 && random.Next(2) == 0 ? 1 : -1;
                    _x = _dir > 0 ? -12 : cols + 12;
                    _mode = Mode.Walk;
                    _until = time + 3 + (3 * random.NextSingle());
                    break;
                case Mode.Leave:
                    if (time > _until)
                    {
                        Walk(dt, RobotRunSpeed);
                        if (_x < -14 || _x > cols + 14) _mode = Mode.Away;
                    }

                    break;
                case Mode.Walk:
                    Walk(dt, RobotSpeed);
                    if (_x < minX && _dir < 0) _dir = 1;
                    if (_x > cols - 10 && _dir > 0) _dir = -1;
                    _eyeToX = _dir * 2.5f;
                    _eyeToY = 0;
                    if (time > _until)
                    {
                        _mode = random.NextSingle() < .3f ? Mode.Wave : Mode.Look;
                        _until = time + (_mode == Mode.Wave ? 1.6f : 2 + (3 * random.NextSingle()));
                    }

                    break;
                default:
                    if (time > _lookUntil)
                    {
                        _eyeToX = (random.NextSingle() - .5f) * 6;
                        _eyeToY = MathF.Round((random.NextSingle() - .5f) * 2);
                        _lookUntil = time + .4f + (1.2f * random.NextSingle());
                        if (random.NextSingle() < .3f) _blink = time;
                    }

                    if (time > _until)
                    {
                        _mode = Mode.Walk;
                        if (random.NextSingle() < .5f) _dir = -_dir;
                        _until = time + 2 + (4 * random.NextSingle());
                    }

                    break;
            }

            float ease = 1 - MathF.Exp(-dt * 12);
            _eyeX += (_eyeToX - _eyeX) * ease;
            _eyeY += (_eyeToY - _eyeY) * ease;
        }

        private void Walk(float dt, float speed)
        {
            _x += _dir * dt * speed;
            _step += dt * speed * 1.2f;
        }

        public void Draw(float[] value, float[] mask, int cols, int rows, float time)
        {
            if (_mode == Mode.Away) return;
            bool walking = _mode is Mode.Walk or (Mode.Leave);
            int x = (int)MathF.Round(_x);
            int foot = rows - 1;
            // a bob of the head on every step
            int bob = walking && MathF.Sin(_step * 2) > .3f ? 1 : 0;
            int headTop = Math.Max(0, foot - 21) + bob;

            // the head: a CRT case with depth on its right and bottom, the screen set into it
            int hx = x - 7, hy = headTop;
            Box(value, mask, cols, rows, hx, hy, 15, 11, .28f, .75f, .5f);
            for (int i = 1; i < 12; i++) Shade(value, mask, cols, rows, hx + 15, hy + i, .2f);
            for (int i = 1; i < 16; i++) Shade(value, mask, cols, rows, hx + i, hy + 11, .2f);
            Box(value, mask, cols, rows, hx + 1, hy + 1, 13, 8, 0, .9f, .9f);
            Shade(value, mask, cols, rows, hx + 3, hy + 9, .9f);
            Shade(value, mask, cols, rows, hx + 5, hy + 9, .5f);
            Shade(value, mask, cols, rows, hx + 12, hy + 9, MathF.Sin(time * 5) > 0 ? 1 : .25f);
            Line(value, mask, cols, rows, hx + 11, hy - 1, hx + 13, hy - 3, .6f);

            // the eye on the screen
            int ex = hx + 7 + (int)MathF.Round(_eyeX * .6f), ey = hy + 5;
            if (time - _blink < .14f)
            {
                Line(value, mask, cols, rows, ex - 3, ey, ex + 3, ey, 1);
            }
            else
            {
                for (int dx = -3; dx <= 3; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (Math.Abs(dx) < 3 || dy == 0) Shade(value, mask, cols, rows, ex + dx, ey + dy, 1);
                    }
                }

                int px = ex + (int)MathF.Round(_eyeX * .35f), py = ey + (int)MathF.Round(_eyeY);
                Shade(value, mask, cols, rows, px, py, 0);
                Shade(value, mask, cols, rows, px + 1, py, 0);
            }

            // neck and body
            int body = hy + 12;
            for (int i = -1; i <= 1; i++) Shade(value, mask, cols, rows, x + i, body, .6f);
            Box(value, mask, cols, rows, x - 4, body + 1, 9, 5, .42f, .75f, .6f);
            Shade(value, mask, cols, rows, x, body + 3, 1);
            Shade(value, mask, cols, rows, x, body + 4, .8f);

            // arms swing against the legs; the right one waves when he waves
            float swing = walking ? MathF.Sin(_step) * 2 : 0;
            int shoulder = body + 2;
            Arm(value, mask, cols, rows, x - 5, shoulder, -1, swing, false, time);
            Arm(value, mask, cols, rows, x + 5, shoulder, 1, -swing, _mode is Mode.Wave or Mode.Leave, time);

            int hip = body + 6;
            Leg(value, mask, cols, rows, x - 2, hip, foot, walking ? MathF.Sin(_step) : 0);
            Leg(value, mask, cols, rows, x + 2, hip, foot, walking ? -MathF.Sin(_step) : 0);
        }

        private void Arm(float[] value, float[] mask, int cols, int rows, int sx, int sy, int side, float swing, bool wave, float time)
        {
            if (wave)
            {
                int wx = sx + (side * 2) + (int)MathF.Round(MathF.Sin(time * 14) * 1.5f);
                Line(value, mask, cols, rows, sx, sy, sx + (side * 2), sy - 3, .7f);
                Line(value, mask, cols, rows, sx + (side * 2), sy - 3, wx, sy - 6, .7f);
                Shade(value, mask, cols, rows, wx, sy - 6, 1);
                return;
            }

            int ex = sx + side - (int)MathF.Round(swing * side * .5f), ey = sy + 3;
            int hx = sx + side - (int)MathF.Round(swing * side), hy = sy + 5;
            Line(value, mask, cols, rows, sx, sy, ex, ey, .7f);
            Line(value, mask, cols, rows, ex, ey, hx, hy, .7f);
            Shade(value, mask, cols, rows, hx, hy, 1);
        }

        private void Leg(float[] value, float[] mask, int cols, int rows, int lx, int hip, int foot, float phase)
        {
            int kx = lx + (int)MathF.Round(phase), ky = hip + 1 - (phase > .3f ? 1 : 0);
            int fx = lx + (int)MathF.Round(phase * 2);
            Line(value, mask, cols, rows, lx, hip, kx, ky, .7f);
            Line(value, mask, cols, rows, kx, ky, fx, foot - 1, .7f);
            Line(value, mask, cols, rows, fx, foot, fx + (int)_dir * 2, foot, .9f);
        }

        // the robot covers what is behind him: every cell he draws is masked, dark ones included
        private static void Shade(float[] value, float[] mask, int cols, int rows, int x, int y, float v)
        {
            if ((uint)x >= (uint)cols || (uint)y >= (uint)rows) return;
            int i = (y * cols) + x;
            value[i] = v;
            mask[i] = 1;
        }

        private static void Box(float[] value, float[] mask, int cols, int rows, int x, int y, int w, int h, float fill, float lit, float dark)
        {
            for (int r = 0; r < h; r++)
            {
                for (int c = 0; c < w; c++)
                {
                    float v = r == 0 || c == 0 ? lit : r == h - 1 || c == w - 1 ? dark : fill;
                    Shade(value, mask, cols, rows, x + c, y + r, v);
                }
            }
        }

        private static void Line(float[] value, float[] mask, int cols, int rows, int x0, int y0, int x1, int y1, float v)
        {
            int n = Math.Max(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)), 1);
            for (int i = 0; i <= n; i++)
            {
                Shade(value, mask, cols, rows, x0 + (int)MathF.Round((x1 - x0) * i / (float)n), y0 + (int)MathF.Round((y1 - y0) * i / (float)n), v);
            }
        }
    }
}
