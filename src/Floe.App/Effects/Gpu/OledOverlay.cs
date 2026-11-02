using Floe.Core.Visualization;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// What the OLED panel shows on top of the visualizer, drawn on the CPU straight into the panel's cells: the
/// song that plays (cover, title, artist, controls, progress) in the strip's left wing, and a pair of big
/// eyes that wander the panel and look about while nothing plays.
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
    private readonly Eyes _eyes = new();

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

        // the media lives in the left wing, from the bezel to just short of the dock
        int wingEnd = Math.Min(cols, (int)(dockLeft * cols) - 3);
        bool media = title.Length > 0 && wingEnd > 40;
        int mediaEnd = 0;
        if (media)
        {
            BuildCover(pixels, version, Math.Clamp(rows - 8, 4, 26));
            mediaEnd = DrawMedia(value, mask, cols, rows, wingEnd, title, artist, position, duration, playing, dt);
        }

        // the eyes keep off the song in the left wing, and leave while music plays
        _eyes.Update(dt, time, cols, media ? mediaEnd + FadeCells + 16 : 16, music: playing, _random);
        _eyes.Draw(value, mask, cols, rows, time);
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

    // ---- the eyes ---------------------------------------------------------------------------------

    /// <summary>
    /// A pair of big eyes that keep the quiet panel company: they open somewhere, drift along the strip, stop to
    /// look about and blink now and then. When music starts they shut and leave the panel to the show.
    /// </summary>
    private sealed class Eyes
    {
        private const float RadiusX = 5.5f, RadiusY = 7.5f, Apart = 8, Pupil = 2.4f;

        private bool _shown;
        private float _x, _toX, _open, _until, _lookUntil, _blink = -1;
        private float _lookX, _lookY, _lookToX, _lookToY;

        public void Update(float dt, float time, int cols, int minX, bool music, Random random)
        {
            int maxX = cols - 16;
            if (music || maxX <= minX)
            {
                // shut, then gone
                _open = MathF.Max(0, _open - (dt * 5));
                if (_open <= 0) _shown = false;
                return;
            }

            if (!_shown)
            {
                _shown = true;
                _x = _toX = minX + (random.NextSingle() * (maxX - minX));
                _until = time + 2;
            }

            _open = MathF.Min(1, _open + (dt * 3));
            _x = Math.Clamp(_x, minX, maxX);

            // drift to a new spot now and then, looking the way they go
            float gap = _toX - _x;
            if (MathF.Abs(gap) > .5f)
            {
                _x += MathF.Sign(gap) * MathF.Min(MathF.Abs(gap), dt * (6 + (MathF.Abs(gap) * .8f)));
                _lookToX = MathF.Sign(gap);
                _lookToY = 0;
            }
            else if (time > _until)
            {
                _toX = Math.Clamp(_x + ((random.NextSingle() - .5f) * 140), minX, maxX);
                _until = time + 4 + (6 * random.NextSingle());
            }
            else if (time > _lookUntil)
            {
                _lookToX = (random.NextSingle() * 2) - 1;
                _lookToY = ((random.NextSingle() * 2) - 1) * .8f;
                _lookUntil = time + .5f + (1.5f * random.NextSingle());
            }

            if (time - _blink > 2 && random.NextSingle() < dt * .35f) _blink = time;

            float ease = 1 - MathF.Exp(-dt * 10);
            _lookX += (_lookToX - _lookX) * ease;
            _lookY += (_lookToY - _lookY) * ease;
        }

        public void Draw(float[] value, float[] mask, int cols, int rows, float time)
        {
            if (!_shown) return;

            // a blink is a quick close and open; the lids also close the eyes as they come and go
            float since = time - _blink;
            float lid = since < .16f ? MathF.Abs((since / .08f) - 1) : 1;
            float open = MathF.Max(.08f, _open * lid);
            float cy = (rows - 1) / 2f;
            for (int e = -1; e <= 1; e += 2)
            {
                Eye(value, mask, cols, rows, _x + (e * Apart), cy, open);
            }
        }

        private void Eye(float[] value, float[] mask, int cols, int rows, float cx, float cy, float open)
        {
            float ry = RadiusY * open;
            float px = cx + (_lookX * (RadiusX - Pupil - .6f)), py = cy + (_lookY * (ry - Pupil - .6f));
            for (int y = (int)MathF.Floor(cy - RadiusY - 1); y <= (int)MathF.Ceiling(cy + RadiusY + 1); y++)
            {
                for (int x = (int)MathF.Floor(cx - RadiusX - 1); x <= (int)MathF.Ceiling(cx + RadiusX + 1); x++)
                {
                    if ((uint)x >= (uint)cols || (uint)y >= (uint)rows) continue;
                    float dx = (x - cx) / (RadiusX + 1), dy = (y - cy) / (ry + 1);
                    float d = (dx * dx) + (dy * dy);
                    if (d > 1) continue;
                    int i = (y * cols) + x;
                    mask[i] = 1;

                    // a black rim, then the white with a little shade at its lower edge, then the pupil
                    float inner = ((x - cx) / RadiusX * ((x - cx) / RadiusX)) + ((y - cy) / ry * ((y - cy) / ry));
                    if (inner > 1 || ry < 1.2f)
                    {
                        value[i] = ry < 1.2f && MathF.Abs(y - cy) < .6f && MathF.Abs(x - cx) <= RadiusX ? .8f : 0;
                        continue;
                    }

                    float ppx = x - px, ppy = y - py;
                    float pupil = (ppx * ppx) + (ppy * ppy);
                    if (pupil <= Pupil * Pupil)
                    {
                        bool glint = MathF.Round(ppx) == -1 && MathF.Round(ppy) == -1;
                        value[i] = glint ? 1 : 0;
                    }
                    else
                    {
                        value[i] = y - cy > ry * .55f ? .7f : 1;
                    }
                }
            }
        }
    }
}
