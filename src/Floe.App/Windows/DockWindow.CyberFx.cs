using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Floe.App.Effects.Cyber;
using Floe.App.Services;
using Floe.App.ViewModels;

namespace Floe.App.Windows;

public partial class DockWindow
{
    // What the cyber themes play instead of the glass: a click dumps the icon into its raw bytes, sleep loses the signal and rains away column by column, waking boots the bar back up.

    private const double FxRoom = 76;
    private const int SleepColumns = 40;
    private const string HexCells = "1C BD E9 55 7A FF 0F C4 E9 1C 55 BD";

    internal static readonly Color FxCyan = Color.FromRgb(0x02, 0xD7, 0xF2);
    internal static readonly Color FxRed = Color.FromRgb(0xFF, 0x00, 0x3C);
    private static readonly Color FxInk = Color.FromRgb(0x0A, 0x0A, 0x10);

    private readonly CyberFxLayer _fx = new();
    private CyberFxLayer.Run? _sleepRun;
    private DrawingBrush? _columnMask;
    private (Rect Capsule, Rect Bounds, long Shown) _columnMaskKey;
    private CyberFxLayer.Run? _titleRun;
    private DockItemViewModel? _noticed;
    private IconSlot? _fxHover;
    private double _fxHoverStart = double.NaN;
    private (ImageSource Icon, Point At)? _fxGhost;

    private const double CoverSquare = 56;
    private const double CoverChamfer = 8;
    private const double CoverSlide = .24;
    private CyberFxLayer.Run? _coverRun;
    private bool _coverGone;
    private bool _coverPending;

    private readonly DispatcherTimer _textGlitch = new();

    private double FxRoomAbove => _fx.Busy ? FxRoom : 0;

    // the dock is on its way out, gone, or still playing the sleep run: no effect may draw over it then
    private bool FxAsleep => _dozeHidden || _dozing || _sleepRun is not null;

    private void InitializeCyberFx()
    {
        _fx.RenderTransformOrigin = Root.RenderTransformOrigin;
        _fx.RenderTransform = _rootScale;
        Stage.Children.Add(_fx);
        _textGlitch.Tick += (_, _) => GlitchSomeText();
        NextTextGlitch();
        _textGlitch.Start();
        _media.LayoutUpdated += (_, _) => ResumeCoverIn();
    }

    private void NextTextGlitch() => _textGlitch.Interval = TimeSpan.FromSeconds(3 + (Random.Shared.NextDouble() * 4));

    private void GlitchSomeText()
    {
        NextTextGlitch();
        if (!GlassSettings.Cyber || !IsVisible || _dozeHidden || _sleepRun is not null || Root.OpacityMask is not null) return;
        if (_mediaTarget > 0 && _mediaTrackTitle.Length > 0 && Random.Shared.NextDouble() < .5) DecodeTitle(_mediaTrackTitle);
        else PlayClockSplit();
    }

    private Rect SlotBounds(IconSlot slot) => slot.Button.TransformToAncestor(Root).TransformBounds(new Rect(slot.Button.RenderSize));

    private Rect SlotFrameBounds(IconSlot slot)
    {
        var box = SlotBounds(slot);
        box.Inflate(6, 6);
        box.Height = Math.Min(box.Height, Bottom - 2 - box.Top);
        return box;
    }

    private void PlayCyberClick(DockItemViewModel item)
    {
        if (_slots.FirstOrDefault(s => ReferenceEquals(s.Item, item)) is not { } slot) return;
        PlayHexDump(slot);
    }

