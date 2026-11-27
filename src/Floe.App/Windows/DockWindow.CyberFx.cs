using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Floe.App.Effects.Cyber;
using Floe.App.Services;
using Floe.App.ViewModels;
using Floe.Core.Services;

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
    private double _fxCursorX;
    private double _fxCursorY;
    private IconSlot? _fxSettleSlot;
    private double _fxSettleFromAge;
    private double _fxSettleCursorX;
    private double _fxSettleCursorY;
    private CyberFxLayer.Run? _fxSettleRun;
    private IconSlot? _glitchSlot;
    private CyberFxLayer.Run? _glitchRun;
    private UIElement? _glitchHidden;
    private CyberFxLayer.Run? _warpRun;
    private (ImageSource Icon, ImageBrush? Mask, Point At)? _fxGhost;

    private const double CoverSquare = 56;
    private const double CoverChamfer = 8;
    private const double CoverSlide = .24;
    private CyberFxLayer.Run? _coverRun;
    private bool _coverGone;
    private bool _coverPending;

    private readonly DispatcherTimer _textGlitch = new();

    private double FxRoomAbove => _fx.Busy ? FxRoom : 0;

    // the dock is on its way out, gone, or still playing the sleep run: no effect may draw over it then
    private bool FxAsleep => _dozeHidden || _dozing || _sleepRun is not null || _particleRun is not null || _particleHiding || _particleHidden;

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
        PressDetent(slot);
        if (AuraSettings.CyberClick == CyberClickStyle.Hex)
        {
            PlayHexDump(slot);
            PlayCyberClickRing(slot);
        }
        else
        {
            PlayGlitchClick(slot, AuraSettings.CyberClick);
        }
    }

    private void StopGlitchClick()
    {
        if (_glitchRun is { } run)
        {
            _glitchRun = null;
            _fx.Cancel(run);
        }

        if (_glitchHidden is not null) _glitchHidden.Opacity = 1;
        _glitchHidden = null;
        _glitchSlot = null;
    }

    private void PlayGlitchClick(IconSlot slot, CyberClickStyle style)
    {
        StopGlitchClick();
        if (FxAsleep || slot.Button.Content is not Grid { Children: [Image artwork, ..] } grid) return;

        bool isCard = slot.Card is not null;
        ImageSource? snapshot = isCard
            ? slot.Card!.Value.OnFrame
            : slot.Item.Icon is { } icon && artwork.Visibility == Visibility.Visible
                ? icon
                : null;
        if (snapshot is null)
        {
            PlayHexDump(slot);
            PlayCyberClickRing(slot);
            return;
        }

        Brush?[]? grid48 = style == CyberClickStyle.Datamosh ? CyberGlitchClick.Sample(snapshot, 48) : null;
        if (style == CyberClickStyle.BitCrush)
        {
            foreach (int n in new[] { 4, 8, 12, 16, 24 })
            {
                CyberGlitchClick.Sample(snapshot, n);
            }
        }

        grid.Opacity = 0;
        _glitchHidden = grid;
        _glitchSlot = slot;

        double baseSeconds = style == CyberClickStyle.Datamosh ? 0.82 : 0.80;
        double seconds = baseSeconds / AuraSettings.ClickSpeed;
        _glitchRun = _fx.Play(seconds, (dc, p) =>
        {
            if (!Root.IsAncestorOf(slot.Button)) return;
            try
            {
                var box = SlotBounds(slot);
                var at = isCard
                    ? Rect.Inflate(box, CardBleed * (box.Width / IconBox), CardBleed * (box.Width / IconBox))
                    : box;
                bool popped = PushUprightAbout(dc, at);
                switch (style)
                {
                    case CyberClickStyle.Datamosh:
                        CyberGlitchClick.DrawDatamosh(dc, snapshot, grid48!, at, p);
                        break;
                    case CyberClickStyle.BitCrush:
                        CyberGlitchClick.DrawBitCrush(dc, snapshot, n => CyberGlitchClick.Sample(snapshot, n), at, p);
                        break;
                    default:
                        CyberGlitchClick.Draw(dc, snapshot, at, SlotFrameBounds(slot), p, FxCyan, FxRed, FxInk, (ctx, txt, x, y, sz, br) => _fx.Text(ctx, txt, x, y, sz, br));
                        break;
                }
                if (popped) dc.Pop();
            }
            catch (InvalidOperationException)
            {
                // Visual disconnected mid-frame
            }
        }, done: StopGlitchClick);
    }

    private void PlayCyberClickRing(IconSlot slot)
    {
        _fx.Play(0.36 / Math.Max(0.7, AuraSettings.ClickSpeed), (dc, p) =>
        {
            if (!Root.IsAncestorOf(slot.Button)) return;
            var look = CyberLook.Current;
            CyberLaunchEffect.DrawClickRing(dc, SlotFrameBounds(slot), p, CyberLook.Readable(look.Accent, FxInk), FxCyan);
        });
    }

    private void PlayWindowWarp(IconSlot slot, bool forward, int windowIndex, int windowCount)
    {
        if (FxAsleep) return;
        if (_warpRun is { } previous)
        {
            _warpRun = null;
            _fx.Cancel(previous);
        }

        PressDetent(slot);
        bool isCard = slot.Card is not null;
        ImageSource? icon = isCard ? slot.Card!.Value.OnFrame : slot.Item.Icon;
        double dir = forward ? 1.0 : -1.0;

        _warpRun = _fx.Play(0.44, (dc, p) =>
        {
            if (!Root.IsAncestorOf(slot.Button)) return;
            try
            {
                var box = SlotBounds(slot);
                var at = isCard
                    ? Rect.Inflate(box, CardBleed * (box.Width / IconBox), CardBleed * (box.Width / IconBox))
                    : box;
                double cx = box.X + (box.Width * 0.5);
                double cy = box.Y + (box.Height * 0.5);
                var look = CyberLook.Current;
                var accent = CyberLook.Readable(look.Accent, FxInk);
                if (accent == FxCyan) accent = Color.FromRgb(0xFC, 0xEE, 0x0A);

                double flare = p < 0.22 ? p / 0.22 : Math.Pow(1.0 - ((p - 0.22) / 0.78), 1.65);

                // 1) Teleporting radial bloom halo (white-hot core -> electric cyan -> transparent)
                if (flare > 0.01)
                {
                    double bloomR = box.Width * (0.68 + (0.48 * Math.Sin(p * Math.PI * 0.75)));
                    var bloom = new RadialGradientBrush(new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb((byte)(230 * flare), 0xF2, 0xFF, 0xFF), 0.0),
                        new GradientStop(Color.FromArgb((byte)(175 * flare), FxCyan.R, FxCyan.G, FxCyan.B), 0.36),
                        new GradientStop(Color.FromArgb((byte)(70 * flare), accent.R, accent.G, accent.B), 0.68),
                        new GradientStop(Color.FromArgb(0, FxCyan.R, FxCyan.G, FxCyan.B), 1.0),
                    });
                    bloom.Freeze();
                    dc.DrawEllipse(bloom, null, new Point(cx, cy), bloomR, bloomR);
                }

                // 2) Directional teleport silhouette streak (forward = right/up, backward = left/down)
                if (icon is not null && p < 0.78)
                {
                    double gp = p / 0.78;
                    double eased = 1.0 - Math.Pow(1.0 - gp, 3.0);
                    double gAlpha = Math.Pow(1.0 - gp, 1.4) * 0.85;
                    var ghostRect = at;
                    ghostRect.Offset(dir * 16.0 * eased, -5.0 * Math.Sin(gp * Math.PI));
                    var ghostBrush = CyberFxLayer.Solid(Color.FromArgb((byte)(255 * gAlpha), FxCyan.R, FxCyan.G, FxCyan.B));
                    CyberFxLayer.Silhouette(dc, icon, ghostRect, ghostBrush);
                }

                // 3) Vertical teleport energy beam cutting through the icon
                if (p < 0.56)
                {
                    double bp = p / 0.56;
                    double beamFade = Math.Sin(bp * Math.PI);
                    double beamX = cx + (dir * (bp - 0.35) * box.Width * 0.85);
                    double beamH = box.Height * (1.15 + (0.55 * beamFade));
                    double outerW = 10.0 * beamFade;
                    double coreW = 2.6 * beamFade;
                    var outerBrush = new LinearGradientBrush(
                        Color.FromArgb(0, FxCyan.R, FxCyan.G, FxCyan.B),
                        Color.FromArgb((byte)(185 * beamFade), FxCyan.R, FxCyan.G, FxCyan.B),
                        new Point(0.5, 0), new Point(0.5, 1))
                    {
                        GradientStops =
                        {
                            new GradientStop(Color.FromArgb(0, FxCyan.R, FxCyan.G, FxCyan.B), 0.0),
                            new GradientStop(Color.FromArgb((byte)(190 * beamFade), FxCyan.R, FxCyan.G, FxCyan.B), 0.5),
                            new GradientStop(Color.FromArgb(0, FxCyan.R, FxCyan.G, FxCyan.B), 1.0),
                        }
                    };
                    outerBrush.Freeze();
                    dc.DrawRectangle(outerBrush, null, new Rect(beamX - (outerW * 0.5), cy - (beamH * 0.5), outerW, beamH));
                    dc.DrawRectangle(CyberFxLayer.Solid(Color.FromArgb((byte)(245 * beamFade), 0xFF, 0xFF, 0xFF)), null, new Rect(beamX - (coreW * 0.5), cy - (beamH * 0.42), coreW, beamH * 0.84));
                }

                // 4) Crisp window index pips along top of slot so you know which window is now active
                if (windowCount > 1 && windowIndex >= 0)
                {
                    int shown = Math.Min(windowCount, 6);
                    int activePip = windowIndex % shown;
                    double pipFade = p < 0.72 ? 1.0 : (1.0 - ((p - 0.72) / 0.28));
                    const double pipW = 8.0, pipH = 3.5, gap = 3.0;
                    double totalW = (shown * pipW) + ((shown - 1) * gap);
                    double startX = cx - (totalW * 0.5);
                    double pipY = box.Top - 9.0;
                    for (int i = 0; i < shown; i++)
                    {
                        bool active = i == activePip;
                        double px = startX + (i * (pipW + gap));
                        var bg = CyberFxLayer.Solid(Color.FromArgb((byte)(210 * pipFade), FxInk.R, FxInk.G, FxInk.B));
                        dc.DrawRectangle(bg, null, new Rect(px - 1, pipY - 1, pipW + 2, pipH + 2));
                        var fg = active
                            ? CyberFxLayer.Solid(Color.FromArgb((byte)(255 * pipFade), 0xE8, 0xFF, 0xFF))
                            : CyberFxLayer.Solid(Color.FromArgb((byte)(115 * pipFade), FxCyan.R, FxCyan.G, FxCyan.B));
                        dc.DrawRectangle(fg, null, new Rect(px, pipY, pipW, pipH));
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // Visual disconnected mid-frame
            }
        }, done: () => _warpRun = null);
    }

    private void PlayCyberLaunch(IconSlot slot)
    {
        PressDetent(slot);
        if (FxAsleep || NoFx.Contains("click", StringComparison.OrdinalIgnoreCase)) return;

        double seconds = 0.78 / Math.Max(0.6, Math.Sqrt(AuraSettings.ClickSpeed));
        _fx.Play(seconds, (dc, p) =>
        {
            if (!Root.IsAncestorOf(slot.Button)) return;
            var look = CyberLook.Current;
            CyberLaunchEffect.DrawLaunch(
                dc,
                SlotFrameBounds(slot),
                _windowWidth,
                p,
                CyberLook.Readable(look.Accent, FxInk),
                FxCyan,
                look.Strip,
                FxInk,
                (ctx, txt, x, y, sz, br) => _fx.Text(ctx, txt, x, y, sz, br));
        });
    }

    private static void PressDetent(IconSlot slot)
    {
        var press = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(165) };
        var easeOut = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        press.KeyFrames.Add(new EasingDoubleKeyFrame(0.89, KeyTime.FromPercent(0.32), easeOut));
        press.KeyFrames.Add(new EasingDoubleKeyFrame(1.02, KeyTime.FromPercent(0.74), easeOut));
        press.KeyFrames.Add(new EasingDoubleKeyFrame(1.00, KeyTime.FromPercent(1.00)));
        slot.PressScale.BeginAnimation(ScaleTransform.ScaleXProperty, press);
        slot.PressScale.BeginAnimation(ScaleTransform.ScaleYProperty, press);
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
        var prev = _fxHover;
        double prevAge = double.IsNaN(_fxHoverStart) ? 0 : Math.Max(0, _fx.Time - _fxHoverStart);
        double prevCx = _fxCursorX;
        double prevCy = _fxCursorY;

        CancelFxSettle(restoreArtwork: true);

        _fxHover = slot;
        _fxHoverStart = double.NaN;
        _fxCursorX = 0;
        _fxCursorY = 0;

        if (prev is { Card: not null } && GlassSettings.Cyber && !FxAsleep && !_isDragging && Root.IsAncestorOf(prev.Button) && prevAge > 0.03)
        {
            _fxSettleSlot = prev;
            _fxSettleFromAge = prevAge;
            _fxSettleCursorX = prevCx;
            _fxSettleCursorY = prevCy;
            _fxSettleRun = _fx.Play(0.18, draw: null, tick: null, done: () => CancelFxSettle(restoreArtwork: true), inside: true);
        }

        UpdateFxOverlay();
    }

    private void CancelFxSettle(bool restoreArtwork = true)
    {
        var run = _fxSettleRun;
        var settling = _fxSettleSlot;
        _fxSettleRun = null;
        _fxSettleSlot = null;
        if (run is not null) _fx.Cancel(run);
        if (restoreArtwork && settling is not null && !ReferenceEquals(settling, _fxHover))
        {
            RestoreCyberSlotArtwork(settling);
        }

        UpdateFxOverlay();
    }

    private void UpdateFxOverlay()
    {
        _fx.Underlay = (_fxHover is { Card: not null } || _fxSettleSlot is { Card: not null }) && !FxAsleep ? DrawFxUnderlay : null;
        _fx.Overlay = _fxHover is not null || _fxGhost is not null ? DrawFxOverlay : null;
    }

    private void DrawFxUnderlay(DrawingContext dc, double time)
    {
        if (FxAsleep) return;

        if (_fxSettleSlot is { } settling && _fxSettleRun is { } run && Root.IsAncestorOf(settling.Button) && !ReferenceEquals(settling, _glitchSlot))
        {
            double p = Math.Clamp(run.Elapsed / Math.Max(0.01, run.Seconds), 0, 1);
            DrawSlotAnimatedLogo(dc, settling, isSettling: true, _fxSettleFromAge + run.Elapsed, _fxSettleCursorX, _fxSettleCursorY, p);
        }

        if (_fxHover is not { } slot || !Root.IsAncestorOf(slot.Button)) return;
        if (double.IsNaN(_fxHoverStart)) _fxHoverStart = time;

        double age = Math.Max(0, time - _fxHoverStart);
        UpdateHoverCursorParallax(slot);
        if (!ReferenceEquals(slot, _glitchSlot))
        {
            DrawSlotAnimatedLogo(dc, slot, isSettling: false, age, _fxCursorX, _fxCursorY, settleProgress: 0);
        }
    }

    private void UpdateHoverCursorParallax(IconSlot slot)
    {
        if (IsMouseOver)
        {
            var pt = Mouse.GetPosition(Root);
            var box = SlotBounds(slot);
            double rawCx = Math.Clamp((pt.X - (box.X + (box.Width * 0.5))) / Math.Max(8.0, box.Width * 0.5), -1.0, 1.0);
            double rawCy = Math.Clamp((pt.Y - (box.Y + (box.Height * 0.5))) / Math.Max(8.0, box.Height * 0.5), -1.0, 1.0);
            if (double.IsFinite(rawCx) && double.IsFinite(rawCy))
            {
                _fxCursorX += (rawCx - _fxCursorX) * 0.22;
                _fxCursorY += (rawCy - _fxCursorY) * 0.22;
                return;
            }
        }

        _fxCursorX *= 0.85;
        _fxCursorY *= 0.85;
    }

    private void DrawSlotAnimatedLogo(
        DrawingContext dc,
        IconSlot slot,
        bool isSettling,
        double age,
        double cursorX,
        double cursorY,
        double settleProgress)
    {
        var look = CyberLook.Current;
        var accent = CyberLook.Readable(look.Accent, FxInk);
        var backdrop = isSettling ? look.Body : FxInk;
        var box = SlotBounds(slot);
        var kind = AnimatedIconCatalog.Match(slot.Item.Name, slot.Item.ProcessName);

        if (slot.Card is { } card && CardIcons.ExtractLogo(isSettling ? card.OnDock : card.OnFrame) is { } logo)
        {
            double scale = box.Width / IconBox;
            var cardRect = Rect.Inflate(box, CardBleed * scale, CardBleed * scale);
            double inset = cardRect.Width * (43.0 / 192.0);
            double logoSize = cardRect.Width * (106.0 / 192.0);
            var logoRect = new Rect(cardRect.X + inset, cardRect.Y + inset, logoSize, logoSize);
            var clipRect = Rect.Inflate(logoRect, logoRect.Width * 0.22, logoRect.Height * 0.22);

            bool popped = PushUprightAbout(dc, logoRect);
            CyberAnimatedIcons.DrawAnimatedLogo(dc, logo, kind, logoRect, clipRect, age, accent, FxCyan, isCard: true, cursorX, cursorY, settleProgress, backdrop);
            if (popped) dc.Pop();
        }
    }

    private bool PushUprightAbout(DrawingContext dc, Rect bounds)
    {
        if (_fx.Upright.IsIdentity) return false;
        double cx = bounds.X + (bounds.Width * 0.5);
        double cy = bounds.Y + (bounds.Height * 0.5);
        var m = Matrix.Identity;
        m.Translate(-cx, -cy);
        m.Append(_fx.Upright);
        m.Translate(cx, cy);
        dc.PushTransform(new MatrixTransform(m));
        return true;
    }

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
            DrawCarriedItem(dc, ghost.Icon, ghost.Mask, ghost.At);
        }
    }

    // the dragged app travels in its own item frame, so it reads over the dock and whatever is behind it alike
    private void DrawCarriedItem(DrawingContext dc, ImageSource icon, ImageBrush? mask, Point at)
    {
        var look = CyberLook.Current;
        var box = new Rect(at.X - (IconBox / 2), at.Y - (IconBox / 2), IconBox, IconBox);
        var frame = Rect.Inflate(box, 6, 6);
        var middle = RestTop + (RestBarHeight / 2);
        dc.PushOpacity(_isDragging && _dragGap < 0 ? .45 : 1); // pulled off the dock: it is on its way out
        if (frame.Bottom < middle)
        {
            var dash = new Pen(CyberFxLayer.Solid(FxCyan), 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
            dc.DrawLine(dash, new Point(at.X, frame.Bottom), new Point(at.X, middle));
        }

        dc.DrawGeometry(CyberFxLayer.Solid(FxInk), null, CyberFxLayer.SlotOutline(frame));
        if (mask is null)
        {
            dc.DrawImage(icon, box);
        }
        else
        {
            dc.PushOpacityMask(mask);
            dc.DrawRectangle(CyberFxLayer.Solid(CyberLook.Readable(look.Accent, FxInk)), null, box);
            dc.Pop();
        }

        CyberFxLayer.SlotFrame(dc, frame, look.Accent, FxCyan, FxInk);
        dc.Pop();
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
        bool dropping = ReferenceEquals(CyberDropSlot, slot);
        if ((slot.Item.Windows.Count > 0 && !dropping) || age < .08) return;

        bool flip = box.Right + 18 + width > _windowWidth - 4;
        double left = flip ? box.Left - 18 - width : box.Right + 18;
        var card = new Rect(left, Math.Max(2, box.Top - height - 8), width, height);
        dc.DrawLine(new Pen(accent, 1.5), new Point(flip ? box.Left : box.Right, box.Top), new Point(flip ? card.Right : card.Left, card.Bottom - 9));
        CyberFxLayer.ItemFrame(dc, card, look.Accent, FxCyan, FxInk);
        string name = slot.Item.Name.ToUpper(Loc.Culture);
        if (name.Length > 20) name = name[..20];
        _fx.Text(dc, CyberFxLayer.Decode(name, CyberFxLayer.Segment(age, .08, .42), Math.Floor(age * 22)), card.X + inset, card.Y + 5, 16, CyberFxLayer.Solid(look.Strip));
        _fx.Text(dc, Loc.T(dropping ? "BIRAK // BUNUNLA AÇ" : slot.Item.IsPinned ? "SABİT // HAZIR" : "HAZIR"), card.X + inset, card.Y + 24, 12, CyberFxLayer.Solid(FxCyan));
    }

    // the data strip in the free room under the bar
    private void DrawTicker(DrawingContext dc, IconSlot slot, double age)
    {
        // a small dock has no room for the band under it: its letters would be too small to read
        if (FullScale < .85) return;
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

        if ((CardIcon(slot.Item) ?? slot.Item.Icon) is not { } icon) return;
        _fxGhost = (icon, ContourMask(slot.Item), at);
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
        if (item is null) return;
        if (!GlassSettings.Cyber)
        {
            ShowGlassNotice(item);
            return;
        }

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
            // down a side the time stands upright across the bar: its line is centred on the box it turned into
            double top = DockSettings.Vertical ? bounds.Y + ((bounds.Height - _clockTime.ActualHeight) / 2) + 1 : bounds.Y + 1;
            _fx.Text(dc, _clockTime.Text, middle - 3, top, _clockTime.FontSize, red, TextAlignment.Center, upright: true);
            _fx.Text(dc, _clockTime.Text, middle + 3, top, _clockTime.FontSize, cyan, TextAlignment.Center, upright: true);
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
        CancelFxSettle(restoreArtwork: true);
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
        var slot = _fxHover ?? (_slots.Count > 2 ? _slots[2] : _slots.FirstOrDefault());
        if (slot is null) return;
        switch (name)
        {
            case "hex": PlayHexDump(slot); break;
            case "glitch": PlayGlitchClick(slot, CyberClickStyle.Glitch); break;
            case "datamosh": PlayGlitchClick(slot, CyberClickStyle.Datamosh); break;
            case "bitcrush": PlayGlitchClick(slot, CyberClickStyle.BitCrush); break;
            case "warp": PlayWindowWarp(slot, forward: true, windowIndex: 1, windowCount: 3); break;
            case "launch": PlayCyberLaunch(slot); break;
            case "cubes": PlayClickCubes(slot); break;
            case "cubes1": PlayClickCubes(_slots[1]); break;
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
            case "cards":
                GlassSettings.PreviewIconSet(DockIconSet.Cards);
                foreach (var each in _slots) ApplyCyberIcon(each);
                break;
            case "drop":
                _dropIndex = _slots.IndexOf(slot);
                UpdateDropRing();
                break;
            case "ghost": SetFxGhost(slot, new Point(slot.Center + 90, RestTop - 26)); break;
            case "coverin": PreviewMedia(); PlayCoverIn(); break;
            case "coverswap": PreviewMedia(); PlayCoverSwap(); break;
            case "coverout": PreviewMedia(); PlayCoverOut(); break;
            case "lock" or "rest" or "tear":
                SetCyberLock(slot);
                UpdateLayout();
                _lockMark.Preview(name, progress);
                UpdateLayout();
                return;
            case "sidestripe" or "focuspop" or "chroma" or "runner":
                AuraSettings.PreviewCyberActive(name switch
                {
                    "sidestripe" => CyberActiveStyle.SideStripe,
                    "focuspop" => CyberActiveStyle.FocusPop,
                    "chroma" => CyberActiveStyle.ChromaticEcho,
                    _ => CyberActiveStyle.PerimeterRunner,
                });
                SetCyberLock(slot);
                UpdateLayout();
                _lockMark.Preview("rest", progress);
                UpdateLayout();
                return;
        }

        _fx.Seek(progress);
    }
}
