using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floe.App.Services;
using Floe.Core.Services;

namespace Floe.App.Effects.Cyber;

/// <summary>
/// Animates unmodified original dock icons by understanding each icon's actual pixel topology —
/// connected islands, negative-space cutout stencils, multi-tone depth planes, and structural seams —
/// so the real parts inside each icon move according to their physical geometry while preserving 100% of original pixels at rest.
/// </summary>
public static class CyberAnimatedIcons
{
    private static readonly Dictionary<Color, SolidColorBrush> BrushCache = new();
    private static readonly Dictionary<(Color Color, double Thickness), Pen> PenCache = new();

    /// <summary>
    /// Draws <paramref name="logo"/> using per-part anatomical choreography inside <paramref name="targetRect"/>.
    /// </summary>
    public static void DrawAnimatedLogo(
        DrawingContext dc,
        ImageSource logo,
        AnimatedIconKind kind,
        Rect targetRect,
        Rect clipRect,
        double age,
        Color accent,
        Color cyan,
        bool isCard,
        double cursorX = 0,
        double cursorY = 0,
        double settleProgress = 0,
        Color? cutoutBackdrop = null)
    {
        if (targetRect.Width <= 1 || targetRect.Height <= 1) return;

        var fitRect = UniformFit(targetRect, logo);
        double active = 1.0 - EaseOutCubic(Math.Clamp(settleProgress, 0, 1));

        // At rest (t = 0) or completed settle, render the raw unmodified bitmap 100% pixel-for-pixel.
        if (age <= 0.0001 || active <= 0.001)
        {
            dc.DrawImage(logo, fitRect);
            return;
        }

        double cx = fitRect.X + (fitRect.Width * 0.5);
        double cy = fitRect.Y + (fitRect.Height * 0.5);
        double ease = EaseOutCubic(Math.Clamp(age / 0.24, 0, 1)) * active;
        double cursorBlend = active * EaseOutCubic(Math.Clamp(age / 0.14, 0, 1));
        double effCx = Math.Clamp(cursorX, -1, 1) * cursorBlend;
        double effCy = Math.Clamp(cursorY, -1, 1) * cursorBlend;
        var backdrop = cutoutBackdrop ?? Color.FromRgb(0x0A, 0x0A, 0x10);

        dc.PushClip(FrozenRect(clipRect));

        bool handled = kind switch
        {
            AnimatedIconKind.FileExplorer => TryDrawFolderAnatomy(dc, logo, fitRect, clipRect, cx, cy, age, ease, active, effCx, effCy, accent, cyan, isCard),
            AnimatedIconKind.Chrome => TryDrawChromeAnatomy(dc, logo, fitRect, cx, cy, age, ease, active, settleProgress, effCx, effCy, accent, cyan, isCard),
            AnimatedIconKind.Steam => TryDrawSteamAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy, accent, cyan, backdrop, isCard),
            AnimatedIconKind.AppleMusic => TryDrawAppleMusicAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy, accent, cyan, backdrop, isCard),
            AnimatedIconKind.Spotify => TryDrawSpotifyAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy, accent, cyan, backdrop),
            AnimatedIconKind.Antigravity => TryDrawAntigravityAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy, accent, cyan, isCard),
            AnimatedIconKind.OpenCode => TryDrawOpenCodeAnatomy(dc, logo, fitRect, cx, cy, age, ease, active, effCx, effCy, accent, cyan),
            AnimatedIconKind.Claude => TryDrawClaudeAnatomy(dc, logo, fitRect, age, ease, active, settleProgress, effCx, effCy, accent, cyan),
            AnimatedIconKind.OrcaSlicer => TryDrawOrcaSlicerAnatomy(dc, logo, fitRect, cx, cy, age, ease, settleProgress, effCx, effCy, accent, cyan),
            AnimatedIconKind.TaskManager => TryDrawTaskManagerAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy, accent, cyan),
            AnimatedIconKind.Blender => TryDrawBlenderAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy, accent, cyan, isCard),
            AnimatedIconKind.Notepad => TryDrawNotepadAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy, accent, cyan),
            AnimatedIconKind.Terminal => TryDrawTerminalAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy, accent, cyan, backdrop),
            AnimatedIconKind.SevenZip => TryDrawSevenZipAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy, accent, cyan),
            AnimatedIconKind.Fusion => TryDrawFusionAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy, accent, cyan, isCard),
            AnimatedIconKind.VSCode => TryDrawVSCodeAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy, accent, cyan, isCard),
            AnimatedIconKind.RivaTuner => TryDrawRivatunerAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy, accent, cyan),
            AnimatedIconKind.GitHub => TryDrawGitHubAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy, accent, cyan, backdrop),
            AnimatedIconKind.WindowsSettings => TryDrawSettingsGearAnatomy(dc, logo, fitRect, cx, cy, age, ease, active, settleProgress, effCx, effCy, accent, cyan),
            AnimatedIconKind.MSIAfterburner => TryDrawMsiAfterburnerAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy, accent, cyan),
            _ => TryDrawAutomaticAnatomy(dc, logo, kind, fitRect, cx, cy, age, ease, active, settleProgress, effCx, effCy, accent, cyan, backdrop, isCard),
        };

        if (!handled)
        {
            DrawFallbackMotion(dc, logo, kind, fitRect, cx, cy, age, ease, active, settleProgress, effCx, effCy, accent, cyan, isCard);
        }

        dc.Pop();
    }

    // 1. FILE EXPLORER: Clean mechanical folder open (grounded back tab + subtle inner mouth glow + symmetrical hinged front flap).
    private static bool TryDrawFolderAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect, Rect clipRect,
        double cx, double cy, double age, double ease, double active, double effCx, double effCy,
        Color accent, Color cyan, bool isCard)
    {
        if (CardIcons.SplitHorizontal(logo, isCard ? 0.23 : 0.27) is not { } split) return false;

        double open = EaseOutBackSubtle(Math.Clamp(age / 0.28, 0, 1)) * active;
        double breathe = Math.Sin(age * 2.8) * 0.015 * ease;
        double baseScale = 1.0 + (0.03 * ease);

        // Part 1: Grounded back folder wall & tab.
        dc.PushTransform(BuildTransform(cx, cy, effCx * 0.6, (-1.4 * ease) + (effCy * 0.4), 0, baseScale, baseScale));
        dc.DrawImage(split.Top, fitRect);
        dc.Pop();

        // Part 2: Subtle interior pocket depth shadow + clean rim light inside the open mouth (no cartoon documents).
        if (open > 0.04)
        {
            double mouthY = fitRect.Top + (fitRect.Height * (isCard ? 0.24 : 0.28)) - (0.8 * ease);
            double mouthW = fitRect.Width * 0.78;
            double mouthH = Math.Max(1.5, fitRect.Height * 0.08 * open);
            dc.DrawRectangle(
                CachedBrush(Color.FromArgb((byte)(145 * open), 0x08, 0x0A, 0x10)),
                null,
                new Rect(cx - (mouthW * 0.5), mouthY, mouthW, mouthH));
            dc.DrawRectangle(
                CachedBrush(Color.FromArgb((byte)(170 * open), 0xFF, 0xF4, 0xCC)),
                null,
                new Rect(cx - (mouthW * 0.46), mouthY + mouthH - 1.0, mouthW * 0.92, 1.2));
        }

        // Part 3: Symmetrical bottom-hinged front flap opening forward cleanly.
        dc.PushTransform(BuildTransform(
            cx,
            fitRect.Bottom,
            effCx * 1.1,
            (-0.5 * ease) + (effCy * 0.6),
            0,
            1.0 + (0.035 * open),
            1.0 - ((0.10 + breathe) * open),
            skewXDeg: effCx * -2.0));
        dc.DrawImage(split.Bottom, fitRect);
        DrawDiagonalSheen(dc, split.Bottom, fitRect, age, effCx * 0.18, Colors.White, Color.FromRgb(0xFF, 0xF6, 0xD6), peakOpacity: 0.22);
        dc.Pop();
        return true;
    }

    // 2. GOOGLE CHROME: 3 individual outer shutter petals irising open/closed while spinning around the cursor-tracking pupil.
    private static bool TryDrawChromeAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double active, double settleProgress,
        double effCx, double effCy, Color accent, Color cyan, bool isCard)
    {
        double outerAngle = SettleAngle(IntegrateEasedSpin(age, speedDegPerSec: 64.0, rampSec: 0.28), active, settleProgress);
        var islands = CyberIconAnatomy.GetConnectedIslands(logo);

        if (islands is { Count: >= 4 })
        {
            // Identify center core sphere vs 3 outer shutter blades.
            var sorted = islands
                .OrderBy(i => Math.Sqrt(Math.Pow(i.NormalizedCenter.X - 0.5, 2) + Math.Pow(i.NormalizedCenter.Y - 0.5, 2)))
                .ToList();
            var core = sorted[0];
            var petals = sorted.Skip(1).ToList();

            // Each outer petal spins around (cx, cy) AND breathes radially outward along its own shutter vector.
            dc.PushTransform(BuildTransform(cx, cy, effCx * 0.6, effCy * 0.6, outerAngle, 1.0, 1.0));
            for (int i = 0; i < petals.Count; i++)
            {
                var petal = petals[i];
                double irisShift = (1.9 + (1.2 * Math.Sin((age * 4.8) + (i * 2.094)))) * ease;
                double pxShift = petal.RadialDir.X * irisShift;
                double pyShift = petal.RadialDir.Y * irisShift;

                dc.PushTransform(new TranslateTransform(pxShift, pyShift));
                dc.DrawImage(petal.Bitmap, fitRect);
                DrawDiagonalSheen(dc, petal.Bitmap, fitRect, age, effCx * 0.2, cyan, isCard ? accent : cyan, 0.55);
                dc.Pop();
            }
            dc.Pop();

            DrawChromeIrisRing(dc, fitRect, cx, cy, age, ease, effCx, effCy, cyan);

            double innerScale = 1.0 + ((0.06 + (0.05 * Math.Sin(age * 4.6))) * ease);
            dc.PushTransform(BuildTransform(cx, cy, effCx * 2.6, effCy * 2.6, 0, innerScale, innerScale));
            dc.DrawImage(core.Bitmap, fitRect);
            dc.Pop();
            return true;
        }

        if (CardIcons.SplitRadial(logo, 0.415) is { } radial)
        {
            double outerScale = 1.0 + (0.04 * ease);
            dc.PushTransform(BuildTransform(cx, cy, effCx * 0.6, effCy * 0.6, outerAngle, outerScale, outerScale));
            dc.DrawImage(radial.Outer, fitRect);
            dc.Pop();

            DrawChromeIrisRing(dc, fitRect, cx, cy, age, ease, effCx, effCy, cyan);

            double innerScale = 1.0 + ((0.05 + (0.05 * Math.Sin(age * 4.6))) * ease);
            dc.PushTransform(BuildTransform(cx, cy, effCx * 2.5, effCy * 2.5, 0, innerScale, innerScale));
            dc.DrawImage(radial.Inner, fitRect);
            dc.Pop();
            return true;
        }

        return false;
    }

    private static void DrawChromeIrisRing(
        DrawingContext dc, Rect fitRect, double cx, double cy, double age, double ease, double effCx, double effCy, Color cyan)
    {
        if (ease <= 0.03) return;
        double irisR = Math.Min(fitRect.Width, fitRect.Height) * 0.5 * 0.425 * (1.0 + (0.04 * ease));
        var center = new Point(cx + (effCx * 1.1), cy + (effCy * 1.1));
        dc.DrawEllipse(null, CachedPen(Color.FromArgb((byte)(155 * ease), cyan.R, cyan.G, cyan.B), 1.1), center, irisR, irisR);

        double tickBase = -age * 95.0;
        var tickPen = CachedPen(Color.FromArgb((byte)(220 * ease), cyan.R, cyan.G, cyan.B), 1.7);
        for (int i = 0; i < 3; i++)
        {
            DrawArc(dc, center, irisR, tickBase + (i * 120.0), 34.0, tickPen);
        }
    }

    // 3. STEAM: Stationary circular badge while the internal locomotive piston arm & crank wheels physically pump inside.
    private static bool TryDrawSteamAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double effCx, double effCy,
        Color accent, Color cyan, Color backdrop, bool isCard)
    {
        if (!isCard || CyberIconAnatomy.GetSteamAnatomy(logo) is not { } steam) return false;

        // Pressurized steam puffs venting from the top-right cylinder port.
        for (int i = 0; i < 4; i++)
        {
            double t = ((age * 1.7) + (i * 0.25)) % 1.0;
            byte a = (byte)(230 * Math.Sin(t * Math.PI) * ease);
            if (a < 8) continue;
            double sx = fitRect.Right - 5.5 + (t * 9.5) + (i * 1.0);
            double sy = fitRect.Top + 5.5 - (t * 9.5) + Math.Sin((age * 7.0) + i);
            var c = i % 2 == 0 ? cyan : Color.FromRgb(0xF0, 0xF4, 0xFC);
            double sz = 2.2 + ((i % 2) * 0.6);
            dc.DrawRectangle(CachedBrush(Color.FromArgb(a, c.R, c.G, c.B)), null, new Rect(sx, sy, sz, sz));
        }

        // Part 1: Stationary circular engine disc.
        double discScale = 1.0 + (0.025 * ease);
        dc.PushTransform(BuildTransform(cx, cy, effCx * 0.8, (-0.8 * ease) + (effCy * 0.8), 0, discScale, discScale));
        dc.DrawImage(steam.FilledDisc, fitRect);
        DrawDiagonalSheen(dc, steam.FilledDisc, fitRect, age, effCx * 0.2, cyan, accent, 0.45);

        // Clip internal piston crank strictly to the circular disc boundary.
        double clipR = Math.Min(fitRect.Width, fitRect.Height) * 0.492;
        var discClip = new EllipseGeometry(new Point(cx, cy), clipR, clipR);
        discClip.Freeze();
        dc.PushClip(discClip);

        // Part 2: Internal locomotive piston reciprocation along (-45°) crank axis.
        double theta = age * 7.4;
        double stroke = Math.Sin(theta) * 4.4 * ease;
        double rodTx = stroke * 0.707;
        double rodTy = -stroke * 0.707;
        double rodRock = Math.Cos(theta) * 7.8 * ease;
        double upperAxleX = fitRect.Left + (fitRect.Width * 0.660);
        double upperAxleY = fitRect.Top + (fitRect.Height * 0.368);

        dc.PushTransform(BuildTransform(upperAxleX, upperAxleY, rodTx, rodTy, rodRock, 1.0, 1.0));
        // Subtle cyan internal chamber glow along the moving piston slot.
        dc.PushTransform(new TranslateTransform(-1.0 * ease, 1.0 * ease));
        DrawSilhouette(dc, steam.CrankCutout, fitRect, CachedBrush(Color.FromArgb((byte)(170 * ease), cyan.R, cyan.G, cyan.B)));
        dc.Pop();
        if (steam.IsColored)
        {
            dc.DrawImage(steam.CrankCutout, fitRect);
        }
        else
        {
            DrawSilhouette(dc, steam.CrankCutout, fitRect, CachedBrush(backdrop));
        }
        dc.Pop();

        // Part 3: Lower-left reciprocating pivot wheel island.
        if (steam.LowerWheel is { } lower)
        {
            double lcx = fitRect.Left + (fitRect.Width * lower.NormalizedCenter.X);
            double lcy = fitRect.Top + (fitRect.Height * lower.NormalizedCenter.Y);
            dc.PushTransform(BuildTransform(lcx, lcy, rodTx * 1.15, rodTy * 1.15, -theta * 57.3, 1.0, 1.0));
            dc.DrawImage(lower.Bitmap, fitRect);
            dc.Pop();
        }

        // Part 4: Upper-right main crank wheel hub + orbiting cyan crank pin.
        if (steam.UpperWheel is { } upper)
        {
            double ucx = fitRect.Left + (fitRect.Width * upper.NormalizedCenter.X);
            double ucy = fitRect.Top + (fitRect.Height * upper.NormalizedCenter.Y);
            dc.PushTransform(BuildTransform(ucx, ucy, rodTx * 0.55, rodTy * 0.55, theta * 57.3, 1.0, 1.0));
            dc.DrawImage(upper.Bitmap, fitRect);
            dc.Pop();

            double pinR = fitRect.Width * 0.065;
            var pinPos = new Point(
                ucx + (rodTx * 0.55) + (Math.Cos(theta) * pinR),
                ucy + (rodTy * 0.55) + (Math.Sin(theta) * pinR));
            dc.DrawEllipse(CachedBrush(Color.FromArgb((byte)(230 * ease), cyan.R, cyan.G, cyan.B)), null, pinPos, 1.7, 1.7);
        }

        dc.Pop(); // discClip
        dc.Pop(); // discTransform
        return true;
    }

    // 4. APPLE MUSIC: Stationary rounded square tile while the extracted '♫' musical note dances, hops, and tilts inside.
    private static bool TryDrawAppleMusicAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double effCx, double effCy,
        Color accent, Color cyan, Color backdrop, bool isCard)
    {
        if (CyberIconAnatomy.GetCutoutStencil(logo) is not { Parts.Count: >= 1 } stencil) return false;

        double beat = Math.Pow(Math.Max(0, Math.Sin(age * 6.4)), 2.6);
        double squash = Math.Pow(Math.Max(0, -Math.Sin(age * 6.4)), 3.0);

        // Part 1: Grounded rounded square tile (stays upright, only subtle bass resonance).
        double tileScale = 1.0 + ((0.02 + (0.025 * beat)) * ease);
        dc.PushTransform(BuildTransform(cx, cy, effCx * 1.0, (-0.8 * ease) + (effCy * 0.8), 0, tileScale, tileScale));
        dc.DrawImage(stencil.FilledBase, fitRect);
        DrawDiagonalSheen(dc, stencil.FilledBase, fitRect, age, effCx * 0.2, cyan, isCard ? accent : cyan, 0.38);

        // Part 2: The extracted '♫' note dancing inside the stationary tile.
        var note = stencil.Parts[0];
        double nx = fitRect.Left + (fitRect.Width * note.NormalizedCenter.X);
        double ny = fitRect.Top + (fitRect.Height * note.NormalizedCenter.Y);
        double noteTy = ((-5.2 * beat) + (1.4 * squash)) * ease;
        double noteTx = Math.Sin(age * 3.2) * 2.2 * ease;
        double noteAngle = Math.Sin(age * 3.2) * 12.5 * ease;
        double noteSx = 1.0 + (((-0.09 * beat) + (0.08 * squash)) * ease);
        double noteSy = 1.0 + (((0.15 * beat) - (0.09 * squash)) * ease);

        dc.PushTransform(BuildTransform(nx, ny, noteTx, noteTy, noteAngle, noteSx, noteSy));
        DrawIconPart(dc, note, fitRect, backdrop, cyan, accent, ease, chromaOffset: 1.4);
        dc.Pop();

        dc.Pop(); // tileTransform

        DrawMiniEqualizerAndSparks(dc, fitRect, cx, age, ease, cyan, accent);
        return true;
    }

    // 5. SPOTIFY (Card AND Real Green Icon): Stationary circular disc while the 3 curved sound-wave bars pulse independently in sequence.
    private static bool TryDrawSpotifyAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double effCx, double effCy,
        Color accent, Color cyan, Color backdrop)
    {
        if (CyberIconAnatomy.GetCutoutStencil(logo, isCircularDisc: true) is not { Parts.Count: >= 2 } stencil) return false;

        double beat = Math.Pow(Math.Max(0, Math.Sin(age * 5.6)), 3.2);
        DrawAcousticEcho(dc, stencil.FilledBase, fitRect, cx, cy, age, ease, accent, cyan);

        // Part 1: Grounded circular disc.
        double discScale = 1.0 + ((0.025 + (0.035 * beat)) * ease);
        dc.PushTransform(BuildTransform(cx, cy, effCx * 1.1, (-0.8 * ease) + (effCy * 1.0), 0, discScale, discScale));
        dc.DrawImage(stencil.FilledBase, fitRect);

        // Parts 2..4: The 3 curved sound-wave arcs (ordered bottom-to-top so acoustic energy radiates upward).
        var wavesBottomUp = stencil.Parts.OrderByDescending(p => p.NormalizedCenter.Y).ToList();
        for (int w = 0; w < wavesBottomUp.Count; w++)
        {
            var wave = wavesBottomUp[w];
            double phase = (age * 6.4) - (w * 0.95);
            double wavePulse = Math.Pow(Math.Max(0, Math.Sin(phase)), 2.0) * ease;

            double wx = fitRect.Left + (fitRect.Width * wave.NormalizedCenter.X);
            double wy = fitRect.Top + (fitRect.Height * wave.NormalizedCenter.Y);
            double dy = -(1.5 + (w * 1.05)) * wavePulse;
            double sx = 1.0 + ((0.08 + (w * 0.03)) * wavePulse);
            double sy = 1.0 + ((0.22 + (w * 0.04)) * wavePulse);
            double wAngle = Math.Sin((age * 3.2) + (w * 0.8)) * 3.2 * ease;

            dc.PushTransform(BuildTransform(wx, wy, 0, dy, wAngle, sx, sy));
            DrawIconPart(dc, wave, fitRect, backdrop, cyan, accent, wavePulse, chromaOffset: 1.2);
            dc.Pop();
        }

        dc.Pop();
        return true;
    }

    // 6. ANTIGRAVITY: Grounded rounded square tile while the inner dark 'A' delta-wing spacecraft lifts off in zero-G with ion thruster rings.
    private static bool TryDrawAntigravityAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double effCx, double effCy,
        Color accent, Color cyan, bool isCard)
    {
        if (CyberIconAnatomy.GetToneAnatomy(logo, AnimatedIconKind.Antigravity) is not { } tone) return false;

        // Part 1: Grounded rounded square tile base.
        double tileScale = 1.0 + (0.02 * ease);
        dc.PushTransform(BuildTransform(cx, cy, effCx * 0.7, effCy * 0.6, 0, tileScale, tileScale));
        dc.DrawImage(tone.BaseLayer, fitRect);
        DrawDiagonalSheen(dc, tone.BaseLayer, fitRect, age, effCx * 0.2, cyan, isCard ? accent : cyan, 0.35);
        dc.Pop();

        // Part 2: Ion thruster plasma rings firing inside the tile beneath the lifting 'A' delta-wing ship.
        double shipX = fitRect.Left + (fitRect.Width * tone.FrontCenter.X);
        double shipY = fitRect.Top + (fitRect.Height * tone.FrontCenter.Y);
        double liftY = ((-7.6 + (2.4 * Math.Sin(age * 3.4))) * ease) + (effCy * 1.4);
        double liftX = effCx * 2.4;
        double bankAngle = (Math.Sin(age * 2.2) * 4.8 * ease) + (effCx * 4.0);
        double rollScaleX = 1.0 - (0.07 * Math.Pow(Math.Sin(age * 2.2), 2.0) * ease);

        double thrusterY = fitRect.Top + (fitRect.Height * 0.76);
        for (int r = 0; r < 2; r++)
        {
            double t = ((age * 1.65) + (r * 0.5)) % 1.0;
            double rx = fitRect.Width * (0.11 + (0.18 * t));
            double ry = 1.2 + (1.4 * t);
            byte alpha = (byte)(225 * (1.0 - t) * ease);
            if (alpha < 8) continue;
            dc.DrawEllipse(
                null,
                CachedPen(Color.FromArgb(alpha, cyan.R, cyan.G, cyan.B), 1.45),
                new Point(cx + (liftX * 0.5), thrusterY + (t * 4.5)),
                rx,
                ry);
        }

        // Part 3: The 'A' Delta-Wing Spacecraft lifting off vertically in zero-G.
        dc.PushTransform(BuildTransform(shipX, shipY, liftX, liftY, bankAngle, (1.0 + (0.07 * ease)) * rollScaleX, 1.0 + (0.07 * ease), skewXDeg: effCx * -3.5));
        // Cyan plasma under-rim on the lifting spacecraft.
        dc.PushTransform(new TranslateTransform(0, 1.5 * ease));
        DrawSilhouette(dc, tone.FrontLayer, fitRect, CachedBrush(Color.FromArgb((byte)(185 * ease), cyan.R, cyan.G, cyan.B)));
        dc.Pop();
        dc.DrawImage(tone.FrontLayer, fitRect);
        dc.Pop();

        return true;
    }

    // 7. OPENCODE: Left and Right monolith portal jaws slide apart horizontally to expose the live code chamber inside.
    private static bool TryDrawOpenCodeAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double active, double effCx, double effCy,
        Color accent, Color cyan)
    {
        if (CyberIconAnatomy.SplitSeam(logo, seamKind: 2) is not { } jaws) return false;

        double open = EaseOutBackSubtle(Math.Clamp(age / 0.28, 0, 1)) * active;
        double splitPx = 3.8 * open;
        double scale = 1.0 + (0.04 * ease);

        dc.PushTransform(BuildTransform(cx, cy, effCx * 1.4, (-1.2 * ease) + (effCy * 1.2), 0, scale, scale, skewXDeg: effCx * -3.0));

        // Inner chamber code stream & blinking terminal cursor revealed as the monolith jaws part.
        DrawOpenCodeInteriorTelemetry(dc, fitRect, cx, cy, age, ease, cyan, accent);

        // Left Monolith Jaw sliding left.
        dc.PushTransform(new TranslateTransform(-splitPx, 0));
        dc.DrawImage(jaws.First, fitRect);
        DrawLaserScan(dc, jaws.First, fitRect, age, cyan, accent);
        dc.Pop();

        // Right Monolith Jaw sliding right.
        dc.PushTransform(new TranslateTransform(splitPx, 0));
        dc.DrawImage(jaws.Second, fitRect);
        DrawLaserScan(dc, jaws.Second, fitRect, age, cyan, accent);
        dc.Pop();

        dc.Pop();
        return true;
    }

    // 8. CLAUDE: Rotates around its true solid core nexus (57.5, 51.4) while 3 interleaved 4-ray constellations telescope radially.
    private static bool TryDrawClaudeAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double age, double ease, double active, double settleProgress,
        double effCx, double effCy, Color accent, Color cyan)
    {
        if (CyberIconAnatomy.GetStarburstAnatomy(logo) is not { } star) return false;

        double coreX = fitRect.Left + (fitRect.Width * star.CoreCenter.X);
        double coreY = fitRect.Top + (fitRect.Height * star.CoreCenter.Y);
        double spin = SettleAngle(IntegrateEasedSpin(age, speedDegPerSec: 32.0, rampSec: 0.28), active, settleProgress);

        ReadOnlySpan<double> phases = [0.0, 2.094395, 4.188790];
        BitmapSource[] groups = [star.GroupA, star.GroupB, star.GroupC];

        for (int g = 0; g < 3; g++)
        {
            double rayPulse = 1.0 + ((0.04 + (0.095 * Math.Sin((age * 5.4) + phases[g]))) * ease);
            dc.PushTransform(BuildTransform(coreX, coreY, effCx * 1.3, (-1.0 * ease) + (effCy * 1.1), spin, rayPulse, rayPulse));
            dc.DrawImage(groups[g], fitRect);
            DrawRadialSynapticPulse(dc, groups[g], fitRect, age, ease, cyan, accent);
            dc.Pop();
        }

        double hubScale = 1.0 + ((0.05 + (0.04 * Math.Cos(age * 5.4))) * ease);
        dc.PushTransform(BuildTransform(coreX, coreY, effCx * 1.3, (-1.0 * ease) + (effCy * 1.1), spin, hubScale, hubScale));
        dc.DrawImage(star.CoreHub, fitRect);
        dc.Pop();

        return true;
    }

    // 9. ORCASLICER: Dark circular build plate stays grounded while the bright Orca whale 3D-prints onto it and then breaches/swims inside.
    private static bool TryDrawOrcaSlicerAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double settleProgress,
        double effCx, double effCy, Color accent, Color cyan)
    {
        if (CyberIconAnatomy.GetToneAnatomy(logo, AnimatedIconKind.OrcaSlicer) is not { } tone) return false;

        double scale = 1.0 + (0.04 * ease);
        dc.PushTransform(BuildTransform(cx, cy, effCx * 1.2, (-1.2 * ease) + (effCy * 1.0), 0, scale, scale));

        // Grounded dark circular build plate.
        dc.DrawImage(tone.BaseLayer, fitRect);

        if (age < 0.56 && settleProgress <= 0)
        {
            // Phase 1: Layer-by-layer 3D printing of the bright Orca whale onto the dark build plate.
            double raw = Math.Clamp(age / 0.56, 0, 1);
            double prog = (0.45 * raw) + (0.55 * EaseOutCubic(raw));
            double printY = fitRect.Bottom - (fitRect.Height * prog);

            if (printY > fitRect.Top + 1)
            {
                dc.PushClip(FrozenRect(new Rect(fitRect.X - 4, fitRect.Y - 4, fitRect.Width + 8, Math.Max(0.5, printY - fitRect.Top + 4))));
                dc.PushOpacityMask(MaskBrush(tone.FrontLayer, fitRect));
                var slicePen = CachedPen(Color.FromArgb(135, cyan.R, cyan.G, cyan.B), 1.0);
                for (double y = fitRect.Top + 1.5; y < printY; y += 3.0)
                {
                    dc.DrawLine(slicePen, new Point(fitRect.Left, y), new Point(fitRect.Right, y));
                }
                dc.Pop();
                dc.Pop();
            }

            dc.PushClip(FrozenRect(new Rect(fitRect.X - 4, printY, fitRect.Width + 8, Math.Max(0.5, fitRect.Bottom - printY + 4))));
            dc.DrawImage(tone.FrontLayer, fitRect);
            dc.Pop();

            dc.DrawLine(CachedPen(Color.FromArgb(225, cyan.R, cyan.G, cyan.B), 1.4), new Point(fitRect.Left - 1, printY), new Point(fitRect.Right + 1, printY));
            double nozzleX = fitRect.Left + (fitRect.Width * (0.15 + (0.70 * (0.5 + (0.5 * Math.Sin(age * 38.0))))));
            dc.DrawLine(CachedPen(Color.FromArgb(230, accent.R, accent.G, accent.B), 1.3), new Point(nozzleX, printY - 5.5), new Point(nozzleX, printY));
            dc.DrawEllipse(CachedBrush(Color.FromArgb(245, accent.R, accent.G, accent.B)), null, new Point(nozzleX, printY), 2.7, 2.7);
            dc.DrawEllipse(CachedBrush(Color.FromArgb(255, 0xF8, 0xFA, 0xFF)), null, new Point(nozzleX, printY), 1.3, 1.3);
        }
        else
        {
            // Phase 2: The printed Orca whale physically breaches and swims inside the dark circular build plate.
            double swimT = Math.Max(0, age - 0.56);
            double swimAngle = Math.Sin(swimT * 4.0) * 11.5 * ease;
            double swimTx = Math.Cos(swimT * 4.0) * 2.0 * ease;
            double swimTy = -Math.Abs(Math.Sin(swimT * 4.0)) * 2.8 * ease;

            dc.PushTransform(BuildTransform(cx, cy, swimTx, swimTy, swimAngle, 1.0 + (0.03 * ease), 1.0 + (0.03 * ease)));
            dc.DrawImage(tone.FrontLayer, fitRect);
            DrawLaserScan(dc, tone.FrontLayer, fitRect, age, cyan, accent, upward: true);
            dc.Pop();
        }

        dc.Pop();
        return true;
    }

    // 10. TASK MANAGER: Stationary monitor screen while the Back and Front CPU mountain waveforms surge up/down and scroll inside.
    private static bool TryDrawTaskManagerAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double effCx, double effCy,
        Color accent, Color cyan)
    {
        if (CyberIconAnatomy.GetToneAnatomy(logo, AnimatedIconKind.TaskManager) is not { MidLayer: not null } tone) return false;

        double scale = 1.0 + (0.045 * ease);
        dc.PushTransform(BuildTransform(cx, cy, effCx * 1.4, (-1.2 * ease) + (effCy * 1.2), 0, scale, scale, skewXDeg: effCx * -2.8));

        // Clip all oscillating waves strictly inside the exact rounded monitor silhouette.
        dc.PushOpacityMask(MaskBrush(logo, fitRect));

        // Layer 1: Dark monitor sky.
        dc.DrawImage(tone.BaseLayer, fitRect);

        // Layer 2: Back mid-tone CPU mountain wave surging & scrolling horizontally.
        double backSy = 1.0 + (0.28 * Math.Sin(age * 4.8) * ease);
        double backTx = Math.Sin(age * 2.6) * 3.8 * ease;
        dc.PushTransform(BuildTransform(cx, fitRect.Bottom, backTx, 0, 0, 1.08, backSy));
        dc.DrawImage(tone.MidLayer, fitRect);
        dc.Pop();

        // Layer 3: Front bright CPU mountain wave surging in counter-phase.
        double frontSy = 1.0 + (0.30 * Math.Cos(age * 6.2) * ease);
        double frontTx = -Math.Cos(age * 3.2) * 3.4 * ease;
        dc.PushTransform(BuildTransform(cx, fitRect.Bottom, frontTx, 0, 0, 1.08, frontSy));
        dc.DrawImage(tone.FrontLayer, fitRect);
        dc.Pop();

        dc.Pop(); // monitor silhouette mask

        DrawHorizontalOscilloscope(dc, logo, fitRect, age, ease, cyan, accent);
        dc.Pop();
        return true;
    }

    // 11. BLENDER 5.0: Outer 3-fin body tilts in 3D while the inner offset eye pupil (at (0.65, 0.55)) looks around and dilates.
    private static bool TryDrawBlenderAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double effCx, double effCy,
        Color accent, Color cyan, bool isCard)
    {
        if (CyberIconAnatomy.GetConnectedIslands(logo) is not { Count: >= 2 } islands) return false;

        var sorted = islands.OrderByDescending(i => i.PixelCount).ToList();
        var body = sorted[0];
        var pupil = sorted[1];

        double eyeX = fitRect.Left + (fitRect.Width * pupil.NormalizedCenter.X);
        double eyeY = fitRect.Top + (fitRect.Height * pupil.NormalizedCenter.Y);
        double bodyAngle = Math.Sin(age * 2.8) * 6.5 * ease;
        double bodySx = 1.0 - (0.07 * Math.Pow(Math.Sin(age * 1.9), 2.0) * ease);

        // Part 1: Outer finned body pivoting around the eye socket.
        dc.PushTransform(BuildTransform(eyeX, eyeY, effCx * 1.2, (-1.0 * ease) + (effCy * 1.0), bodyAngle, bodySx * (1.0 + (0.04 * ease)), 1.0 + (0.04 * ease)));
        dc.DrawImage(body.Bitmap, fitRect);
        DrawDiagonalSheen(dc, body.Bitmap, fitRect, age, effCx * 0.2, cyan, isCard ? accent : cyan, 0.55);
        dc.Pop();

        // Part 2: Inner eye pupil looking around & dilating inside the eye socket.
        double lookX = ((Math.Cos(age * 3.4) * 2.4) + (effCx * 2.8)) * ease;
        double lookY = ((Math.Sin(age * 2.6) * 2.0) + (effCy * 2.4)) * ease;
        double pupilScale = 1.0 + ((0.08 + (0.09 * Math.Sin(age * 5.2))) * ease);

        dc.PushTransform(BuildTransform(eyeX, eyeY, lookX, lookY, 0, pupilScale, pupilScale));
        dc.PushTransform(new TranslateTransform(0.8 * ease, 0.8 * ease));
        DrawSilhouette(dc, pupil.Bitmap, fitRect, CachedBrush(Color.FromArgb((byte)(170 * ease), cyan.R, cyan.G, cyan.B)));
        dc.Pop();
        dc.DrawImage(pupil.Bitmap, fitRect);
        dc.Pop();

        return true;
    }

    // 12. NOTEPAD: Notepad page stays steady on the left while the diagonal stylus pen on the right scribbles across the page.
    private static bool TryDrawNotepadAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double effCx, double effCy,
        Color accent, Color cyan)
    {
        if (CyberIconAnatomy.SplitSeam(logo, seamKind: 1) is not { } parts) return false;

        // Part 1: Notepad page on the left.
        dc.PushTransform(BuildTransform(cx, cy, effCx * 1.0, (-1.0 * ease) + (effCy * 1.0), -1.5 * ease, 1.0 + (0.035 * ease), 1.0 + (0.035 * ease)));
        dc.DrawImage(parts.First, fitRect);
        DrawLaserScan(dc, parts.First, fitRect, age, cyan, accent);
        dc.Pop();

        // Part 2: Diagonal stylus pen scribbling back-and-forth across the lines.
        double penPivotX = fitRect.Left + (fitRect.Width * 0.88);
        double penPivotY = fitRect.Top + (fitRect.Height * 0.14);
        double scribbleX = ((Math.Sin(age * 11.5) * 2.6) - 1.0) * ease;
        double scribbleY = ((Math.Cos(age * 5.8) * 2.2) + 0.8) * ease;
        double penAngle = Math.Sin(age * 11.5) * 8.5 * ease;

        dc.PushTransform(BuildTransform(penPivotX, penPivotY, scribbleX + (effCx * 1.4), scribbleY + (effCy * 1.2), penAngle, 1.0 + (0.05 * ease), 1.0 + (0.05 * ease)));
        dc.DrawImage(parts.Second, fitRect);
        dc.Pop();

        return true;
    }

    // 13. WINDOWS TERMINAL: 3 top window tabs ripple in sequence while inside the screen '>' nudges forward and '_' blinks & types.
    private static bool TryDrawTerminalAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double effCx, double effCy,
        Color accent, Color cyan, Color backdrop)
    {
        if (CyberIconAnatomy.GetTerminalAnatomy(logo) is not { } term) return false;

        double scale = 1.0 + (0.04 * ease);
        dc.PushTransform(BuildTransform(cx, cy, effCx * 1.4, (-1.2 * ease) + (effCy * 1.2), 0, scale, scale));

        // Part 1: Main terminal screen body.
        dc.DrawImage(term.FilledBody, fitRect);

        // Parts 2..4: The 3 top terminal tabs rippling upward in sequence.
        for (int i = 0; i < term.TopTabs.Count; i++)
        {
            double tabWave = Math.Pow(Math.Max(0, Math.Sin((age * 5.5) - (i * 1.15))), 2.0) * ease;
            dc.PushTransform(new TranslateTransform(0, -2.5 * tabWave));
            dc.DrawImage(term.TopTabs[i].Bitmap, fitRect);
            dc.Pop();
        }

        // Part 5: '>' chevron prompt pulsing right on command execution.
        if (term.PromptHole is { } prompt)
        {
            double kick = Math.Pow(Math.Max(0, Math.Sin(age * 4.2)), 3.0) * ease;
            double px = fitRect.Left + (fitRect.Width * prompt.NormalizedCenter.X);
            double py = fitRect.Top + (fitRect.Height * prompt.NormalizedCenter.Y);
            dc.PushTransform(BuildTransform(px, py, 2.4 * kick, 0, 0, 1.0 + (0.08 * kick), 1.0 + (0.08 * kick)));
            DrawIconPart(dc, prompt, fitRect, backdrop, cyan, accent, ease, chromaOffset: 1.2);
            dc.Pop();
        }

        // Part 6: '_' underscore cursor sliding horizontally and blinking.
        if (term.CursorHole is { } cursor)
        {
            double typeX = (-2.2 + (4.4 * ((age * 1.35) % 1.0))) * ease;
            bool blinkLit = ((int)(age * 5.0) % 2) == 0;
            dc.PushTransform(new TranslateTransform(typeX, 0));
            DrawIconPart(dc, cursor, fitRect, blinkLit ? cyan : backdrop, cyan, accent, ease, chromaOffset: 0.8);
            dc.Pop();
        }

        dc.Pop();
        return true;
    }

    // 14. 7-ZIP: Outer clamp box squeezes horizontally while the inner '7' and 'z' islands physically slide together under compression.
    private static bool TryDrawSevenZipAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double effCx, double effCy,
        Color accent, Color cyan)
    {
        if (CyberIconAnatomy.GetConnectedIslands(logo) is not { Count: >= 3 } islands) return false;

        var sorted = islands.OrderByDescending(i => i.PixelCount).ToList();
        var frame = sorted[0];
        var inner = sorted.Skip(1).OrderBy(i => i.NormalizedCenter.X).ToList();

        double clamp = Math.Pow(Math.Max(0, Math.Sin(age * 3.8)), 3.2) * ease;
        double frameSx = 1.0 + (0.03 * ease) - (0.14 * clamp);
        double frameSy = 1.0 + (0.03 * ease) + (0.08 * clamp);

        dc.PushTransform(BuildTransform(cx, cy, effCx * 1.4, (-1.2 * ease) + (effCy * 1.2), 0, 1.0, 1.0));

        // Part 1: Outer clamp box squeezing inward.
        dc.PushTransform(BuildTransform(cx, cy, 0, 0, 0, frameSx, frameSy));
        dc.DrawImage(frame.Bitmap, fitRect);
        dc.Pop();

        // Parts 2 & 3: '7' and 'z' glyphs getting pushed inward toward the center seam.
        for (int i = 0; i < inner.Count; i++)
        {
            var part = inner[i];
            double dir = part.NormalizedCenter.X < 0.5 ? 1.0 : -1.0;
            double shiftX = dir * 3.0 * clamp;
            double gx = fitRect.Left + (fitRect.Width * part.NormalizedCenter.X);
            double gy = fitRect.Top + (fitRect.Height * part.NormalizedCenter.Y);
            dc.PushTransform(BuildTransform(gx, gy, shiftX, 0, 0, 1.0 - (0.08 * clamp), 1.0 + (0.06 * clamp)));
            dc.DrawImage(part.Bitmap, fitRect);
            dc.Pop();
        }

        DrawCompressionJaws(dc, fitRect, age, ease, cyan);
        dc.Pop();
        return true;
    }

    // 15. AUTODESK FUSION: Upper-left sweeping isometric wing and lower-right vertical pillar articulate in 3D CAD parallax.
    private static bool TryDrawFusionAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double effCx, double effCy,
        Color accent, Color cyan, bool isCard)
    {
        if (CyberIconAnatomy.SplitSeam(logo, seamKind: 3) is not { } planes) return false;

        double wave = Math.Sin(age * 3.6) * ease;
        double scale = 1.0 + (0.045 * ease);

        // Plane 1: Upper-left sweeping isometric ribbon.
        dc.PushTransform(BuildTransform(cx, cy, (-1.8 * wave) + (effCx * 1.6), (-1.8 * wave) - (1.4 * ease) + (effCy * 1.2), 0, scale, scale, skewYDeg: (4.8 * wave) + (effCx * 3.5)));
        dc.DrawImage(planes.First, fitRect);
        DrawDiagonalSheen(dc, planes.First, fitRect, age, effCx * 0.2, cyan, isCard ? accent : cyan, 0.65);
        dc.Pop();

        // Plane 2: Lower-right vertical isometric pillar moving in counter-depth.
        dc.PushTransform(BuildTransform(cx, cy, (1.8 * wave) + (effCx * 1.6), (1.8 * wave) - (1.4 * ease) + (effCy * 1.2), 0, scale, scale, skewYDeg: (-4.8 * wave) + (effCx * 3.5)));
        dc.DrawImage(planes.Second, fitRect);
        DrawDiagonalSheen(dc, planes.Second, fitRect, age + 0.4, effCx * 0.2, cyan, isCard ? accent : cyan, 0.65);
        dc.Pop();

        return true;
    }

    // 16. VS CODE: Left '<' chevron wing and right vertical ribbon loop scissor in 3D torsion.
    private static bool TryDrawVSCodeAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double effCx, double effCy,
        Color accent, Color cyan, bool isCard)
    {
        if (CyberIconAnatomy.SplitSeam(logo, seamKind: 4) is not { } ribbon) return false;

        double flex = Math.Sin(age * 4.0) * ease;
        double scale = 1.0 + (0.045 * ease);

        dc.PushTransform(BuildTransform(cx, cy, (-1.5 * flex) + (effCx * 1.4), (-1.2 * ease) + (effCy * 1.2), -3.5 * flex, scale, scale));
        dc.DrawImage(ribbon.First, fitRect);
        dc.Pop();

        dc.PushTransform(BuildTransform(cx, cy, (1.5 * flex) + (effCx * 1.4), (-1.2 * ease) + (effCy * 1.2), 3.5 * flex, scale, scale));
        dc.DrawImage(ribbon.Second, fitRect);
        DrawDiagonalSheen(dc, ribbon.Second, fitRect, age, effCx * 0.2, cyan, isCard ? accent : cyan, 0.60);
        dc.Pop();

        return true;
    }

    // 17. RIVATUNER (RTSS): Stand base stays planted while monitor tilts, top-left '60' FPS OSD badge pulses, and bottom slider sweeps.
    private static bool TryDrawRivatunerAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double effCx, double effCy,
        Color accent, Color cyan)
    {
        if (CyberIconAnatomy.GetRivatunerAnatomy(logo) is not { } rtss) return false;

        // Part 1: Grounded monitor stand base.
        dc.DrawImage(rtss.StandBase, fitRect);

        // Part 2: Monitor screen tilting on its hinge.
        double hingeY = fitRect.Top + (fitRect.Height * 0.78);
        double tiltSy = 1.0 - (0.05 * Math.Pow(Math.Sin(age * 2.5), 2.0) * ease);
        dc.PushTransform(BuildTransform(cx, hingeY, effCx * 1.4, -1.6 * ease, 0, 1.0 + (0.04 * ease), tiltSy, skewXDeg: effCx * -2.8));
        dc.DrawImage(rtss.ScreenBody, fitRect);
        DrawLaserScan(dc, rtss.ScreenBody, fitRect, age, cyan, accent);

        // Part 3: Top-left '60' FPS OSD counter badge pulsing with cyan telemetry highlight.
        if (rtss.FpsBadge is { } badge)
        {
            double bx = fitRect.Left + (fitRect.Width * badge.NormalizedCenter.X);
            double by = fitRect.Top + (fitRect.Height * badge.NormalizedCenter.Y);
            double pulse = 1.0 + (0.18 * Math.Pow(Math.Max(0, Math.Sin(age * 6.0)), 2.0) * ease);
            dc.PushTransform(BuildTransform(bx, by, 0, 0, 0, pulse, pulse));
            DrawSilhouette(dc, badge.Bitmap, fitRect, CachedBrush(Color.FromArgb((byte)(210 * ease), accent.R, accent.G, accent.B)));
            dc.Pop();
        }

        // Part 4: Bottom bezel progress slider sweeping left and right.
        if (rtss.BezelSlider is { } slider)
        {
            double slideX = (4.5 + (4.5 * Math.Sin(age * 4.2))) * ease;
            dc.PushTransform(new TranslateTransform(slideX, 0));
            DrawSilhouette(dc, slider.Bitmap, fitRect, CachedBrush(Color.FromArgb((byte)(235 * ease), cyan.R, cyan.G, cyan.B)));
            dc.Pop();
        }

        dc.Pop();
        return true;
    }

    // 18. GITHUB DESKTOP: Circular badge stays round while the extracted inner Octocat silhouette pops up and wags/nods inside.
    private static bool TryDrawGitHubAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double effCx, double effCy,
        Color accent, Color cyan, Color backdrop)
    {
        if (CyberIconAnatomy.GetCutoutStencil(logo, isCircularDisc: true) is not { Parts.Count: >= 1 } stencil) return false;

        var cat = stencil.Parts.OrderByDescending(p => p.PixelCount).First();

        // Part 1: Stationary circular badge.
        double discScale = 1.0 + (0.03 * ease);
        dc.PushTransform(BuildTransform(cx, cy, effCx * 1.0, (-0.8 * ease) + (effCy * 0.8), 0, discScale, discScale));
        dc.DrawImage(stencil.FilledBase, fitRect);

        // Part 2: Inner Octocat silhouette nodding & wagging around its bottom base.
        double catPivotX = fitRect.Left + (fitRect.Width * cat.NormalizedCenter.X);
        double catPivotY = fitRect.Top + (fitRect.Height * Math.Min(0.88, cat.NormalizedBounds.Bottom));
        double wagAngle = Math.Sin(age * 4.2) * 9.5 * ease;
        double hopY = -Math.Abs(Math.Sin(age * 4.2)) * 2.4 * ease;

        dc.PushTransform(BuildTransform(catPivotX, catPivotY, 0, hopY, wagAngle, 1.0 + (0.04 * ease), 1.0 + (0.04 * ease)));
        DrawIconPart(dc, cat, fitRect, backdrop, cyan, accent, ease, chromaOffset: 1.3);
        dc.Pop();

        dc.Pop();
        return true;
    }

    // 19. WINDOWS SETTINGS: Outer 8-tooth gear ratchets in stepped 45° clicks while the inner bright bearing ring counter-rotates.
    private static bool TryDrawSettingsGearAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double active, double settleProgress,
        double effCx, double effCy, Color accent, Color cyan)
    {
        if (CyberIconAnatomy.GetToneAnatomy(logo, AnimatedIconKind.WindowsSettings) is not { } gear) return false;

        // Stepped 45° mechanical gear ratchet every 0.45s.
        double stepPeriod = 0.45;
        double stepIndex = Math.Floor(age / stepPeriod);
        double stepPhase = (age % stepPeriod) / stepPeriod;
        double steppedAngle = (stepIndex + EaseOutBackSubtle(Math.Clamp(stepPhase * 1.4, 0, 1))) * 45.0;
        double outerAngle = SettleAngle(steppedAngle, active, settleProgress);
        double innerAngle = SettleAngle(-age * 90.0, active, settleProgress);
        double scale = 1.0 + (0.04 * ease);

        dc.PushTransform(BuildTransform(cx, cy, effCx * 1.2, (-1.0 * ease) + (effCy * 1.0), outerAngle, scale, scale));
        dc.DrawImage(gear.BaseLayer, fitRect);
        dc.Pop();

        dc.PushTransform(BuildTransform(cx, cy, effCx * 1.6, (-1.0 * ease) + (effCy * 1.2), innerAngle, scale * 1.04, scale * 1.04));
        dc.DrawImage(gear.FrontLayer, fitRect);
        dc.Pop();

        return true;
    }

    // 19b. MSI AFTERBURNER: Dragon's upper head & wings roar/lunge against its coiled lower tail inside dual high-RPM turbine gauges.
    private static bool TryDrawMsiAfterburnerAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease,
        double effCx, double effCy, Color accent, Color cyan)
    {
        DrawMsiAfterburnerTurbines(dc, fitRect, cx, cy, age, ease, effCx, effCy, accent, cyan);

        if (CardIcons.SplitHorizontal(logo, 0.52) is not { } dragon) return false;

        double roar = Math.Sin(age * 5.2) * ease;
        double scale = 1.0 + ((0.04 + (0.04 * Math.Max(0, roar))) * ease);

        // Upper dragon head & wings lunging forward/upward on each roar pulse.
        dc.PushTransform(BuildTransform(cx, cy, (effCx * 1.6) + (0.8 * roar), (-1.6 * ease) - (1.8 * Math.Max(0, roar)) + (effCy * 1.2), 4.5 * roar, scale, scale));
        dc.DrawImage(dragon.Top, fitRect);
        DrawDiagonalSheen(dc, dragon.Top, fitRect, age, effCx * 0.2, cyan, accent, 0.72);
        dc.Pop();

        // Lower coiled dragon body & tail counter-balancing.
        dc.PushTransform(BuildTransform(cx, cy, (effCx * 1.4) - (0.8 * roar), (-1.0 * ease) + (1.2 * Math.Max(0, roar)) + (effCy * 1.2), -3.5 * roar, scale, scale));
        dc.DrawImage(dragon.Bottom, fitRect);
        DrawDiagonalSheen(dc, dragon.Bottom, fitRect, age + 0.25, effCx * 0.2, cyan, accent, 0.72);
        dc.Pop();

        return true;
    }

    // 20. AUTOMATIC TOPOLOGICAL ANATOMY FOR ANY OTHER / UNKNOWN ICON:
    // Inspects the icon's actual islands, interior cutout holes, or tonal layers and animates the discovered parts.
    private static bool TryDrawAutomaticAnatomy(
        DrawingContext dc, ImageSource logo, AnimatedIconKind kind, Rect fitRect,
        double cx, double cy, double age, double ease, double active, double settleProgress,
        double effCx, double effCy, Color accent, Color cyan, Color backdrop, bool isCard)
    {
        // Case A: Multi-island icon (2..8 separate connected islands) — articulate each island along its radial/orbital vector.
        if (CyberIconAnatomy.GetConnectedIslands(logo) is { Count: >= 2 } islands)
        {
            var profile = AnimatedIconCatalog.MotionFor(kind);
            double baseSpin = profile == IconMotionProfile.RotorSpin && kind != AnimatedIconKind.MSIAfterburner
                ? SettleAngle(IntegrateEasedSpin(age, 52.0, 0.30), active, settleProgress)
                : 0;

            if (kind == AnimatedIconKind.MSIAfterburner)
            {
                DrawMsiAfterburnerTurbines(dc, fitRect, cx, cy, age, ease, effCx, effCy, accent, cyan);
            }

            dc.PushTransform(BuildTransform(cx, cy, effCx * 1.4, (-1.2 * ease) + (effCy * 1.2), baseSpin, 1.0 + (0.04 * ease), 1.0 + (0.04 * ease)));
            for (int i = 0; i < islands.Count; i++)
            {
                var isl = islands[i];
                double wave = Math.Sin((age * 4.8) - (i * 1.05)) * ease;
                double dx = isl.RadialDir.X * 2.0 * wave;
                double dy = isl.RadialDir.Y * 2.0 * wave;
                double ix = fitRect.Left + (fitRect.Width * isl.NormalizedCenter.X);
                double iy = fitRect.Top + (fitRect.Height * isl.NormalizedCenter.Y);
                double iScale = 1.0 + (0.05 * Math.Max(0, wave));

                dc.PushTransform(BuildTransform(ix, iy, dx, dy, wave * 3.5, iScale, iScale));
                dc.DrawImage(isl.Bitmap, fitRect);
                DrawDiagonalSheen(dc, isl.Bitmap, fitRect, age, effCx * 0.2, cyan, isCard ? accent : cyan, 0.55);
                dc.Pop();
            }
            dc.Pop();
            return true;
        }

        // Case B: Cutout stencil icon (solid plate/disc with 1..5 interior holes) — ground the plate and animate the inner cutout symbols.
        if (CyberIconAnatomy.GetCutoutStencil(logo) is { Parts.Count: >= 1 } stencil)
        {
            dc.PushTransform(BuildTransform(cx, cy, effCx * 1.0, (-0.8 * ease) + (effCy * 0.8), 0, 1.0 + (0.025 * ease), 1.0 + (0.025 * ease)));
            dc.DrawImage(stencil.FilledBase, fitRect);
            DrawDiagonalSheen(dc, stencil.FilledBase, fitRect, age, effCx * 0.2, cyan, isCard ? accent : cyan, 0.40);

            for (int i = 0; i < stencil.Parts.Count; i++)
            {
                var part = stencil.Parts[i];
                double px = fitRect.Left + (fitRect.Width * part.NormalizedCenter.X);
                double py = fitRect.Top + (fitRect.Height * part.NormalizedCenter.Y);
                double pulse = Math.Sin((age * 4.6) - (i * 0.9)) * ease;
                double pScale = 1.0 + (0.10 * Math.Abs(pulse));

                dc.PushTransform(BuildTransform(px, py, effCx * 1.4, -2.4 * Math.Abs(pulse), pulse * 7.5, pScale, pScale));
                DrawIconPart(dc, part, fitRect, backdrop, cyan, accent, ease, chromaOffset: 1.2);
                dc.Pop();
            }
            dc.Pop();
            return true;
        }

        // Case C: Multi-tone shaded icon — ground the base backdrop and float/articulate the foreground emblem in 3D parallax.
        if (CyberIconAnatomy.GetToneAnatomy(logo, kind) is { } tone)
        {
            dc.PushTransform(BuildTransform(cx, cy, effCx * 0.8, effCy * 0.8, 0, 1.0 + (0.02 * ease), 1.0 + (0.02 * ease)));
            dc.DrawImage(tone.BaseLayer, fitRect);
            dc.Pop();

            double fx = fitRect.Left + (fitRect.Width * tone.FrontCenter.X);
            double fy = fitRect.Top + (fitRect.Height * tone.FrontCenter.Y);
            double floatY = ((-3.5 + (1.6 * Math.Sin(age * 3.6))) * ease) + (effCy * 1.5);
            double tilt = (Math.Sin(age * 2.8) * 4.5 * ease) + (effCx * 3.5);

            dc.PushTransform(BuildTransform(fx, fy, effCx * 2.2, floatY, tilt, 1.0 + (0.065 * ease), 1.0 + (0.065 * ease), skewXDeg: effCx * -3.2));
            dc.DrawImage(tone.FrontLayer, fitRect);
            DrawDiagonalSheen(dc, tone.FrontLayer, fitRect, age, effCx * 0.2, cyan, isCard ? accent : cyan, 0.58);
            dc.Pop();
            return true;
        }

        return false;
    }

    private static void DrawIconPart(
        DrawingContext dc,
        CyberIconAnatomy.IconPart part,
        Rect fitRect,
        Color backdrop,
        Color cyan,
        Color accent,
        double ease,
        double chromaOffset)
    {
        if (part.IsCutoutHole)
        {
            if (ease > 0.04 && chromaOffset > 0.1)
            {
                byte rimA = (byte)(185 * ease);
                dc.PushTransform(new TranslateTransform(-chromaOffset * ease, 0));
                DrawSilhouette(dc, part.Bitmap, fitRect, CachedBrush(Color.FromArgb(rimA, cyan.R, cyan.G, cyan.B)));
                dc.Pop();

                dc.PushTransform(new TranslateTransform(chromaOffset * ease, 0));
                DrawSilhouette(dc, part.Bitmap, fitRect, CachedBrush(Color.FromArgb(rimA, accent.R, accent.G, accent.B)));
                dc.Pop();
            }

            DrawSilhouette(dc, part.Bitmap, fitRect, CachedBrush(backdrop));
        }
        else
        {
            if (ease > 0.05 && chromaOffset > 0.1)
            {
                dc.PushTransform(new TranslateTransform(0, chromaOffset * ease));
                DrawSilhouette(dc, part.Bitmap, fitRect, CachedBrush(Color.FromArgb((byte)(170 * ease), cyan.R, cyan.G, cyan.B)));
                dc.Pop();
            }

            dc.DrawImage(part.Bitmap, fitRect);
        }
    }

    private static void DrawMsiAfterburnerTurbines(
        DrawingContext dc, Rect fitRect, double cx, double cy, double age, double ease, double effCx, double effCy, Color accent, Color cyan)
    {
        if (ease <= 0.04) return;
        var center = new Point(cx + (effCx * 0.7), cy + (effCy * 0.7));
        double outerR = Math.Min(fitRect.Width, fitRect.Height) * 0.55;
        double innerR = Math.Min(fitRect.Width, fitRect.Height) * 0.46;
        var outerPen = CachedPen(Color.FromArgb((byte)(220 * ease), cyan.R, cyan.G, cyan.B), 1.8);
        var innerPen = CachedPen(Color.FromArgb((byte)(230 * ease), accent.R, accent.G, accent.B), 1.6);
        for (int i = 0; i < 3; i++)
        {
            DrawArc(dc, center, outerR, (age * 165.0) + (i * 120.0), 52.0, outerPen);
            DrawArc(dc, center, innerR, (-age * 210.0) + (i * 120.0) + 25.0, 36.0, innerPen);
        }
    }

    // Fallback for single-layer solid icons when no multi-part decomposition applies:
    // splits inner core relief (r <= 0.58) from outer perimeter so the inner relief still articulates in 3D depth.
    private static void DrawFallbackMotion(
        DrawingContext dc, ImageSource logo, AnimatedIconKind kind, Rect fitRect,
        double cx, double cy, double age, double ease, double active, double settleProgress,
        double effCx, double effCy, Color accent, Color cyan, bool isCard)
    {
        var profile = AnimatedIconCatalog.MotionFor(kind);
        if (kind == AnimatedIconKind.MSIAfterburner)
        {
            DrawMsiAfterburnerTurbines(dc, fitRect, cx, cy, age, ease, effCx, effCy, accent, cyan);
        }

        if (CardIcons.SplitRadial(logo, 0.56) is { } relief)
        {
            double outerAngle = profile == IconMotionProfile.RotorSpin && kind != AnimatedIconKind.MSIAfterburner
                ? SettleAngle(IntegrateEasedSpin(age, 52.0, 0.30), active, settleProgress)
                : (Math.Sin(age * 3.0) * 3.0 * ease);
            double innerBounce = Math.Sin(age * 4.5) * 1.8 * ease;

            dc.PushTransform(BuildTransform(cx, cy, effCx * 1.2, (-1.0 * ease) + (effCy * 1.0), outerAngle, 1.0 + (0.035 * ease), 1.0 + (0.035 * ease)));
            dc.DrawImage(relief.Outer, fitRect);
            dc.Pop();

            dc.PushTransform(BuildTransform(cx, cy, effCx * 2.2, (-2.2 * ease) + innerBounce + (effCy * 1.8), outerAngle * 0.5, 1.0 + (0.075 * ease), 1.0 + (0.075 * ease), skewXDeg: effCx * -3.2));
            dc.DrawImage(relief.Inner, fitRect);
            DrawDiagonalSheen(dc, relief.Inner, fitRect, age, effCx * 0.22, cyan, isCard ? accent : cyan, 0.65);
            dc.Pop();
            return;
        }

        dc.PushTransform(BuildTransform(cx, cy, effCx * 2.0, (-1.8 * ease) + (effCy * 1.5), 0, 1.0 + (0.05 * ease), 1.0 + (0.05 * ease), skewXDeg: effCx * -3.5));
        dc.DrawImage(logo, fitRect);
        DrawDiagonalSheen(dc, logo, fitRect, age, effCx * 0.22, cyan, isCard ? accent : cyan, 0.65);
        dc.Pop();
    }

    private static void DrawOpenCodeInteriorTelemetry(
        DrawingContext dc, Rect fitRect, double cx, double cy, double age, double ease, Color cyan, Color accent)
    {
        double boxW = fitRect.Width * 0.26;
        double boxH = fitRect.Height * 0.26;
        double left = cx - (boxW * 0.5);
        double top = cy - (boxH * 0.5);

        var barBrush = CachedBrush(Color.FromArgb((byte)(215 * ease), cyan.R, cyan.G, cyan.B));
        for (int r = 0; r < 3; r++)
        {
            double frac = 0.40 + (0.55 * (0.5 + (0.5 * Math.Sin((age * 7.5) + (r * 1.4)))));
            dc.DrawRectangle(barBrush, null, new Rect(left, top + (r * (boxH * 0.32)), boxW * frac, 1.6));
        }

        if (((int)(age * 4.5) % 2) == 0)
        {
            dc.DrawRectangle(
                CachedBrush(Color.FromArgb((byte)(240 * ease), accent.R, accent.G, accent.B)),
                null,
                new Rect(left, top + (boxH * 0.72), 3.4, 4.2));
        }
    }

    private static void DrawRadialSynapticPulse(
        DrawingContext dc, ImageSource logo, Rect fitRect, double age, double ease, Color cyan, Color accent)
    {
        double u = (age % 1.15) / 1.15;
        double fade = Math.Sin(u * Math.PI) * ease;
        byte coreA = (byte)(230 * fade);
        byte edgeA = (byte)(165 * fade);
        if (coreA < 6) return;

        var radial = new RadialGradientBrush
        {
            Center = new Point(0.54, 0.48),
            GradientOrigin = new Point(0.54, 0.48),
            RadiusX = 0.65,
            RadiusY = 0.65,
        };
        double r = u * 1.15;
        radial.GradientStops.Add(new GradientStop(Colors.Transparent, r - 0.22));
        radial.GradientStops.Add(new GradientStop(Color.FromArgb(edgeA, cyan.R, cyan.G, cyan.B), r - 0.07));
        radial.GradientStops.Add(new GradientStop(Color.FromArgb(coreA, 0xF8, 0xFA, 0xFF), r));
        radial.GradientStops.Add(new GradientStop(Color.FromArgb(edgeA, accent.R, accent.G, accent.B), r + 0.07));
        radial.GradientStops.Add(new GradientStop(Colors.Transparent, r + 0.22));
        radial.Freeze();

        DrawSilhouette(dc, logo, fitRect, radial);
    }

    private static void DrawHorizontalOscilloscope(
        DrawingContext dc, ImageSource logo, Rect fitRect, double age, double ease, Color cyan, Color accent)
    {
        double u = (age % 1.15) / 1.15;
        double beamX = fitRect.Left + (fitRect.Width * u);
        double trailW = Math.Min(fitRect.Width * 0.26, Math.Max(0, beamX - fitRect.Left));
        byte env = (byte)(235 * Math.Sin(u * Math.PI) * ease);
        if (env < 6) return;

        dc.PushOpacityMask(MaskBrush(logo, fitRect));
        if (trailW > 1.0)
        {
            var trail = new LinearGradientBrush(
                Color.FromArgb(0, cyan.R, cyan.G, cyan.B),
                Color.FromArgb((byte)(env * 0.50), cyan.R, cyan.G, cyan.B),
                new Point(0, 0),
                new Point(1, 0));
            trail.Freeze();
            dc.DrawRectangle(trail, null, new Rect(beamX - trailW, fitRect.Y, trailW, fitRect.Height));
        }

        dc.DrawLine(CachedPen(Color.FromArgb(env, cyan.R, cyan.G, cyan.B), 1.8), new Point(beamX, fitRect.Top), new Point(beamX, fitRect.Bottom));
        dc.DrawLine(
            CachedPen(Color.FromArgb(env, accent.R, accent.G, accent.B), 1.4),
            new Point(beamX, fitRect.Top + (fitRect.Height * 0.30)),
            new Point(beamX, fitRect.Bottom - (fitRect.Height * 0.30)));
        dc.Pop();
    }

    private static void DrawMiniEqualizerAndSparks(
        DrawingContext dc, Rect fitRect, double cx, double age, double ease, Color cyan, Color accent)
    {
        const int bars = 4;
        const double barW = 2.4;
        const double pitch = 4.2;
        double totalW = ((bars - 1) * pitch) + barW;
        double startX = cx - (totalW * 0.5);
        double baseY = fitRect.Bottom + 2.5;

        for (int b = 0; b < bars; b++)
        {
            double level = 0.25 + (0.75 * (0.5 + (0.5 * Math.Sin((age * (8.2 + (b * 1.7))) + (b * 1.3)))));
            double h = (2.0 + (level * 4.5)) * ease;
            var c = b % 2 == 0 ? cyan : accent;
            dc.DrawRectangle(CachedBrush(Color.FromArgb((byte)(220 * ease), c.R, c.G, c.B)), null, new Rect(startX + (b * pitch), baseY - h, barW, h));
        }
    }

    private static void DrawCompressionJaws(DrawingContext dc, Rect fitRect, double age, double ease, Color cyan)
    {
        double clamp = Math.Pow(Math.Max(0, Math.Sin(age * 3.8)), 3.2) * ease;
        double inset = 2.4 * clamp;
        var pen = CachedPen(Color.FromArgb((byte)(220 * ease), cyan.R, cyan.G, cyan.B), 1.5);
        double tick = Math.Max(4.0, fitRect.Width * 0.14);

        double leftX = fitRect.Left - 3.0 + inset;
        double rightX = fitRect.Right + 3.0 - inset;
        double top = fitRect.Top + (fitRect.Height * 0.14);
        double bottom = fitRect.Bottom - (fitRect.Height * 0.14);

        dc.DrawLine(pen, new Point(leftX + tick, top), new Point(leftX, top));
        dc.DrawLine(pen, new Point(leftX, top), new Point(leftX, bottom));
        dc.DrawLine(pen, new Point(leftX, bottom), new Point(leftX + tick, bottom));

        dc.DrawLine(pen, new Point(rightX - tick, top), new Point(rightX, top));
        dc.DrawLine(pen, new Point(rightX, top), new Point(rightX, bottom));
        dc.DrawLine(pen, new Point(rightX, bottom), new Point(rightX - tick, bottom));
    }

    private static void DrawAcousticEcho(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, Color accent, Color cyan)
    {
        if (ease <= 0.02) return;
        for (int wave = 0; wave < 2; wave++)
        {
            double t = ((age * 1.45) + (wave * 0.5)) % 1.0;
            double echoScale = 1.0 + (0.24 * t);
            byte alpha = (byte)(135 * Math.Pow(1.0 - t, 1.7) * ease);
            if (alpha < 6) continue;

            var color = wave == 0 ? cyan : accent;
            dc.PushTransform(BuildTransform(cx, cy, 0, 0, 0, echoScale, echoScale));
            DrawSilhouette(dc, logo, fitRect, CachedBrush(Color.FromArgb(alpha, color.R, color.G, color.B)));
            dc.Pop();
        }
    }

    private static void DrawDiagonalSheen(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double age, double cursorOffset, Color cyan, Color accent, double peakOpacity)
    {
        const double sweepSec = 0.65;
        const double periodSec = 1.55;
        double t = age % periodSec;
        if (t > sweepSec) return;

        double u = t / sweepSec;
        double center = -0.25 + (1.50 * u) + cursorOffset;
        double fade = Math.Sin(u * Math.PI) * peakOpacity;
        byte edgeAlpha = (byte)(130 * fade);
        byte coreAlpha = (byte)(210 * fade);
        if (coreAlpha < 4) return;

        var band = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        band.GradientStops.Add(new GradientStop(Colors.Transparent, center - 0.11));
        band.GradientStops.Add(new GradientStop(Color.FromArgb(edgeAlpha, cyan.R, cyan.G, cyan.B), center - 0.035));
        band.GradientStops.Add(new GradientStop(Color.FromArgb(coreAlpha, 0xF8, 0xFA, 0xFF), center));
        band.GradientStops.Add(new GradientStop(Color.FromArgb(edgeAlpha, accent.R, accent.G, accent.B), center + 0.035));
        band.GradientStops.Add(new GradientStop(Colors.Transparent, center + 0.11));
        band.Freeze();

        DrawSilhouette(dc, logo, fitRect, band);
    }

    private static void DrawLaserScan(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double age, Color cyan, Color accent, bool upward = false)
    {
        const double periodSec = 1.30;
        double u = (age % periodSec) / periodSec;
        double posU = upward ? (1.0 - u) : u;
        double beamY = fitRect.Top + (fitRect.Height * posU);
        double trailH = upward
            ? Math.Min(fitRect.Height * 0.22, Math.Max(0, fitRect.Bottom - beamY))
            : Math.Min(fitRect.Height * 0.22, Math.Max(0, beamY - fitRect.Top));
        byte env = (byte)(230 * Math.Sin(u * Math.PI));
        if (env < 6) return;

        dc.PushOpacityMask(MaskBrush(logo, fitRect));

        if (trailH > 1.0)
        {
            var trail = upward
                ? new LinearGradientBrush(Color.FromArgb((byte)(env * 0.45), cyan.R, cyan.G, cyan.B), Color.FromArgb(0, cyan.R, cyan.G, cyan.B), new Point(0, 0), new Point(0, 1))
                : new LinearGradientBrush(Color.FromArgb(0, cyan.R, cyan.G, cyan.B), Color.FromArgb((byte)(env * 0.45), cyan.R, cyan.G, cyan.B), new Point(0, 0), new Point(0, 1));
            trail.Freeze();
            double rectY = upward ? beamY : beamY - trailH;
            dc.DrawRectangle(trail, null, new Rect(fitRect.X, rectY, fitRect.Width, trailH));
        }

        dc.DrawLine(CachedPen(Color.FromArgb(env, cyan.R, cyan.G, cyan.B), 1.7), new Point(fitRect.Left, beamY), new Point(fitRect.Right, beamY));
        dc.DrawLine(CachedPen(Color.FromArgb(env, accent.R, accent.G, accent.B), 1.7), new Point(fitRect.Left + (fitRect.Width * 0.35), beamY), new Point(fitRect.Left + (fitRect.Width * 0.65), beamY));
        dc.Pop();
    }

    private static void DrawArc(DrawingContext dc, Point center, double radius, double startDeg, double sweepDeg, Pen pen)
    {
        double r0 = startDeg * Math.PI / 180.0;
        double r1 = (startDeg + sweepDeg) * Math.PI / 180.0;
        var p0 = new Point(center.X + (Math.Cos(r0) * radius), center.Y + (Math.Sin(r0) * radius));
        var p1 = new Point(center.X + (Math.Cos(r1) * radius), center.Y + (Math.Sin(r1) * radius));
        var seg = new ArcSegment(p1, new Size(radius, radius), 0, Math.Abs(sweepDeg) > 180, sweepDeg >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true);
        var fig = new PathFigure(p0, new PathSegment[] { seg }, false);
        var geo = new PathGeometry(new[] { fig });
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    private static Geometry ChamferedRect(Rect r, double cut)
    {
        double c = Math.Min(cut, Math.Min(r.Width, r.Height) * 0.35);
        var fig = new PathFigure
        {
            IsClosed = true,
            IsFilled = true,
            StartPoint = new Point(r.Left + c, r.Top),
        };
        fig.Segments.Add(new PolyLineSegment(new[]
        {
            new Point(r.Right, r.Top),
            new Point(r.Right, r.Bottom - c),
            new Point(r.Right - c, r.Bottom),
            new Point(r.Left, r.Bottom),
            new Point(r.Left, r.Top + c),
        }, true));
        var geo = new PathGeometry(new[] { fig });
        geo.Freeze();
        return geo;
    }

    private static ImageBrush MaskBrush(ImageSource logo, Rect fitRect)
    {
        var brush = new ImageBrush(logo)
        {
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = fitRect,
            Stretch = Stretch.Fill,
        };
        brush.Freeze();
        return brush;
    }

    private static void DrawSilhouette(DrawingContext dc, ImageSource logo, Rect fitRect, Brush fill)
    {
        dc.PushOpacityMask(MaskBrush(logo, fitRect));
        dc.DrawRectangle(fill, null, fitRect);
        dc.Pop();
    }

    private static Transform BuildTransform(
        double px, double py, double tx, double ty,
        double angleDeg, double scaleX, double scaleY,
        double skewXDeg = 0, double skewYDeg = 0)
    {
        if (angleDeg == 0 && scaleX == 1.0 && scaleY == 1.0 && skewXDeg == 0 && skewYDeg == 0 && tx == 0 && ty == 0)
        {
            return Transform.Identity;
        }

        var m = Matrix.Identity;
        m.Translate(-px, -py);
        if (scaleX != 1.0 || scaleY != 1.0) m.Scale(scaleX, scaleY);
        if (skewXDeg != 0 || skewYDeg != 0) m.Skew(skewXDeg, skewYDeg);
        if (angleDeg != 0) m.Rotate(angleDeg);
        m.Translate(px + tx, py + ty);
        return new MatrixTransform(m);
    }

    private static Rect UniformFit(Rect bounds, ImageSource img)
    {
        double w = img.Width, h = img.Height;
        if (w <= 0 || h <= 0) return bounds;
        double scale = Math.Min(bounds.Width / w, bounds.Height / h);
        double fitW = w * scale, fitH = h * scale;
        return new Rect(bounds.X + ((bounds.Width - fitW) * 0.5), bounds.Y + ((bounds.Height - fitH) * 0.5), fitW, fitH);
    }

    private static Geometry FrozenRect(Rect rect)
    {
        var g = new RectangleGeometry(rect);
        g.Freeze();
        return g;
    }

    private static SolidColorBrush CachedBrush(Color c)
    {
        if (BrushCache.TryGetValue(c, out var known)) return known;
        if (BrushCache.Count > 256) BrushCache.Clear();
        var b = new SolidColorBrush(c);
        b.Freeze();
        return BrushCache[c] = b;
    }

    private static Pen CachedPen(Color c, double thickness)
    {
        var key = (c, thickness);
        if (PenCache.TryGetValue(key, out var known)) return known;
        if (PenCache.Count > 256) PenCache.Clear();
        var p = new Pen(CachedBrush(c), thickness);
        p.Freeze();
        return PenCache[key] = p;
    }

    private static double IntegrateEasedSpin(double age, double speedDegPerSec, double rampSec)
    {
        if (age <= 0) return 0;
        if (age < rampSec) return 0.5 * speedDegPerSec * (age * age) / rampSec;
        return (0.5 * speedDegPerSec * rampSec) + (speedDegPerSec * (age - rampSec));
    }

    private static double SettleAngle(double liveAngle, double active, double settleProgress)
    {
        if (settleProgress <= 0) return liveAngle;
        double mod = ((liveAngle % 360.0) + 360.0) % 360.0;
        return mod <= 180.0 ? mod * active : 360.0 - ((360.0 - mod) * active);
    }

    private static double EaseOutCubic(double t) => 1.0 - Math.Pow(1.0 - t, 3.0);

    private static double EaseOutBackSubtle(double t)
    {
        const double s = 1.15;
        double tm = t - 1.0;
        return 1.0 + ((s + 1.0) * tm * tm * tm) + (s * tm * tm);
    }
}