    // the icon turns cell by cell into its raw bytes, each in the colour of the pixels it stands for
    private void PlayHexDump(IconSlot slot)
    {
        double seconds = .52 / AuraSettings.ClickSpeed;
        const int columns = 5, rows = 6;
        if (slot.Item.Icon is not { } icon) return;
        var bytes = HexBrushes(icon, columns, rows);
        var ink = CyberFxLayer.Solid(FxInk);
        var unreadable = CyberFxLayer.Solid(FxRed);
        var seed = Environment.TickCount % 90;
        _fx.Play(seconds, (dc, p) =>
        {
            if (!Root.IsAncestorOf(slot.Button)) return;
            var box = SlotBounds(slot);
            double width = box.Width / columns, height = box.Height / rows, tick = Math.Floor(p * seconds * 16);
            double step = tick / (seconds * 16);
            double reach = 1.45 * (step < .12 ? step / .12 : Math.Pow(1 - ((step - .12) / .88), 1.4));
            for (var r = 0; r < rows; r++)
            {
                for (var c = 0; c < columns; c++)
                {
                    double cell = (c * 7) + (r * 13);
                    if (CyberFxLayer.Random(cell + seed) > reach) continue;
                    var at = new Rect(box.X + (c * width), box.Y + (r * height), width, height);
                    dc.DrawRectangle(ink, null, at);
                    bool lost = CyberFxLayer.Random(cell + seed + (tick * 31)) < .14;
                    var value = lost ? "??" : ((int)(CyberFxLayer.Random(cell + (tick * 17)) * 256)).ToString("X2");
                    _fx.Text(dc, value, at.X + (width / 2), at.Y - 1, height * .9, lost ? unreadable : bytes[(r * columns) + c], TextAlignment.Center);
                }
            }
        });
    }

    // the icon's colour under each cell; a cell over the transparent corners or a dark area reads in cyan
    private static Brush[] HexBrushes(ImageSource icon, int columns, int rows)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawImage(icon, new Rect(0, 0, columns, rows));
        var small = new RenderTargetBitmap(columns, rows, 96, 96, PixelFormats.Pbgra32);
        small.Render(visual);
        var pixels = new byte[columns * rows * 4];
        small.CopyPixels(pixels, columns * 4, 0);
        var cyan = CyberFxLayer.Solid(FxCyan);
        var brushes = new Brush[columns * rows];
        for (var i = 0; i < brushes.Length; i++)
        {
            byte b = pixels[i * 4], g = pixels[(i * 4) + 1], r = pixels[(i * 4) + 2], a = pixels[(i * 4) + 3];
            brushes[i] = a > 90 && r + g + b > 170 ? CyberFxLayer.Solid(Color.FromRgb(r, g, b)) : cyan;
        }

