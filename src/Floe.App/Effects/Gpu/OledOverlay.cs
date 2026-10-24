using Floe.Core.Visualization;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// What the OLED panel shows on top of the visualizer, drawn on the CPU straight into the panel's cells: the
/// song that plays (cover, title, artist, controls, progress) in the strip's left wing, and a little CRT-headed
/// robot who walks the panel while nothing plays, looking around with the eyes on his screen, and dances at its
/// right end while music plays.
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
    private const int TextWidth = 150, FadeCells = 14;

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

    public void Draw(float[] value, float[] mask, int cols, int rows, float dt, float time, float beat, ReadOnlySpan<float> bands)
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

        // the media lives in the left wing, from the bezel to just short of the dock
        int wingEnd = Math.Min(cols, (int)(dockLeft * cols) - 3);
        bool media = title.Length > 0 && wingEnd > 40;
        int mediaEnd = 0;
        if (media)
        {
            BuildCover(pixels, version, Math.Clamp(rows - 8, 4, 26));
            mediaEnd = DrawMedia(value, mask, cols, rows, wingEnd, title, artist, position, duration, playing, dt);
        }

        // he keeps off the song in the left wing, and dances at the right end while it plays
        _robot.Update(dt, time, cols, media ? mediaEnd + FadeCells + 12 : 12, music: playing, beat, _random);
        _robot.Draw(value, mask, cols, rows, time, bands);
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

    // Returns where the block ends: past it the visualizer fades in over a few cells instead of a hard edge.
    private int DrawMedia(float[] value, float[] mask, int cols, int rows, int wingEnd, string title, string artist,
        double position, double duration, bool playing, float dt)
    {
        // paused or stopped, the last song stays on the panel, dimmed
        float bright = playing ? 1 : .42f;
        int x = 2;
        bool cover = _cover.Length == _coverSize * _coverSize && _coverSize > 0;
        int top = cover ? (rows - _coverSize) / 2 : Math.Max(1, (rows - 22) / 2);
        int textEnd = Math.Min(wingEnd, x + (cover ? _coverSize + 4 : 0) + TextWidth);
        int end = textEnd + 2;
        Fill(mask, cols, rows, 0, 0, end, rows, 1);
        for (int c = 0; c < FadeCells; c++)
        {
            Fill(mask, cols, rows, end + c, 0, 1, rows, 1 - ((c + 1) / (float)(FadeCells + 1)));
        }

        if (cover)
        {
            for (int y = 0; y < _coverSize; y++)
            {
                for (int c = 0; c < _coverSize; c++)
                {
                    Plot(value, cols, rows, x + c, top + y, _cover[(y * _coverSize) + c] * bright);
                }
            }

            x += _coverSize + 4;
        }

        if (textEnd - x < 24) return end;

        // the title scrolls when it does not fit, resting a moment at the start of every pass
        int width = PixelFont.Measure(title);
        int room = textEnd - x;
        if (width <= room)
        {
            _scroll = 0;
            PixelFont.Draw(value, cols, rows, title, x, top, bright);
        }
        else
        {
            float loop = width + ScrollGap;
            _scroll += dt * ScrollSpeed;
            if (_scroll > loop + (ScrollPause * ScrollSpeed)) _scroll = 0;
            int offset = (int)Math.Max(0, _scroll - (ScrollPause * ScrollSpeed));
            PixelFont.Draw(value, cols, rows, title, x - offset, top, bright, x, textEnd);
            PixelFont.Draw(value, cols, rows, title, x - offset + (int)loop, top, bright, x, textEnd);
        }

        // the artist, and the time on the same line at the right
        string time = duration > 0 ? $"{Clock(position)}/{Clock(duration)}" : Clock(position);
        int timeWidth = PixelFont.Measure(time);
        int artistEnd = textEnd - timeWidth - 6;
        PixelFont.Draw(value, cols, rows, artist, x, top + 10, .66f * bright, x, artistEnd);
        PixelFont.Draw(value, cols, rows, time, textEnd - timeWidth, top + 10, .55f * bright);

        // controls and the progress bar along the bottom
        int iconsY = Math.Min(rows - 6, top + Math.Max(_coverSize, 22) - 5);
        Icon(value, cols, rows, Previous, x, iconsY, .8f * bright);
        Icon(value, cols, rows, playing ? Pause : Play, x + 8, iconsY, bright);
        Icon(value, cols, rows, Next, x + 16, iconsY, .8f * bright);
        int bar0 = x + 26, bar1 = textEnd - 1, barY = iconsY + 2;
        if (bar1 - bar0 < 10) return end;
        float p = duration > 0 ? (float)Math.Clamp(position / duration, 0, 1) : 0;
        int knob = bar0 + (int)MathF.Round((bar1 - bar0) * p);
        for (int c = bar0; c <= bar1; c++) Plot(value, cols, rows, c, barY, (c <= knob ? 1 : .3f) * bright);
        Fill(value, cols, rows, knob - 1, barY - 1, 3, 3, bright);
        return end;
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
    /// A 16-bit style robot: a CRT monitor for a head (seen from three quarters, two eyes on its screen), a body
    /// with a little equalizer on the chest, gloved hands and boots. While nothing plays he walks the panel,
    /// stops to look around, blinks and waves; while music plays he stands at the right end and dances to it.
    /// </summary>
    private sealed class Robot
    {
        private enum Mode { Away, Walk, Look, Wave, Dance }

        // '.' is see-through; '0'..'5' are the panel's greys from black to full white
        private static readonly string[] Head =
        {
            "............55......",
            ".............3......",
            ".4444444444444444...",
            "455555555555555532..",
            "45311111111111113521",
            "45320000000000003521",
            "45300000000000003521",
            "45300000000000003521",
            "45300000000000003521",
            "45300000000000003521",
            "45300000000000003521",
            "45311111111111113521",
            "45525255555555553321",
            ".2222222222222222211",
        };

        private static readonly string[] Torso =
        {
            "..24444442..",
            ".2455555542.",
            "245300003542",
            "245300003542",
            "245300003542",
            "245333333542",
            ".2455555542.",
            "..22222222..",
        };

        private Mode _mode = Mode.Away;
        private float _x = -30, _dir = 1, _until, _lookUntil;
        private float _eyeX, _eyeY, _eyeToX, _eyeToY, _blink = -1;
        private float _step, _beat;
        private int _beats;
        private bool _wasBeat;

        public void Update(float dt, float time, int cols, int minX, bool music, float beat, Random random)
        {
            _beat = beat;
            bool hit = beat > .55f;
            if (hit && !_wasBeat) _beats++;
            _wasBeat = hit;

            if (music)
            {
                // the music plays: off to the right end, to dance there
                float spot = cols - 28;
                if (_mode == Mode.Away) _x = cols + 14;
                if (MathF.Abs(_x - spot) > 1)
                {
                    _mode = Mode.Walk;
                    _dir = _x < spot ? 1 : -1;
                    Walk(dt, RobotRunSpeed);
                }
                else
                {
                    _mode = Mode.Dance;
                    _dir = -1;
                }

                Look(dt, _dir * .6f, 0);
                return;
            }

            switch (_mode)
            {
                case Mode.Away:
                    _dir = minX <= 12 && random.Next(2) == 0 ? 1 : -1;
                    _x = _dir > 0 ? -16 : cols + 16;
                    _mode = Mode.Walk;
                    _until = time + 3 + (3 * random.NextSingle());
                    break;
                case Mode.Dance:
                    _mode = Mode.Look;
                    _until = time + 2;
                    break;
                case Mode.Walk:
                    Walk(dt, RobotSpeed);
                    if (_x < minX && _dir < 0) _dir = 1;
                    if (_x > cols - 14 && _dir > 0) _dir = -1;
                    _eyeToX = _dir;
                    _eyeToY = 0;
                    if (time > _until && _x > minX && _x < cols - 14)
                    {
                        _mode = random.NextSingle() < .3f ? Mode.Wave : Mode.Look;
                        _until = time + (_mode == Mode.Wave ? 1.8f : 2 + (3 * random.NextSingle()));
                    }

                    break;
                default:
                    if (time > _lookUntil)
                    {
                        _eyeToX = MathF.Round((random.NextSingle() * 2) - 1);
                        _eyeToY = MathF.Round((random.NextSingle() * 2) - 1);
                        _lookUntil = time + .4f + (1.2f * random.NextSingle());
                        if (random.NextSingle() < .35f) _blink = time;
                    }

                    if (time > _until)
                    {
                        _mode = Mode.Walk;
                        if (random.NextSingle() < .5f) _dir = -_dir;
                        _until = time + 2 + (4 * random.NextSingle());
                    }

                    break;
            }

            Look(dt, _eyeToX, _eyeToY);
        }

        private void Look(float dt, float x, float y)
        {
            float ease = 1 - MathF.Exp(-dt * 14);
            _eyeX += (x - _eyeX) * ease;
            _eyeY += (y - _eyeY) * ease;
        }

        private void Walk(float dt, float speed)
        {
            _x += _dir * dt * speed;
            _step += dt * speed * .9f;
        }

        public void Draw(float[] value, float[] mask, int cols, int rows, float time, ReadOnlySpan<float> bands)
        {
            if (_mode == Mode.Away) return;
            bool walking = _mode == Mode.Walk;
            bool dancing = _mode == Mode.Dance;
            bool flip = _dir < 0;
            int x = (int)MathF.Round(_x);

            // from the boots up: boots 2 rows, legs 6, body 8, neck 1, head 14
            int foot = rows - 1;
            int lift = (walking && MathF.Sin(_step * 2) > .3f) || (dancing && _beat > .5f) ? 1 : 0;
            int hip = foot - 7 - lift;
            int torsoTop = hip - 8;
            int headTop = torsoTop - 1 - Head.Length;

            // the head turns with him: the case's depth stays on the side he walks away from
            int headX = flip ? x - 11 : x - 9;
            Sprite(value, mask, cols, rows, Head, headX, headTop, flip);
            if (MathF.Sin(time * 3) > .6f) Put(value, mask, cols, rows, headX + Col(12, Head[0].Length, flip), headTop, .4f);
            Eyes(value, mask, cols, rows, headX, headTop, flip, time);

            Put(value, mask, cols, rows, x - 1, torsoTop - 1, .6f);
            Put(value, mask, cols, rows, x, torsoTop - 1, .6f);
            int torsoX = x - 6;
            Sprite(value, mask, cols, rows, Torso, torsoX, torsoTop, flip);
            Equalizer(value, mask, cols, rows, torsoX + 4, torsoTop + 2, bands, time);

            // arms: they swing with the steps, one waves, both pump on the beat when he dances
            float swing = walking ? MathF.Sin(_step) * 2 : 0;
            bool leftUp = dancing && _beat > .3f && (_beats & 1) == 0;
            bool rightUp = (dancing && _beat > .3f && (_beats & 1) == 1) || _mode == Mode.Wave;
            Arm(value, mask, cols, rows, torsoX - 1, torsoTop + 1, -1, swing, leftUp, time);
            Arm(value, mask, cols, rows, torsoX + 12, torsoTop + 1, 1, -swing, rightUp, time);

            float stride = walking ? MathF.Sin(_step) : dancing ? ((_beats & 1) == 0 ? .5f : -.5f) : 0;
            Leg(value, mask, cols, rows, x - 3, hip, foot, stride, flip);
            Leg(value, mask, cols, rows, x + 2, hip, foot, -stride, flip);
        }

        // Two round eyes on the screen: their pupils look about, they blink together, and they turn to happy
        // little arcs when he waves or dances.
        private void Eyes(float[] value, float[] mask, int cols, int rows, int headX, int headTop, bool flip, float time)
        {
            int w = Head[0].Length;
            bool happy = _mode is Mode.Wave or Mode.Dance;
            bool shut = time - _blink < .13f;
            int py = (int)MathF.Round(Math.Clamp(_eyeY, -1, 1));
            int px = (int)MathF.Round(Math.Clamp(_eyeX * (flip ? -1 : 1), -1, 1));
            for (int e = 0; e < 2; e++)
            {
                int left = e == 0 ? 4 : 10;
                for (int r = 0; r < 4; r++)
                {
                    for (int c = 0; c < 4; c++)
                    {
                        float v;
                        if (happy) v = (r == 1 && c is 1 or 2) || (r == 2 && c is 0 or 3) ? 1 : -1;
                        else if (shut) v = r == 2 ? .8f : -1;
                        else if ((r is 0 or 3) && (c is 0 or 3)) v = -1;
                        else v = (c - 1 == px || c - 2 == px) && (r - 1 == py || r - 2 == py) ? 0 : 1;
                        if (v < 0) continue;
                        Put(value, mask, cols, rows, headX + Col(left + c, w, flip), headTop + 6 + r, v);
                    }
                }
            }
        }

        // the chest's little screen: four bars from the bands when he dances, a slow scan otherwise
        private void Equalizer(float[] value, float[] mask, int cols, int rows, int x, int y, ReadOnlySpan<float> bands, float time)
        {
            for (int c = 0; c < 4; c++)
            {
                float level = _mode == Mode.Dance && bands.Length >= 4 ? bands[c * bands.Length / 4]
                    : ((int)(time * 4) & 3) == c ? .4f : 0;
                int h = (int)MathF.Round(level * 3);
                for (int r = 0; r < 3; r++)
                {
                    Put(value, mask, cols, rows, x + c, y + 2 - r, r < h ? (r == h - 1 ? 1 : .6f) : 0);
                }
            }
        }

        private void Arm(float[] value, float[] mask, int cols, int rows, int sx, int sy, int side, float swing, bool up, float time)
        {
            int ex, ey, hx, hy;
            if (up)
            {
                int wave = _mode == Mode.Wave ? (int)MathF.Round(MathF.Sin(time * 12) * 1.5f) : 0;
                ex = sx + (side * 2);
                ey = sy - 3;
                hx = sx + (side * 2) + wave;
                hy = sy - 7;
            }
            else
            {
                ex = sx + side - (int)MathF.Round(swing * side * .5f);
                ey = sy + 3;
                hx = sx + side - (int)MathF.Round(swing * side);
                hy = sy + 6;
            }

            Limb(value, mask, cols, rows, sx, sy, ex, ey);
            Limb(value, mask, cols, rows, ex, ey, hx, hy);

            // a round glove
            for (int r = -1; r <= 1; r++)
            {
                for (int c = -1; c <= 1; c++)
                {
                    if (Math.Abs(r) + Math.Abs(c) < 2) Put(value, mask, cols, rows, hx + c, hy + r, r < 0 || c < 0 ? 1 : .8f);
                }
            }
        }

        private void Leg(float[] value, float[] mask, int cols, int rows, int lx, int hip, int foot, float stride, bool flip)
        {
            int kx = lx + (int)MathF.Round(stride), ky = hip + 3 - (stride > .3f ? 1 : 0);
            int fx = lx + (int)MathF.Round(stride * 2);
            Limb(value, mask, cols, rows, lx, hip, kx, ky);
            Limb(value, mask, cols, rows, kx, ky, fx, foot - 2);

            // a boot pointing the way he faces, light on top, a dark sole
            int toe = flip ? -1 : 1;
            for (int c = -1; c <= 2; c++)
            {
                Put(value, mask, cols, rows, fx + (c * toe), foot - 1, c == 2 ? .6f : .8f);
                Put(value, mask, cols, rows, fx + (c * toe), foot, .4f);
            }
        }

        // a limb two cells thick, lit on one side and shaded on the other
        private static void Limb(float[] value, float[] mask, int cols, int rows, int x0, int y0, int x1, int y1)
        {
            int n = Math.Max(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)), 1);
            for (int i = 0; i <= n; i++)
            {
                int x = x0 + (int)MathF.Round((x1 - x0) * i / (float)n), y = y0 + (int)MathF.Round((y1 - y0) * i / (float)n);
                Put(value, mask, cols, rows, x, y, .8f);
                Put(value, mask, cols, rows, x + 1, y, .5f);
            }
        }

        private static int Col(int c, int width, bool flip) => flip ? width - 1 - c : c;

        private static void Sprite(float[] value, float[] mask, int cols, int rows, string[] art, int x, int y, bool flip)
        {
            for (int r = 0; r < art.Length; r++)
            {
                string line = art[r];
                for (int c = 0; c < line.Length; c++)
                {
                    char ch = line[c];
                    if (ch == '.') continue;
                    Put(value, mask, cols, rows, x + Col(c, line.Length, flip), y + r, (ch - '0') / 5f);
                }
            }
        }

        // the robot covers what is behind him: every cell he draws is masked, dark ones included
        private static void Put(float[] value, float[] mask, int cols, int rows, int x, int y, float v)
        {
            if ((uint)x >= (uint)cols || (uint)y >= (uint)rows) return;
            int i = (y * cols) + x;
            value[i] = v;
            mask[i] = 1;
        }
    }
}