        return brushes;
    }

    private void SetFxHover(IconSlot? slot)
    {
        if (ReferenceEquals(_fxHover, slot)) return;
        _fxHover = slot;
        _fxHoverStart = double.NaN;
        UpdateFxOverlay();
    }

    private void UpdateFxOverlay() => _fx.Overlay = _fxHover is not null || _fxGhost is not null ? DrawFxOverlay : null;

    private void DrawFxOverlay(DrawingContext dc, double time)
    {
        // the app under the cursor can quit and take its icon with it
        if (_fxHover is { } gone && !Root.IsAncestorOf(gone.Button))
        {
            Dispatcher.BeginInvoke(() => SetFxHover(null));
        }
        else if (_fxHover is { } slot)
        {
            if (double.IsNaN(_fxHoverStart)) _fxHoverStart = time;
            DrawScanTag(dc, slot, time - _fxHoverStart);
            DrawTicker(dc, slot, time - _fxHoverStart);
        }

        if (_fxGhost is { } ghost)
        {
            var box = new Rect(ghost.At.X - 22, ghost.At.Y - 22, 44, 44);
            CyberFxLayer.Silhouette(dc, ghost.Icon, new Rect(box.X + 3, box.Y, 44, 44), CyberFxLayer.Solid(CyberLook.Current.Accent));
            dc.DrawImage(ghost.Icon, box);
            var dash = new Pen(CyberFxLayer.Solid(FxCyan), 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
            dc.DrawLine(dash, new Point(ghost.At.X, box.Bottom), new Point(ghost.At.X, RestTop + (RestBarHeight / 2)));
        }
    }

    // the item frame on the icon, a leader and a card that decodes the name; a running app opens its window preview
    // there instead, so it keeps the frame and the data strip
    private void DrawScanTag(DrawingContext dc, IconSlot slot, double age)
    {
        const double width = 164, height = 46, inset = 15;
        var look = CyberLook.Current;
        var accent = CyberFxLayer.Solid(look.Accent);
        var box = SlotFrameBounds(slot);
        CyberFxLayer.SlotFrame(dc, box, look.Accent, FxCyan, FxInk);
        if (slot.Item.Windows.Count > 0 || age < .08) return;

        bool flip = box.Right + 18 + width > _windowWidth - 4;
        double left = flip ? box.Left - 18 - width : box.Right + 18;
        var card = new Rect(left, Math.Max(2, box.Top - height - 8), width, height);
        dc.DrawLine(new Pen(accent, 1.5), new Point(flip ? box.Left : box.Right, box.Top), new Point(flip ? card.Right : card.Left, card.Bottom - 9));
        CyberFxLayer.ItemFrame(dc, card, look.Accent, FxCyan, FxInk);
        string name = slot.Item.Name.ToUpper(Loc.Culture);
        if (name.Length > 20) name = name[..20];
        _fx.Text(dc, CyberFxLayer.Decode(name, CyberFxLayer.Segment(age, .08, .42), Math.Floor(age * 22)), card.X + inset, card.Y + 5, 16, CyberFxLayer.Solid(look.Strip));
        _fx.Text(dc, Loc.T(slot.Item.IsPinned ? "SABİT // HAZIR" : "HAZIR"), card.X + inset, card.Y + 24, 12, CyberFxLayer.Solid(FxCyan));
    }

    // the data strip in the free room under the bar
    private void DrawTicker(DrawingContext dc, IconSlot slot, double age)
    {
        const double height = 11, glyph = 5.6, gap = 4;
        var band = new Rect(_capsuleLeft + 14, Bottom + gap, Math.Max(1, _capsuleWidth - 28), height);
        var count = slot.Item.Windows.Count;
        var state = count > 0 ? string.Format(Loc.T("ÇALIŞIYOR // {0} PENCERE"), count) : Loc.T("KAPALI");
        var text = $"// {slot.Item.Name.ToUpper(Loc.Culture)} // {state} //   ";
        var yellow = CyberFxLayer.Solid(CyberLook.Current.Strip);
        double offset = (age * 46) % (text.Length * glyph);
        dc.PushClip(new RectangleGeometry(band));
        dc.DrawRectangle(CyberFxLayer.Solid(FxInk), null, band);
        for (var x = band.X - offset; x < band.Right; x += text.Length * glyph) _fx.Text(dc, text, x, band.Y - 2, 11, yellow);
        dc.Pop();
    }

    private void SetFxGhost(IconSlot? slot, Point at)
    {
        if (slot is null || !GlassSettings.Cyber)
        {
            if (_fxGhost is null) return;
            _fxGhost = null;
            UpdateFxOverlay();
            return;
        }

        if (slot.Item.Icon is not { } icon) return;
        _fxGhost = (icon, at);
        UpdateFxOverlay();
    }

    private void NoteCyberAttention(DockItemViewModel? item)
    {
        if (ReferenceEquals(_noticed, item)) return;
        // nothing plays over a dock that is not on screen: drop the card and let a later alert play it after the wake
        if (item is not null && FxAsleep)
        {
            _noticed = null;
            return;
        }

        _noticed = item;
        if (item is null || !GlassSettings.Cyber) return;
        var ink = CyberFxLayer.Solid(FxInk);
        var yellow = CyberFxLayer.Solid(CyberLook.Current.Strip);
        var cyan = CyberFxLayer.Solid(FxCyan);
        var edge = new Pen(yellow, 1.5);
        var name = item.Name.ToUpper(Loc.Culture);
        _fx.Play(4, (dc, p) =>
        {
            const double width = 210, height = 40;
            double slide = 1 - Math.Pow(1 - CyberFxLayer.Segment(p, 0, .05), 3);
            double leave = CyberFxLayer.Segment(p, .94, 1);
            var box = new Rect(_capsuleLeft + 8 - ((1 - slide + leave) * (width + 16)), RestTop - height - 8, width, height);
            dc.PushClip(new RectangleGeometry(new Rect(_capsuleLeft, 0, width + 24, RestTop)));
            dc.DrawGeometry(ink, edge, CyberFxLayer.Plate(box, 7));
            dc.DrawRectangle(yellow, null, new Rect(box.X + 7, box.Y + 7, 4, height - 14));
            _fx.Text(dc, Loc.T("YENİ BİLDİRİM"), box.X + 18, box.Y + 3, 16, yellow);
            _fx.Text(dc, CyberFxLayer.Decode(name, CyberFxLayer.Segment(p, .05, .14), Math.Floor(p * 90)), box.X + 18, box.Y + 21, 12, cyan);
            dc.Pop();
        });
    }

    // the minute turns: the digits split into their channels and snap back
    private void PlayClockSplit()
    {
        var look = CyberLook.Current;
        var red = CyberFxLayer.Solid(look.Accent);
        var cyan = CyberFxLayer.Solid(FxCyan);
        _fx.Play(.5, (dc, p) =>
        {
            if (CyberFxLayer.Random(Math.Floor(p * 10) + 3) < .4 || !Root.IsAncestorOf(_clockTime)) return;
            var bounds = _clockTime.TransformToAncestor(Root).TransformBounds(new Rect(_clockTime.RenderSize));
            double middle = bounds.X + (bounds.Width / 2);
            _fx.Text(dc, _clockTime.Text, middle - 3, bounds.Y + 1, _clockTime.FontSize, red, TextAlignment.Center);
            _fx.Text(dc, _clockTime.Text, middle + 3, bounds.Y + 1, _clockTime.FontSize, cyan, TextAlignment.Center);
        }, inside: true);
    }

    private void DecodeTitle(string title)
    {
        _fx.Cancel(_titleRun);
        _titleRun = _fx.Play(.6, null, p => _mediaTitle.Text = CyberFxLayer.Decode(title, p, Math.Floor(p * 24)), () => _mediaTitle.Text = title, true);
    }

    // the section has no real position until the layout has placed it; before that the transform reports the window
    // origin, so anything drawn from it would land as a loose square at the dock's upper-left
    private bool MediaPlaced() => !double.IsNaN(Canvas.GetLeft(_media)) && !double.IsNaN(Canvas.GetTop(_media));

    private Rect? CoverRect()
    {
        if (_media.Visibility != Visibility.Visible || !Root.IsAncestorOf(_media) || !MediaPlaced()) return null;
        var corner = _media.TransformToAncestor(Root).Transform(new Point(0, -8));
        return new Rect(corner.X, corner.Y, CoverSquare, CoverSquare);
    }

    // the cover comes through block by block: a block covered in blank, then a flash of colour, then the picture
    private void DrawCoverBlocks(DrawingContext dc, double p, Brush blank, Brush? spark = null)
    {
        const int cells = 6;
        if (CoverRect() is not { } box) return;
        var look = CyberLook.Current;
        var red = CyberFxLayer.Solid(look.Accent);
        var cyan = spark ?? CyberFxLayer.Solid(FxCyan);
        double cell = box.Width / cells;
        dc.PushClip(CyberFxLayer.Plate(box, CoverChamfer));
        for (var c = 0; c < cells; c++)
        {
            for (var r = 0; r < cells; r++)
            {
                double settle = .25 + (CyberFxLayer.Random((c * cells) + r + .5) * .7);
                if (p >= settle) continue;
                var fill = p < settle - .12 ? blank : CyberFxLayer.Random(c + (r * cells) + .2) < .6 ? red : cyan;
                dc.DrawRectangle(fill, null, new Rect(box.X + (c * cell), box.Y + (r * cell), cell + .5, cell + .5));
            }
        }

        dc.Pop();
    }

    private void PlayCoverIn()
    {
        _coverGone = false;
        // the wipe waits for the first layout instead of drawing where the unplaced section sits
        if (!MediaPlaced())
        {
            _coverPending = true;
            return;
        }

        PlayCoverNow();
    }

    // the section has been placed: a wipe held back at startup plays now
    private void ResumeCoverIn()
    {
        if (!_coverPending) return;
        if (_coverGone || _mediaTarget == 0)
        {
            _coverPending = false;
            return;
        }

        if (!MediaPlaced()) return;
        _coverPending = false;
        PlayCoverNow();
    }

    private void PlayCoverNow()
    {
        var body = CyberFxLayer.Solid(CyberLook.Current.Body);
        _fx.Play(.62, (dc, p) => DrawCoverBlocks(dc, p, body));
        if (_mediaTrackTitle.Length > 0) DecodeTitle(_mediaTrackTitle);
    }

    private void PlayCoverSwap()
    {
        if (_mediaTarget == 0) return;
        var ink = CyberFxLayer.Solid(FxInk);
        _fx.Play(.42, (dc, p) => DrawCoverBlocks(dc, p, ink));
    }

    // the section only closes once the cover has broken up
    private void PlayCoverOut()
    {
        if (_coverRun is not null) return;
        _coverPending = false;
        var body = CyberFxLayer.Solid(CyberLook.Current.Body);
        _coverRun = _fx.Play(.4, (dc, p) => DrawCoverBlocks(dc, 1 - p, body, CyberFxLayer.Solid(FxInk)), null, () =>
        {
            _coverRun = null;
            _coverGone = true;
            _mediaTarget = 0;
            EnsureAnimation();
        });
    }

    private void CancelCoverOut()
    {
        _fx.Cancel(_coverRun);
        _coverRun = null;
        _coverGone = false;
    }

    // sleep is a relic malfunction: the bar tears, strobes black and red, blocks eat it, and it rains away in red code
    private void CyberSleep()
    {
        string text = Loc.T("KRİTİK SİSTEM HATASI");
        var look = CyberLook.Current;
        var rect = CapsuleRect();
        var red = CyberFxLayer.Solid(look.Accent);
        var ink = CyberFxLayer.Solid(FxInk);
        var edge = new Pen(red, 2);
        var warning = new PathGeometry(new[]
        {
            new PathFigure(new Point(0, 26), new[] { new PolyLineSegment(new[] { new Point(15, 0), new Point(30, 26) }, true) }, true),
        });
        var cells = HexCells.Replace(" ", string.Empty);
        double seconds = 2.3 / AuraSettings.DerezzSpeed;
        double Drop(int column) => .5 + (CyberFxLayer.Random(column * 2.3) * .3);

        _sleepRun = _fx.Play(seconds, (dc, p) =>
        {
            double tick = Math.Floor(p * 2.3 * 18);

            // the blackout: every few ticks the whole bar drops to black with red scanlines, then ever more often
            if (p < .62 && tick % (p < .3 ? 5 : 2) == 0)
            {
                dc.DrawRectangle(ink, null, rect);
                for (double y = rect.Top + 2; y < rect.Bottom; y += 4) dc.DrawRectangle(red, null, new Rect(rect.Left, y, rect.Width, 1.5));

                // the failure screen's read-outs: a barcode at one end, stand-by at the other
                var code = new Rect(rect.Left + 26, rect.Top + 9, 170, rect.Height - 18);
                dc.DrawRectangle(ink, edge, code);
                for (double x = code.Left + 5; x < code.Right - 7; x += 7)
                {
                    dc.DrawRectangle(red, null, new Rect(x, code.Top + 5, 1.5 + (CyberFxLayer.Random(x) * 4), code.Height - 10));
                }

                var standby = new Rect(rect.Right - 176, rect.Top + 14, 150, rect.Height - 28);
                dc.DrawRectangle(ink, edge, standby);
                _fx.Text(dc, Loc.T("BEKLEMEDE"), standby.X + (standby.Width / 2), standby.Y + ((standby.Height - 26) / 2), 20, red, TextAlignment.Center);
            }

            if (p < .72)
            {
                var frame = rect;
                frame.Inflate(7, 7);
                CyberFxLayer.Corners(dc, frame, 18, edge);
            }

            // the tear: slices of the bar thrown sideways, trailing red and black
            double strength = CyberFxLayer.Segment(p, 0, .1);
            for (var k = 0; k < 9 && p < .8; k++)
            {
                double seed = (tick * 7.3) + k;
                if (CyberFxLayer.Random(seed) < .3) continue;
                double top = rect.Top - 10 + (CyberFxLayer.Random(seed + .1) * (rect.Height + 14));
                double height = 3 + (CyberFxLayer.Random(seed + .2) * 15);
                double shift = (CyberFxLayer.Random(seed + .3) - .5) * 170 * strength;
                double from = rect.Left + (CyberFxLayer.Random(seed + .4) * rect.Width * .7);
                double width = 90 + (CyberFxLayer.Random(seed + .5) * rect.Width * .4);
                dc.DrawRectangle(k % 3 == 0 ? ink : red, null, new Rect(from + shift, top, width, height));
            }

            // the blocks: black and red, more of them as it goes
            for (var b = 0; b < 46; b++)
            {
                if (p < .26 + (CyberFxLayer.Random(b + .7) * .36) || p > .84) continue;
                double x = rect.Left + (CyberFxLayer.Random(b + .1) * (rect.Width - 60));
                double y = rect.Top + (Math.Floor(CyberFxLayer.Random(b + .2) * 5) * rect.Height / 5);
                double width = 40 + (CyberFxLayer.Random(b + .3) * 150);
                dc.DrawRectangle(b % 3 == 1 ? red : ink, null, new Rect(x, y, width, rect.Height / 5));
            }

            if (p < .72)
            {
                double jitter = (CyberFxLayer.Random(tick + .9) - .5) * 8;
                var card = new Rect(rect.Left + (rect.Width / 2) - 160 + jitter, Math.Max(1, RestTop - 56), 320, 50);
                dc.DrawGeometry(ink, edge, CyberFxLayer.Plate(card, 8));

                dc.PushTransform(new TranslateTransform(card.X + 12, card.Y + 11));
                dc.DrawGeometry(null, edge, warning);
                _fx.Text(dc, "!", 15, 8, 16, red, TextAlignment.Center);
                dc.Pop();

                var bar = new Rect(card.X + 52, card.Y + 7, card.Width - 62, 23);
                dc.DrawRectangle(red, null, bar);
                string typed = CyberFxLayer.Decode(text, CyberFxLayer.Segment(p, .04, .3), tick);
                _fx.Text(dc, typed, bar.X + (bar.Width / 2), bar.Y - 2, 21, ink, TextAlignment.Center);
                _fx.Text(dc, Loc.T("RELIC ARIZASI // BAĞLANTI KESİLDİ"), bar.X + (bar.Width / 2), bar.Bottom + 1, 12, red, TextAlignment.Center);
            }

            double step = rect.Width / SleepColumns;
            for (var c = 0; c < SleepColumns; c++)
            {
                double gone = p - Drop(c);
                if (gone < 0 || gone > .2) continue;
                for (var j = 0; j < 6; j++)
                {
                    double y = rect.Top + (gone * 460) + (j * 13) - 30;
                    if (y < rect.Top - 6 || y > Bottom - 10) continue;
                    _fx.Text(dc, cells[(c + j) % cells.Length].ToString(), rect.Left + (c * step), y, 15, red);
                }
            }
        }, p =>
        {
            if (p >= .5) Root.OpacityMask = ColumnMask(rect, c => p < Drop(c), SleepColumns);
        }, () =>
        {
            _sleepRun = null;
            HideDozedDock();
            SinkBelowVisualizer();
            if (_dozePreview) CyberWake();
        });
    }

    private void CyberWake()
    {
        if (_sleepRun is not null)
        {
            _fx.Cancel(_sleepRun);
            _sleepRun = null;
            Root.OpacityMask = null;
            return;
        }

        if (!_dozeHidden) return;
        _dozeHidden = false;
        Root.Opacity = 1;
        // the relic comes back: a black bar is traced from the middle, code rains up into it while the reboot card
        // counts, and a bright front opens the dock from the middle out
        var look = CyberLook.Current;
        var rect = CapsuleRect();
        var red = CyberFxLayer.Solid(look.Accent);
        var body = CyberFxLayer.Solid(look.Body);
        var cyan = CyberFxLayer.Solid(FxCyan);
        var ink = CyberFxLayer.Solid(FxInk);
        var edge = new Pen(body, 2);
        var cells = HexCells.Replace(" ", string.Empty);
        const double wakeSeconds = 1.7;
        string text = Loc.T("SİSTEM YENİDEN BAŞLATILIYOR");
        double middle = rect.Left + (rect.Width / 2);
        double Open(double p) => CyberFxLayer.Segment(p, .52, .9);

        _fx.Play(wakeSeconds / AuraSettings.DerezzSpeed, (dc, p) =>
        {
            double tick = Math.Floor(p * wakeSeconds * 18);
            double trace = CyberFxLayer.Segment(p, 0, .16) * rect.Width / 2;
            double open = Open(p) * ((rect.Width / 2) + 24);

            // the black bar, whatever of it the dock has not taken back yet
            if (p > .1 && p < .92)
            {
                double fill = CyberFxLayer.Segment(p, .1, .22) * rect.Height;
                var plate = new Rect(rect.Left, rect.Bottom - fill, rect.Width, fill);
                var hole = new RectangleGeometry(new Rect(middle - open, 0, open * 2, Bottom + 40));
                dc.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(plate), hole));
                dc.DrawRectangle(ink, null, plate);
                for (double y = rect.Top + 2; y < rect.Bottom; y += 4)
                {
                    dc.DrawRectangle(red, null, new Rect(rect.Left, y, rect.Width, tick % 3 == 0 ? 1.5 : .75));
                }

                // code rises into place, column by column
                double step = rect.Width / SleepColumns;
                for (var c = 0; c < SleepColumns; c++)
                {
                    double start = .14 + (CyberFxLayer.Random(c * 1.7) * .2);
                    double run = CyberFxLayer.Segment(p, start, start + .22);
                    if (run <= 0 || run >= 1) continue;
                    for (var j = 0; j < 5; j++)
                    {
                        double y = rect.Bottom - (run * (rect.Height + 40)) + (j * 13);
                        if (y < rect.Top - 2 || y > rect.Bottom - 12) continue;
                        _fx.Text(dc, cells[(c + j + (int)tick) % cells.Length].ToString(), rect.Left + (c * step), y, 14, j == 0 ? body : red);
                    }
                }

                dc.Pop();
            }

            // the trace runs out both ways along the bar and leaves brackets on its corners
            if (p < .2)
            {
                dc.DrawLine(edge, new Point(middle - trace, rect.Top), new Point(middle + trace, rect.Top));
                dc.DrawLine(edge, new Point(middle - trace, rect.Bottom), new Point(middle + trace, rect.Bottom));
            }
            else if (p < .9)
            {
                var frame = rect;
                frame.Inflate(7, 7);
                CyberFxLayer.Corners(dc, frame, 18, edge);
            }

            // the opening front, one bright edge with a chroma shadow each way
            if (p > .5 && p < .92)
            {
                foreach (var side in new[] { -1, 1 })
                {
                    double x = middle + (side * open);
                    dc.DrawRectangle(cyan, null, new Rect(x + (side * 4), rect.Top - 10, 3, rect.Height + 20));
                    dc.DrawRectangle(body, null, new Rect(x - 1, rect.Top - 14, 3, rect.Height + 28));
                }
            }

            // the reboot card
            if (p < .84)
            {
                double jitter = p < .3 ? (CyberFxLayer.Random(tick + .4) - .5) * 6 : 0;
                var card = new Rect(middle - 160 + jitter, Math.Max(1, RestTop - 56), 320, 50);
                dc.DrawGeometry(ink, edge, CyberFxLayer.Plate(card, 8));
                string typed = CyberFxLayer.Decode(text, CyberFxLayer.Segment(p, .03, .32), tick);
                _fx.Text(dc, typed, middle, card.Y + 5, 19, body, TextAlignment.Center);
                var track = new Rect(card.X + 14, card.Bottom - 15, card.Width - 28, 7);
                double load = Math.Floor(CyberFxLayer.Segment(p, .08, .78) * 24) / 24;
                dc.DrawRectangle(null, new Pen(body, 1), track);
                dc.DrawRectangle(body, null, new Rect(track.X + 1, track.Y + 1, (track.Width - 2) * load, track.Height - 2));
            }

            // the last two blinks of a picture that has just locked on
            if (p > .92 && tick % 2 == 0)
            {
                dc.DrawRectangle(cyan, null, new Rect(rect.Left - 3, rect.Top + 18, rect.Width * .45, 3));
                dc.DrawRectangle(red, null, new Rect(rect.Left + (rect.Width * .5), rect.Bottom - 22, rect.Width * .5, 3));
            }
        }, p =>
        {
            double open = Open(p) * SleepColumns / 2;
            Root.OpacityMask = ColumnMask(rect, c => Math.Abs(c - ((SleepColumns - 1) / 2.0)) < open, SleepColumns);
        }, () =>
        {
            Root.OpacityMask = null;
            _dozePreview = false;
        });
    }

    // shows the columns of the bar that are still there; the shadow around it goes with them
    private DrawingBrush ColumnMask(Rect rect, Func<int, bool> shown, int columns)
    {
        var bounds = new Rect(0, 0, Math.Max(1, Root.ActualWidth), Math.Max(1, Root.ActualHeight));
        long mask = 0;
        for (var c = 0; c < columns; c++)
        {
            if (shown(c)) mask |= 1L << c;
        }

        // the same columns in the same place draw the same brush, and sleep/wake ask for one per frame
        var key = (rect, bounds, mask);
        if (_columnMask is not null && key == _columnMaskKey) return _columnMask;
        _columnMaskKey = key;

        var group = new GeometryGroup();
        double width = (rect.Width + 48) / columns;
        for (var c = 0; c < columns; c++)
        {
            if ((mask & (1L << c)) == 0) continue;
            var first = c; // neighbours go in as one rectangle, or their shared edge shows as a seam
            while (c + 1 < columns && (mask & (1L << (c + 1))) != 0) c++;
            group.Children.Add(new RectangleGeometry(new Rect(rect.Left - 24 + (first * width), 0, (c - first + 1) * width, bounds.Height)));
        }

        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(bounds)));
        drawing.Children.Add(new GeometryDrawing(Brushes.White, null, group));
        var brush = new DrawingBrush(drawing)
        {
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = bounds,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = bounds,
            Stretch = Stretch.Fill,
        };
        brush.Freeze();
        _columnMask = brush;
        return brush;
    }

    private void PreviewMedia()
    {
        var pixels = new byte[64 * 64 * 4];
        for (var i = 0; i < 64 * 64; i++)
        {
            pixels[(i * 4) + 0] = (byte)(i % 64 * 4);
            pixels[(i * 4) + 1] = 40;
            pixels[(i * 4) + 2] = (byte)(i / 64 * 4);
            pixels[(i * 4) + 3] = 255;
        }

        _mediaArt.Source = System.Windows.Media.Imaging.BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4);
        _mediaTitle.Text = _mediaTrackTitle = "Lucky You";
        _mediaArtist.Text = "Deftones";
        _mediaProgress.ScaleX = .4;
        _mediaPlaying = true;
        ApplyCyberMedia();
        _mediaReveal = _mediaTarget = 1;
        _restCapsuleWidth = LeadX + _restContent + TrailWidth(1);
        _restCapsuleLeft = (_windowWidth - _restCapsuleWidth) / 2;
        ApplyLayout(_slots.Select(_ => 1.0).ToList());
        UpdateLayout();
    }

    /// <summary>One still of an effect, for --render with --fx=name:progress.</summary>
    internal void PreviewCyberFx(string name, double progress)
    {
        var slot = _slots.Count > 2 ? _slots[2] : _slots.FirstOrDefault();
        if (slot is null) return;
        switch (name)
        {
            case "hex": PlayHexDump(slot); break;
            case "hover":
                SetFxHover(slot);
                PlaceSlotFill(slot);
                slot.LineBrush.Color = CyberLook.Readable(CyberLook.Current.Accent, FxInk);
                _fxHoverStart = -1;
                break;
            case "notice": NoteCyberAttention(slot.Item); break;
            case "clock": PlayClockSplit(); break;
            case "sleep": CyberSleep(); break;
            case "wake": _dozeHidden = true; CyberWake(); break;
            case "relic": PlayRelic(); break;
            case "ghost": SetFxGhost(slot, new Point(slot.Center + 90, RestTop - 26)); break;
            case "coverin": PreviewMedia(); PlayCoverIn(); break;
            case "coverswap": PreviewMedia(); PlayCoverSwap(); break;
            case "coverout": PreviewMedia(); PlayCoverOut(); break;
        }

        _fx.Seek(progress);
    }
}
