using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floe.App.Services;
using Floe.Core.Services;

namespace Floe.App.Effects.Cyber;

/// <summary>
/// Animates dock icons by articulating each icon's real internal parts — connected islands, negative-space
/// cutout symbols, tonal depth planes, and structural seams — strictly inside the symbol's own geometry,
/// preserving 100% of each icon's true colors without color-tinting filters or external clutter.
/// </summary>
public static class CyberAnimatedIcons
{
    private static readonly Dictionary<Color, SolidColorBrush> BrushCache = new();

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
            AnimatedIconKind.FileExplorer => TryDrawFolderAnatomy(dc, logo, fitRect, cx, cy, age, ease, active, effCx, effCy, isCard),
            AnimatedIconKind.Chrome => TryDrawChromeAnatomy(dc, logo, fitRect, cx, cy, age, ease, active, settleProgress, effCx, effCy),
            AnimatedIconKind.Steam => TryDrawSteamAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy, backdrop, isCard),
            AnimatedIconKind.AppleMusic => TryDrawAppleMusicAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy, backdrop),
            AnimatedIconKind.Spotify => TryDrawSpotifyAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy, backdrop),
            AnimatedIconKind.Antigravity => TryDrawAntigravityAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy),
            AnimatedIconKind.OpenCode => TryDrawOpenCodeAnatomy(dc, logo, fitRect, cx, cy, age, ease, active, effCx, effCy),
            AnimatedIconKind.Claude => TryDrawClaudeAnatomy(dc, logo, fitRect, age, ease, active, settleProgress, effCx, effCy),
            AnimatedIconKind.OrcaSlicer => TryDrawOrcaSlicerAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy),
            AnimatedIconKind.TaskManager => TryDrawTaskManagerAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy),
            AnimatedIconKind.Blender => TryDrawBlenderAnatomy(dc, logo, fitRect, age, ease, effCx, effCy),
            AnimatedIconKind.Notepad => TryDrawNotepadAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy),
            AnimatedIconKind.Terminal => TryDrawTerminalAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy, backdrop),
            AnimatedIconKind.SevenZip => TryDrawSevenZipAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy),
            AnimatedIconKind.Fusion => TryDrawFusionAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy),
            AnimatedIconKind.VSCode => TryDrawVSCodeAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy),
            AnimatedIconKind.RivaTuner => TryDrawRivatunerAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy),
            AnimatedIconKind.GitHub => TryDrawGitHubAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy, backdrop),
            AnimatedIconKind.WindowsSettings => TryDrawSettingsGearAnatomy(dc, logo, fitRect, cx, cy, age, ease, active, settleProgress, effCx, effCy),
            AnimatedIconKind.MSIAfterburner => TryDrawMsiAfterburnerAnatomy(dc, logo, fitRect, cx, cy, age, ease, effCx, effCy),
            _ => TryDrawAutomaticAnatomy(dc, logo, kind, fitRect, cx, cy, age, ease, active, settleProgress, effCx, effCy, backdrop),
        };

        if (!handled)
        {
            DrawFallbackMotion(dc, logo, kind, fitRect, cx, cy, age, ease, active, settleProgress, effCx, effCy);
        }

        dc.Pop();
    }

    // 1. FILE EXPLORER: Grounded back folder wall & tab + symmetrical bottom-hinged front flap opening forward cleanly.
    private static bool TryDrawFolderAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double active, double effCx, double effCy, bool isCard)
    {
        if (CardIcons.SplitHorizontal(logo, isCard ? 0.23 : 0.27) is not { } split) return false;

        double open = EaseOutBackSubtle(Math.Clamp(age / 0.26, 0, 1)) * active;
        double breathe = Math.Sin(age * 2.6) * 0.012 * ease;
        double baseScale = 1.0 + (0.025 * ease);

        // Grounded back folder wall & tab.
        dc.PushTransform(BuildTransform(cx, cy, effCx * 0.5, (-1.0 * ease) + (effCy * 0.4), 0, baseScale, baseScale));
        dc.DrawImage(split.Top, fitRect);
        dc.Pop();

        // Subtle interior pocket shadow visible only along the open folder slit.
        if (open > 0.04)
        {
            double mouthY = fitRect.Top + (fitRect.Height * (isCard ? 0.235 : 0.275)) - (0.6 * ease);
            double mouthW = fitRect.Width * 0.74;
            double mouthH = Math.Max(1.2, fitRect.Height * 0.065 * open);
            dc.DrawRectangle(
                CachedBrush(Color.FromArgb((byte)(120 * open), 0x0A, 0x0C, 0x12)),
                null,
                new Rect(cx - (mouthW * 0.5), mouthY, mouthW, mouthH));
        }

        // Symmetrical bottom-hinged front flap leaning open in pure original yellow/gold.
        dc.PushTransform(BuildTransform(
            cx,
            fitRect.Bottom,
            effCx * 0.9,
            (-0.4 * ease) + (effCy * 0.5),
            0,
            1.0 + (0.025 * open),
            1.0 - ((0.095 + breathe) * open),
            skewXDeg: effCx * -1.8));
        dc.DrawImage(split.Bottom, fitRect);
        dc.Pop();
        return true;
    }

    // 2. GOOGLE CHROME: Smooth 3-petal shutter rotation with subtle cohesive aperture breath + cursor-tracking blue lens core.
    private static bool TryDrawChromeAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double active, double settleProgress,
        double effCx, double effCy)
    {
        double outerAngle = SettleAngle(IntegrateEasedSpin(age, speedDegPerSec: 46.0, rampSec: 0.30), active, settleProgress);
        var islands = CyberIconAnatomy.GetConnectedIslands(logo);

        if (islands is { Count: >= 4 })
        {
            var sorted = islands
                .OrderBy(i => Math.Sqrt(Math.Pow(i.NormalizedCenter.X - 0.5, 2) + Math.Pow(i.NormalizedCenter.Y - 0.5, 2)))
                .ToList();
            var core = sorted[0];
            var petals = sorted.Skip(1).ToList();

            double aperture = (0.55 + (0.45 * Math.Sin(age * 3.4))) * 0.85 * ease;
            dc.PushTransform(BuildTransform(cx, cy, effCx * 0.5, effCy * 0.5, outerAngle, 1.0 + (0.02 * ease), 1.0 + (0.02 * ease)));
            for (int i = 0; i < petals.Count; i++)
            {
                var petal = petals[i];
                dc.PushTransform(new TranslateTransform(petal.RadialDir.X * aperture, petal.RadialDir.Y * aperture));
                dc.DrawImage(petal.Bitmap, fitRect);
                dc.Pop();
            }
            dc.Pop();

            double innerScale = 1.0 + ((0.035 + (0.03 * Math.Cos(age * 3.4))) * ease);
            dc.PushTransform(BuildTransform(cx, cy, effCx * 2.1, effCy * 2.1, 0, innerScale, innerScale));
            dc.DrawImage(core.Bitmap, fitRect);
            dc.Pop();
            return true;
        }

        if (CardIcons.SplitRadial(logo, 0.415) is { } radial)
        {
            dc.PushTransform(BuildTransform(cx, cy, effCx * 0.5, effCy * 0.5, outerAngle, 1.0 + (0.025 * ease), 1.0 + (0.025 * ease)));
            dc.DrawImage(radial.Outer, fitRect);
            dc.Pop();

            double innerScale = 1.0 + ((0.035 + (0.03 * Math.Cos(age * 3.4))) * ease);
            dc.PushTransform(BuildTransform(cx, cy, effCx * 2.0, effCy * 2.0, 0, innerScale, innerScale));
            dc.DrawImage(radial.Inner, fitRect);
            dc.Pop();
            return true;
        }

        return false;
    }

    // 3. STEAM: Coupled mechanical locomotive linkage inside the stationary circular disc — axle hubs stay locked inside piston rings.
    private static bool TryDrawSteamAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double effCx, double effCy,
        Color backdrop, bool isCard)
    {
        if (!isCard || CyberIconAnatomy.GetSteamAnatomy(logo) is not { } steam) return false;

        double discScale = 1.0 + (0.02 * ease);
        dc.PushTransform(BuildTransform(cx, cy, effCx * 0.7, (-0.5 * ease) + (effCy * 0.7), 0, discScale, discScale));
        dc.DrawImage(steam.FilledDisc, fitRect);

        // Clip internal piston crank strictly inside the circular badge.
        double clipR = Math.Min(fitRect.Width, fitRect.Height) * 0.492;
        var discClip = new EllipseGeometry(new Point(cx, cy), clipR, clipR);
        discClip.Freeze();
        dc.PushClip(discClip);

        // Coupled crank kinematics: crank rod and both wheel hubs share the exact same linkage transform
        // around the upper flywheel axle so the inner navy hubs never drift out of the white ring sockets.
        double theta = age * 5.6;
        double crankR = fitRect.Width * 0.032 * ease;
        double crankTx = Math.Cos(theta) * crankR;
        double crankTy = Math.Sin(theta) * crankR;
        double rodRock = Math.Sin(theta - 0.785) * 4.2 * ease;

        double upperAxleX = steam.UpperWheel is { } uw0
            ? fitRect.Left + (fitRect.Width * uw0.NormalizedCenter.X)
            : fitRect.Left + (fitRect.Width * 0.655);
        double upperAxleY = steam.UpperWheel is { } uw1
            ? fitRect.Top + (fitRect.Height * uw1.NormalizedCenter.Y)
            : fitRect.Top + (fitRect.Height * 0.370);

        dc.PushTransform(BuildTransform(upperAxleX, upperAxleY, crankTx, crankTy, rodRock, 1.0, 1.0));

        if (steam.IsColored)
        {
            dc.DrawImage(steam.CrankCutout, fitRect);
        }
        else
        {
            DrawSilhouette(dc, steam.CrankCutout, fitRect, CachedBrush(backdrop));
        }

        if (steam.LowerWheel is { } lower)
        {
            double lcx = fitRect.Left + (fitRect.Width * lower.NormalizedCenter.X);
            double lcy = fitRect.Top + (fitRect.Height * lower.NormalizedCenter.Y);
            dc.PushTransform(BuildTransform(lcx, lcy, 0, 0, -theta * 57.2958, 1.0, 1.0));
            dc.DrawImage(lower.Bitmap, fitRect);
            dc.Pop();
        }

        if (steam.UpperWheel is { } upper)
        {
            double ucx = fitRect.Left + (fitRect.Width * upper.NormalizedCenter.X);
            double ucy = fitRect.Top + (fitRect.Height * upper.NormalizedCenter.Y);
            dc.PushTransform(BuildTransform(ucx, ucy, 0, 0, theta * 57.2958, 1.0, 1.0));
            dc.DrawImage(upper.Bitmap, fitRect);
            dc.Pop();
        }

        dc.Pop(); // linkageTransform
        dc.Pop(); // discClip
        dc.Pop(); // discTransform
        return true;
    }

    // 4. APPLE MUSIC: Grounded red rounded-square tile while the inner white '♫' note dances on a smooth musical beat.
    private static bool TryDrawAppleMusicAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double effCx, double effCy,
        Color backdrop)
    {
        if (CyberIconAnatomy.GetCutoutStencil(logo) is not { Parts.Count: >= 1 } stencil) return false;

        double beat = Math.Pow(Math.Max(0, Math.Sin(age * 5.6)), 2.4);
        double cushion = Math.Pow(Math.Max(0, -Math.Sin(age * 5.6)), 2.8);

        double tileScale = 1.0 + ((0.015 + (0.018 * beat)) * ease);
        dc.PushTransform(BuildTransform(cx, cy, effCx * 0.8, (-0.6 * ease) + (effCy * 0.8), 0, tileScale, tileScale));
        dc.DrawImage(stencil.FilledBase, fitRect);

        // Clip inside the tile silhouette so the dancing note stays cleanly framed.
        dc.PushOpacityMask(MaskBrush(stencil.FilledBase, fitRect));

        var note = stencil.Parts[0];
        double nx = fitRect.Left + (fitRect.Width * note.NormalizedCenter.X);
        double ny = fitRect.Top + (fitRect.Height * note.NormalizedCenter.Y);
        double noteTy = ((-2.8 * beat) + (0.9 * cushion)) * ease;
        double noteTx = Math.Sin(age * 2.8) * 1.3 * ease;
        double noteAngle = Math.Sin(age * 2.8) * 7.5 * ease;
        double noteSx = 1.0 + (((-0.04 * beat) + (0.045 * cushion)) * ease);
        double noteSy = 1.0 + (((0.07 * beat) - (0.045 * cushion)) * ease);

        dc.PushTransform(BuildTransform(nx, ny, noteTx, noteTy, noteAngle, noteSx, noteSy));
        DrawCleanPart(dc, note, fitRect, backdrop);
        dc.Pop();

        dc.Pop(); // tileMask
        dc.Pop(); // tileTransform
        return true;
    }

    // 5. SPOTIFY: Stationary green circular disc while the 3 concentric sound-wave arcs propagate an upward acoustic ripple.
    private static bool TryDrawSpotifyAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double effCx, double effCy,
        Color backdrop)
    {
        if (CyberIconAnatomy.GetCutoutStencil(logo, isCircularDisc: true) is not { Parts.Count: >= 2 } stencil) return false;

        double beat = Math.Pow(Math.Max(0, Math.Sin(age * 5.0)), 3.0);
        double discScale = 1.0 + ((0.02 + (0.02 * beat)) * ease);
        dc.PushTransform(BuildTransform(cx, cy, effCx * 0.9, (-0.6 * ease) + (effCy * 0.8), 0, discScale, discScale));
        dc.DrawImage(stencil.FilledBase, fitRect);

        var wavesBottomUp = stencil.Parts.OrderByDescending(p => p.NormalizedCenter.Y).ToList();
        for (int w = 0; w < wavesBottomUp.Count; w++)
        {
            var wave = wavesBottomUp[w];
            double phase = (age * 5.8) - (w * 0.88);
            double wavePulse = Math.Pow(Math.Max(0, Math.Sin(phase)), 2.2) * ease;

            double wx = fitRect.Left + (fitRect.Width * wave.NormalizedCenter.X);
            double wy = fitRect.Top + (fitRect.Height * wave.NormalizedCenter.Y);
            double dy = -(0.85 + (w * 0.55)) * wavePulse;
            double sx = 1.0 + ((0.045 + (w * 0.02)) * wavePulse);
            double sy = 1.0 + ((0.11 + (w * 0.025)) * wavePulse);

            dc.PushTransform(BuildTransform(wx, wy, 0, dy, 0, sx, sy));
            DrawCleanPart(dc, wave, fitRect, backdrop);
            dc.Pop();
        }

        dc.Pop();
        return true;
    }

    // 6. ANTIGRAVITY: Grounded space-navy rounded tile while the 4-color 'A' delta-wing glides smoothly in zero-G inside the tile.
    private static bool TryDrawAntigravityAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double effCx, double effCy)
    {
        if (CyberIconAnatomy.GetToneAnatomy(logo, AnimatedIconKind.Antigravity) is not { } tone) return false;

        double tileScale = 1.0 + (0.02 * ease);
        dc.PushTransform(BuildTransform(cx, cy, effCx * 0.6, effCy * 0.5, 0, tileScale, tileScale));
        dc.DrawImage(tone.BaseLayer, fitRect);

        // Clip inside the rounded tile so the hovering 'A' never breaches outside the card.
        dc.PushOpacityMask(MaskBrush(tone.BaseLayer, fitRect));

        double shipX = fitRect.Left + (fitRect.Width * tone.FrontCenter.X);
        double shipY = fitRect.Top + (fitRect.Height * tone.FrontCenter.Y);
        double liftY = ((-2.1 + (1.35 * Math.Sin(age * 3.0))) * ease) + (effCy * 1.1);
        double liftX = effCx * 1.8;
        double bankAngle = (Math.Sin(age * 2.0) * 3.0 * ease) + (effCx * 3.0);
        double shipScale = 1.0 + (0.045 * ease);

        dc.PushTransform(BuildTransform(shipX, shipY, liftX, liftY, bankAngle, shipScale, shipScale, skewXDeg: effCx * -2.2));
        dc.DrawImage(tone.FrontLayer, fitRect);
        dc.Pop();

        dc.Pop(); // tileMask
        dc.Pop(); // tileTransform
        return true;
    }

    // 7. OPENCODE: Left and Right monolith jaws part horizontally to reveal clean terminal code typing inside the dark chamber.
    private static bool TryDrawOpenCodeAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double active, double effCx, double effCy)
    {
        if (CyberIconAnatomy.SplitSeam(logo, seamKind: 2) is not { } jaws) return false;

        double open = EaseOutBackSubtle(Math.Clamp(age / 0.28, 0, 1)) * active;
        double splitPx = 2.6 * open;
        double scale = 1.0 + (0.03 * ease);

        dc.PushTransform(BuildTransform(cx, cy, effCx * 1.1, (-0.8 * ease) + (effCy * 0.9), 0, scale, scale));

        DrawOpenCodeInteriorTelemetry(dc, fitRect, cx, cy, age, ease);

        dc.PushTransform(new TranslateTransform(-splitPx, 0));
        dc.DrawImage(jaws.First, fitRect);
        dc.Pop();

        dc.PushTransform(new TranslateTransform(splitPx, 0));
        dc.DrawImage(jaws.Second, fitRect);
        dc.Pop();

        dc.Pop();
        return true;
    }

    // 8. CLAUDE: Rotates smoothly around its true core nexus while 3 interleaved 4-ray constellations gently breathe in warm coral.
    private static bool TryDrawClaudeAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double age, double ease, double active, double settleProgress,
        double effCx, double effCy)
    {
        if (CyberIconAnatomy.GetStarburstAnatomy(logo) is not { } star) return false;

        double coreX = fitRect.Left + (fitRect.Width * star.CoreCenter.X);
        double coreY = fitRect.Top + (fitRect.Height * star.CoreCenter.Y);
        double spin = SettleAngle(IntegrateEasedSpin(age, speedDegPerSec: 24.0, rampSec: 0.30), active, settleProgress);

        ReadOnlySpan<double> phases = [0.0, 2.094395, 4.188790];
        BitmapSource[] groups = [star.GroupA, star.GroupB, star.GroupC];

        for (int g = 0; g < 3; g++)
        {
            double rayPulse = 1.0 + ((0.025 + (0.065 * Math.Sin((age * 4.4) + phases[g]))) * ease);
            dc.PushTransform(BuildTransform(coreX, coreY, effCx * 1.1, (-0.8 * ease) + (effCy * 0.9), spin, rayPulse, rayPulse));
            dc.DrawImage(groups[g], fitRect);
            dc.Pop();
        }

        double hubScale = 1.0 + ((0.03 + (0.025 * Math.Cos(age * 4.4))) * ease);
        dc.PushTransform(BuildTransform(coreX, coreY, effCx * 1.1, (-0.8 * ease) + (effCy * 0.9), spin, hubScale, hubScale));
        dc.DrawImage(star.CoreHub, fitRect);
        dc.Pop();

        return true;
    }

    // 9. ORCASLICER: Stationary cool-white circular badge while the Teal middle slicer wedge glides along its diagonal cut between the upper and lower Orca halves.
    private static bool TryDrawOrcaSlicerAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double effCx, double effCy)
    {
        if (CyberIconAnatomy.GetOrcaSlicerAnatomy(logo) is not { } orca) return false;

        double scale = 1.0 + (0.03 * ease);
        dc.PushTransform(BuildTransform(cx, cy, effCx * 1.0, (-0.8 * ease) + (effCy * 0.8), 0, scale, scale));

        // Grounded circular silver-white badge.
        dc.DrawImage(orca.BaseDisc, fitRect);

        // Clip strictly inside the circular badge so the Orca and diagonal slice stay cleanly inside the rim.
        double clipR = Math.Min(fitRect.Width, fitRect.Height) * 0.468;
        var discClip = new EllipseGeometry(new Point(cx, cy), clipR, clipR);
        discClip.Freeze();
        dc.PushClip(discClip);

        double phase = age * 3.4;
        double poiseAngle = Math.Sin(phase * 0.65) * 3.2 * ease;
        double poiseTx = Math.Cos(phase * 0.65) * 0.8 * ease;
        double poiseTy = -Math.Sin(phase) * 0.9 * ease;
        dc.PushTransform(BuildTransform(cx, cy, poiseTx + (effCx * 0.7), poiseTy + (effCy * 0.7), poiseAngle, 1.0, 1.0));

        double tipX = fitRect.Left + (fitRect.Width * orca.TipNormalized.X);
        double tipY = fitRect.Top + (fitRect.Height * orca.TipNormalized.Y);
        double cutPulse = (0.5 - (0.5 * Math.Cos(phase))) * ease;

        // Upper and lower Orca halves stay flush along the cut without cracking open white gaps.
        dc.DrawImage(orca.LowerOrca, fitRect);
        dc.DrawImage(orca.UpperOrca, fitRect);

        // Teal middle slicer wedge scales radially from its exact bottom-left vertex so both diagonal seams stay 100% flush
        // while the Teal slicer blade cleanly emerges along the diagonal cut and retracts smoothly.
        double sliceScale = 1.0 + (0.085 * cutPulse);
        dc.PushTransform(BuildTransform(tipX, tipY, 0, 0, 0, sliceScale, sliceScale));
        dc.DrawImage(orca.MiddleSlice, fitRect);
        dc.Pop();

        dc.Pop(); // poiseTransform
        dc.Pop(); // discClip
        dc.Pop(); // baseTransform
        return true;
    }

    // 10. TASK MANAGER: Stationary monitor screen while Back and Front CPU mountain waveforms smoothly surge and scroll inside.
    private static bool TryDrawTaskManagerAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double effCx, double effCy)
    {
        if (CyberIconAnatomy.GetToneAnatomy(logo, AnimatedIconKind.TaskManager) is not { MidLayer: not null } tone) return false;

        double scale = 1.0 + (0.035 * ease);
        dc.PushTransform(BuildTransform(cx, cy, effCx * 1.1, (-0.8 * ease) + (effCy * 0.9), 0, scale, scale));

        dc.PushOpacityMask(MaskBrush(logo, fitRect));
        dc.DrawImage(tone.BaseLayer, fitRect);

        double backSy = 1.0 + (0.20 * Math.Sin(age * 4.0) * ease);
        double backTx = Math.Sin(age * 2.2) * 2.8 * ease;
        dc.PushTransform(BuildTransform(cx, fitRect.Bottom, backTx, 0, 0, 1.08, backSy));
        dc.DrawImage(tone.MidLayer, fitRect);
        dc.Pop();

        double frontSy = 1.0 + (0.22 * Math.Cos(age * 5.0) * ease);
        double frontTx = -Math.Cos(age * 2.7) * 2.5 * ease;
        dc.PushTransform(BuildTransform(cx, fitRect.Bottom, frontTx, 0, 0, 1.08, frontSy));
        dc.DrawImage(tone.FrontLayer, fitRect);
        dc.Pop();

        dc.Pop(); // monitor mask
        dc.Pop();
        return true;
    }

    // 11. BLENDER: Orange 3-fin body gently tilts while the cobalt-blue inner eye pupil tracks the cursor and dilates cleanly.
    private static bool TryDrawBlenderAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double age, double ease, double effCx, double effCy)
    {
        if (CyberIconAnatomy.GetConnectedIslands(logo) is not { Count: >= 2 } islands) return false;

        var sorted = islands.OrderByDescending(i => i.PixelCount).ToList();
        var body = sorted[0];
        var pupil = sorted[1];

        double eyeX = fitRect.Left + (fitRect.Width * pupil.NormalizedCenter.X);
        double eyeY = fitRect.Top + (fitRect.Height * pupil.NormalizedCenter.Y);
        double bodyAngle = Math.Sin(age * 2.4) * 4.2 * ease;

        dc.PushTransform(BuildTransform(eyeX, eyeY, effCx * 0.9, (-0.7 * ease) + (effCy * 0.8), bodyAngle, 1.0 + (0.03 * ease), 1.0 + (0.03 * ease)));
        dc.DrawImage(body.Bitmap, fitRect);
        dc.Pop();

        double lookX = ((Math.Cos(age * 2.8) * 1.5) + (effCx * 2.2)) * ease;
        double lookY = ((Math.Sin(age * 2.2) * 1.3) + (effCy * 1.9)) * ease;
        double pupilScale = 1.0 + ((0.05 + (0.06 * Math.Sin(age * 4.2))) * ease);

        dc.PushTransform(BuildTransform(eyeX, eyeY, lookX, lookY, 0, pupilScale, pupilScale));
        dc.DrawImage(pupil.Bitmap, fitRect);
        dc.Pop();

        return true;
    }

    // 12. NOTEPAD: Steady aqua-teal notepad page while the white stylus pen writes smooth strokes across the page.
    private static bool TryDrawNotepadAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double effCx, double effCy)
    {
        if (CyberIconAnatomy.SplitSeam(logo, seamKind: 1) is not { } parts) return false;

        dc.PushTransform(BuildTransform(cx, cy, effCx * 0.8, (-0.7 * ease) + (effCy * 0.8), 0, 1.0 + (0.025 * ease), 1.0 + (0.025 * ease)));
        dc.DrawImage(parts.First, fitRect);

        double penPivotX = fitRect.Left + (fitRect.Width * 0.86);
        double penPivotY = fitRect.Top + (fitRect.Height * 0.14);
        double scribbleX = ((Math.Sin(age * 8.5) * 1.9) - 0.6) * ease;
        double scribbleY = ((Math.Cos(age * 4.25) * 1.4) + 0.5) * ease;
        double penAngle = Math.Sin(age * 8.5) * 5.5 * ease;

        dc.PushTransform(BuildTransform(penPivotX, penPivotY, scribbleX + (effCx * 0.6), scribbleY + (effCy * 0.5), penAngle, 1.0 + (0.03 * ease), 1.0 + (0.03 * ease)));
        dc.DrawImage(parts.Second, fitRect);
        dc.Pop();

        dc.Pop();
        return true;
    }

    // 13. WINDOWS TERMINAL: Subtle tab ripple while inside the screen '>' nudges on command entry and '_' genuinely blinks on/off.
    private static bool TryDrawTerminalAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double effCx, double effCy,
        Color backdrop)
    {
        if (CyberIconAnatomy.GetTerminalAnatomy(logo) is not { } term) return false;

        double scale = 1.0 + (0.03 * ease);
        dc.PushTransform(BuildTransform(cx, cy, effCx * 1.0, (-0.8 * ease) + (effCy * 0.9), 0, scale, scale));

        dc.DrawImage(term.FilledBody, fitRect);

        for (int i = 0; i < term.TopTabs.Count; i++)
        {
            double tabWave = Math.Pow(Math.Max(0, Math.Sin((age * 4.2) - (i * 1.1))), 2.4) * ease;
            dc.PushTransform(new TranslateTransform(0, -1.4 * tabWave));
            dc.DrawImage(term.TopTabs[i].Bitmap, fitRect);
            dc.Pop();
        }

        if (term.PromptHole is { } prompt)
        {
            double kick = Math.Pow(Math.Max(0, Math.Sin(age * 3.4)), 3.5) * ease;
            double px = fitRect.Left + (fitRect.Width * prompt.NormalizedCenter.X);
            double py = fitRect.Top + (fitRect.Height * prompt.NormalizedCenter.Y);
            dc.PushTransform(BuildTransform(px, py, 1.5 * kick, 0, 0, 1.0 + (0.05 * kick), 1.0 + (0.05 * kick)));
            DrawCleanPart(dc, prompt, fitRect, backdrop);
            dc.Pop();
        }

        if (term.CursorHole is { } cursor)
        {
            bool blinkLit = ((int)(age * 3.6) % 2) == 0;
            double cursorOpacity = blinkLit ? 1.0 : (1.0 - (0.86 * ease));
            dc.PushOpacity(cursorOpacity);
            DrawCleanPart(dc, cursor, fitRect, backdrop);
            dc.Pop();
        }

        dc.Pop();
        return true;
    }

    // 14. 7-ZIP: Outer frame gives a clean horizontal compression press while '7' and 'z' squeeze inward inside the frame.
    private static bool TryDrawSevenZipAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double effCx, double effCy)
    {
        if (CyberIconAnatomy.GetConnectedIslands(logo) is not { Count: >= 3 } islands) return false;

        var sorted = islands.OrderByDescending(i => i.PixelCount).ToList();
        var frame = sorted[0];
        var inner = sorted.Skip(1).OrderBy(i => i.NormalizedCenter.X).ToList();

        double clamp = Math.Pow(Math.Max(0, Math.Sin(age * 3.4)), 3.0) * ease;
        double frameSx = 1.0 + (0.025 * ease) - (0.085 * clamp);
        double frameSy = 1.0 + (0.025 * ease) + (0.045 * clamp);

        dc.PushTransform(BuildTransform(cx, cy, effCx * 1.0, (-0.8 * ease) + (effCy * 0.9), 0, 1.0, 1.0));

        dc.PushTransform(BuildTransform(cx, cy, 0, 0, 0, frameSx, frameSy));
        dc.DrawImage(frame.Bitmap, fitRect);
        dc.Pop();

        for (int i = 0; i < inner.Count; i++)
        {
            var part = inner[i];
            double dir = part.NormalizedCenter.X < 0.5 ? 1.0 : -1.0;
            double shiftX = dir * 1.9 * clamp;
            double gx = fitRect.Left + (fitRect.Width * part.NormalizedCenter.X);
            double gy = fitRect.Top + (fitRect.Height * part.NormalizedCenter.Y);
            dc.PushTransform(BuildTransform(gx, gy, shiftX, 0, 0, 1.0 - (0.05 * clamp), 1.0 + (0.04 * clamp)));
            dc.DrawImage(part.Bitmap, fitRect);
            dc.Pop();
        }

        dc.Pop();
        return true;
    }

    // 15. AUTODESK FUSION: Upper-left sweeping isometric wing and lower-right vertical pillar articulate in pure 3D CAD parallax.
    private static bool TryDrawFusionAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double effCx, double effCy)
    {
        if (CyberIconAnatomy.SplitSeam(logo, seamKind: 3) is not { } planes) return false;

        double wave = Math.Sin(age * 3.0) * ease;
        double scale = 1.0 + (0.035 * ease);

        dc.PushTransform(BuildTransform(cx, cy, (-1.1 * wave) + (effCx * 1.2), (-1.1 * wave) - (0.9 * ease) + (effCy * 1.0), 0, scale, scale, skewYDeg: (3.0 * wave) + (effCx * 2.4)));
        dc.DrawImage(planes.First, fitRect);
        dc.Pop();

        dc.PushTransform(BuildTransform(cx, cy, (1.1 * wave) + (effCx * 1.2), (1.1 * wave) - (0.9 * ease) + (effCy * 1.0), 0, scale, scale, skewYDeg: (-3.0 * wave) + (effCx * 2.4)));
        dc.DrawImage(planes.Second, fitRect);
        dc.Pop();

        return true;
    }

    // 16. VS CODE: Left '<' chevron wing and right vertical ribbon loop smoothly fold in 3D torsion.
    private static bool TryDrawVSCodeAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double effCx, double effCy)
    {
        if (CyberIconAnatomy.SplitSeam(logo, seamKind: 4) is not { } ribbon) return false;

        double flex = Math.Sin(age * 3.4) * ease;
        double scale = 1.0 + (0.035 * ease);

        dc.PushTransform(BuildTransform(cx, cy, (-1.1 * flex) + (effCx * 1.1), (-0.8 * ease) + (effCy * 0.9), -2.4 * flex, scale, scale));
        dc.DrawImage(ribbon.First, fitRect);
        dc.Pop();

        dc.PushTransform(BuildTransform(cx, cy, (1.1 * flex) + (effCx * 1.1), (-0.8 * ease) + (effCy * 0.9), 2.4 * flex, scale, scale));
        dc.DrawImage(ribbon.Second, fitRect);
        dc.Pop();

        return true;
    }

    // 17. RIVATUNER (RTSS): Grounded stand base, tilting screen, pulsing pink '60' OSD badge, and gliding bottom bezel slider in true colors.
    private static bool TryDrawRivatunerAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double effCx, double effCy)
    {
        if (CyberIconAnatomy.GetRivatunerAnatomy(logo) is not { } rtss) return false;

        dc.DrawImage(rtss.StandBase, fitRect);

        double hingeY = fitRect.Top + (fitRect.Height * 0.78);
        double tiltSy = 1.0 - (0.035 * Math.Pow(Math.Sin(age * 2.2), 2.0) * ease);
        dc.PushTransform(BuildTransform(cx, hingeY, effCx * 1.1, -1.0 * ease, 0, 1.0 + (0.03 * ease), tiltSy, skewXDeg: effCx * -2.0));
        dc.DrawImage(rtss.ScreenBody, fitRect);

        if (rtss.FpsBadge is { } badge)
        {
            double bx = fitRect.Left + (fitRect.Width * badge.NormalizedCenter.X);
            double by = fitRect.Top + (fitRect.Height * badge.NormalizedCenter.Y);
            double pulse = 1.0 + (0.12 * Math.Pow(Math.Max(0, Math.Sin(age * 4.8)), 2.0) * ease);
            dc.PushTransform(BuildTransform(bx, by, 0, 0, 0, pulse, pulse));
            dc.DrawImage(badge.Bitmap, fitRect);
            dc.Pop();
        }

        if (rtss.BezelSlider is { } slider)
        {
            double slideX = (3.5 + (3.5 * Math.Sin(age * 3.6))) * ease;
            dc.PushTransform(new TranslateTransform(slideX, 0));
            dc.DrawImage(slider.Bitmap, fitRect);
            dc.Pop();
        }

        dc.Pop();
        return true;
    }

    // 18. GITHUB DESKTOP: Stationary purple circular badge while the inner white Octocat silhouette gently sways/nods inside.
    private static bool TryDrawGitHubAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double effCx, double effCy,
        Color backdrop)
    {
        if (CyberIconAnatomy.GetCutoutStencil(logo, isCircularDisc: true) is not { Parts.Count: >= 1 } stencil) return false;

        var cat = stencil.Parts.OrderByDescending(p => p.PixelCount).First();

        double discScale = 1.0 + (0.025 * ease);
        dc.PushTransform(BuildTransform(cx, cy, effCx * 0.9, (-0.6 * ease) + (effCy * 0.8), 0, discScale, discScale));
        dc.DrawImage(stencil.FilledBase, fitRect);

        double clipR = Math.Min(fitRect.Width, fitRect.Height) * 0.485;
        var discClip = new EllipseGeometry(new Point(cx, cy), clipR, clipR);
        discClip.Freeze();
        dc.PushClip(discClip);

        double catPivotX = fitRect.Left + (fitRect.Width * cat.NormalizedCenter.X);
        double catPivotY = fitRect.Top + (fitRect.Height * Math.Min(0.88, cat.NormalizedBounds.Bottom));
        double wagAngle = Math.Sin(age * 3.6) * 6.5 * ease;
        double hopY = -Math.Abs(Math.Sin(age * 3.6)) * 1.4 * ease;

        dc.PushTransform(BuildTransform(catPivotX, catPivotY, 0, hopY, wagAngle, 1.0 + (0.025 * ease), 1.0 + (0.025 * ease)));
        DrawCleanPart(dc, cat, fitRect, backdrop);
        dc.Pop();

        dc.Pop(); // discClip
        dc.Pop(); // discTransform
        return true;
    }

    // 19. WINDOWS SETTINGS: Outer 8-tooth gear ratchets in crisp stepped 45° clicks while the inner bearing ring counter-rotates.
    private static bool TryDrawSettingsGearAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease, double active, double settleProgress,
        double effCx, double effCy)
    {
        if (CyberIconAnatomy.GetToneAnatomy(logo, AnimatedIconKind.WindowsSettings) is not { } gear) return false;

        const double stepPeriod = 0.48;
        double stepIndex = Math.Floor(age / stepPeriod);
        double stepPhase = (age % stepPeriod) / stepPeriod;
        double steppedAngle = (stepIndex + EaseOutBackSubtle(Math.Clamp(stepPhase * 1.45, 0, 1))) * 45.0;
        double outerAngle = SettleAngle(steppedAngle, active, settleProgress);
        double innerAngle = SettleAngle(-age * 72.0, active, settleProgress);
        double scale = 1.0 + (0.03 * ease);

        dc.PushTransform(BuildTransform(cx, cy, effCx * 1.0, (-0.8 * ease) + (effCy * 0.8), outerAngle, scale, scale));
        dc.DrawImage(gear.BaseLayer, fitRect);
        dc.Pop();

        dc.PushTransform(BuildTransform(cx, cy, effCx * 1.3, (-0.8 * ease) + (effCy * 1.0), innerAngle, scale * 1.02, scale * 1.02));
        dc.DrawImage(gear.FrontLayer, fitRect);
        dc.Pop();

        return true;
    }

    // 19b. MSI AFTERBURNER: Dragon's upper head & wings articulate cleanly against its coiled lower tail in pure red tones.
    private static bool TryDrawMsiAfterburnerAnatomy(
        DrawingContext dc, ImageSource logo, Rect fitRect,
        double cx, double cy, double age, double ease,
        double effCx, double effCy)
    {
        if (CardIcons.SplitHorizontal(logo, 0.52) is not { } dragon) return false;

        double roar = Math.Sin(age * 4.2) * ease;
        double scale = 1.0 + ((0.03 + (0.025 * Math.Max(0, roar))) * ease);

        dc.PushTransform(BuildTransform(cx, cy, (effCx * 1.2) + (0.6 * roar), (-1.0 * ease) - (1.2 * Math.Max(0, roar)) + (effCy * 0.9), 3.0 * roar, scale, scale));
        dc.DrawImage(dragon.Top, fitRect);
        dc.Pop();

        dc.PushTransform(BuildTransform(cx, cy, (effCx * 1.0) - (0.6 * roar), (-0.7 * ease) + (0.9 * Math.Max(0, roar)) + (effCy * 0.9), -2.4 * roar, scale, scale));
        dc.DrawImage(dragon.Bottom, fitRect);
        dc.Pop();

        return true;
    }

    // 20. AUTOMATIC TOPOLOGICAL ANATOMY FOR ANY OTHER ICON (pure original colors, zero color filters):
    private static bool TryDrawAutomaticAnatomy(
        DrawingContext dc, ImageSource logo, AnimatedIconKind kind, Rect fitRect,
        double cx, double cy, double age, double ease, double active, double settleProgress,
        double effCx, double effCy, Color backdrop)
    {
        // Case A: Multi-island icon (2..8 separate connected islands).
        if (CyberIconAnatomy.GetConnectedIslands(logo) is { Count: >= 2 } islands)
        {
            var profile = AnimatedIconCatalog.MotionFor(kind);
            double baseSpin = profile == IconMotionProfile.RotorSpin && kind != AnimatedIconKind.MSIAfterburner
                ? SettleAngle(IntegrateEasedSpin(age, 42.0, 0.30), active, settleProgress)
                : 0;

            dc.PushTransform(BuildTransform(cx, cy, effCx * 1.0, (-0.8 * ease) + (effCy * 0.9), baseSpin, 1.0 + (0.03 * ease), 1.0 + (0.03 * ease)));
            for (int i = 0; i < islands.Count; i++)
            {
                var isl = islands[i];
                double wave = Math.Sin((age * 4.0) - (i * 0.95)) * ease;
                double dx = isl.RadialDir.X * 1.35 * wave;
                double dy = isl.RadialDir.Y * 1.35 * wave;
                double ix = fitRect.Left + (fitRect.Width * isl.NormalizedCenter.X);
                double iy = fitRect.Top + (fitRect.Height * isl.NormalizedCenter.Y);
                double iScale = 1.0 + (0.035 * Math.Max(0, wave));

                dc.PushTransform(BuildTransform(ix, iy, dx, dy, wave * 2.4, iScale, iScale));
                dc.DrawImage(isl.Bitmap, fitRect);
                dc.Pop();
            }
            dc.Pop();
            return true;
        }

        // Case B: Cutout stencil icon (solid plate/disc with interior symbol).
        if (CyberIconAnatomy.GetCutoutStencil(logo) is { Parts.Count: >= 1 } stencil)
        {
            dc.PushTransform(BuildTransform(cx, cy, effCx * 0.8, (-0.6 * ease) + (effCy * 0.7), 0, 1.0 + (0.02 * ease), 1.0 + (0.02 * ease)));
            dc.DrawImage(stencil.FilledBase, fitRect);
            dc.PushOpacityMask(MaskBrush(stencil.FilledBase, fitRect));

            for (int i = 0; i < stencil.Parts.Count; i++)
            {
                var part = stencil.Parts[i];
                double px = fitRect.Left + (fitRect.Width * part.NormalizedCenter.X);
                double py = fitRect.Top + (fitRect.Height * part.NormalizedCenter.Y);
                double pulse = Math.Sin((age * 3.8) - (i * 0.85)) * ease;
                double pScale = 1.0 + (0.075 * Math.Abs(pulse));

                dc.PushTransform(BuildTransform(px, py, effCx * 1.0, -1.6 * Math.Abs(pulse), pulse * 4.5, pScale, pScale));
                DrawCleanPart(dc, part, fitRect, backdrop);
                dc.Pop();
            }

            dc.Pop();
            dc.Pop();
            return true;
        }

        // Case C: Multi-tone shaded icon — ground the base and articulate the foreground emblem in subtle 3D parallax.
        if (CyberIconAnatomy.GetToneAnatomy(logo, kind) is { } tone)
        {
            dc.PushTransform(BuildTransform(cx, cy, effCx * 0.7, effCy * 0.7, 0, 1.0 + (0.02 * ease), 1.0 + (0.02 * ease)));
            dc.DrawImage(tone.BaseLayer, fitRect);
            dc.PushOpacityMask(MaskBrush(tone.BaseLayer, fitRect));

            double fx = fitRect.Left + (fitRect.Width * tone.FrontCenter.X);
            double fy = fitRect.Top + (fitRect.Height * tone.FrontCenter.Y);
            double floatY = ((-2.0 + (1.2 * Math.Sin(age * 3.2))) * ease) + (effCy * 1.1);
            double tilt = (Math.Sin(age * 2.4) * 3.2 * ease) + (effCx * 2.5);

            dc.PushTransform(BuildTransform(fx, fy, effCx * 1.6, floatY, tilt, 1.0 + (0.045 * ease), 1.0 + (0.045 * ease), skewXDeg: effCx * -2.0));
            dc.DrawImage(tone.FrontLayer, fitRect);
            dc.Pop();

            dc.Pop();
            dc.Pop();
            return true;
        }

        return false;
    }

    private static void DrawCleanPart(DrawingContext dc, CyberIconAnatomy.IconPart part, Rect fitRect, Color backdrop)
    {
        if (part.IsCutoutHole)
        {
            DrawSilhouette(dc, part.Bitmap, fitRect, CachedBrush(backdrop));
        }
        else
        {
            dc.DrawImage(part.Bitmap, fitRect);
        }
    }

    private static void DrawFallbackMotion(
        DrawingContext dc, ImageSource logo, AnimatedIconKind kind, Rect fitRect,
        double cx, double cy, double age, double ease, double active, double settleProgress,
        double effCx, double effCy)
    {
        var profile = AnimatedIconCatalog.MotionFor(kind);

        if (CardIcons.SplitRadial(logo, 0.56) is { } relief)
        {
            double outerAngle = profile == IconMotionProfile.RotorSpin && kind != AnimatedIconKind.MSIAfterburner
                ? SettleAngle(IntegrateEasedSpin(age, 42.0, 0.30), active, settleProgress)
                : (Math.Sin(age * 2.6) * 2.0 * ease);
            double innerBounce = Math.Sin(age * 3.8) * 1.2 * ease;

            dc.PushTransform(BuildTransform(cx, cy, effCx * 0.9, (-0.7 * ease) + (effCy * 0.8), outerAngle, 1.0 + (0.025 * ease), 1.0 + (0.025 * ease)));
            dc.DrawImage(relief.Outer, fitRect);
            dc.Pop();

            dc.PushTransform(BuildTransform(cx, cy, effCx * 1.6, (-1.4 * ease) + innerBounce + (effCy * 1.3), outerAngle * 0.5, 1.0 + (0.05 * ease), 1.0 + (0.05 * ease), skewXDeg: effCx * -2.0));
            dc.DrawImage(relief.Inner, fitRect);
            dc.Pop();
            return;
        }

        dc.PushTransform(BuildTransform(cx, cy, effCx * 1.4, (-1.2 * ease) + (effCy * 1.1), 0, 1.0 + (0.035 * ease), 1.0 + (0.035 * ease), skewXDeg: effCx * -2.0));
        dc.DrawImage(logo, fitRect);
        dc.Pop();
    }

    private static void DrawOpenCodeInteriorTelemetry(
        DrawingContext dc, Rect fitRect, double cx, double cy, double age, double ease)
    {
        double boxW = fitRect.Width * 0.24;
        double boxH = fitRect.Height * 0.24;
        double left = cx - (boxW * 0.5);
        double top = cy - (boxH * 0.5);

        var lineBrush = CachedBrush(Color.FromArgb((byte)(200 * ease), 0x5C, 0xEE, 0xFF));
        var dimBrush = CachedBrush(Color.FromArgb((byte)(155 * ease), 0xCB, 0xD5, 0xE1));
        for (int r = 0; r < 3; r++)
        {
            double frac = 0.42 + (0.50 * (0.5 + (0.5 * Math.Sin((age * 5.8) + (r * 1.3)))));
            dc.DrawRectangle(r == 0 ? lineBrush : dimBrush, null, new Rect(left, top + (r * (boxH * 0.32)), boxW * frac, 1.4));
        }

        if (((int)(age * 3.8) % 2) == 0)
        {
            dc.DrawRectangle(
                CachedBrush(Color.FromArgb((byte)(230 * ease), 0xF8, 0xFA, 0xFC)),
                null,
                new Rect(left, top + (boxH * 0.72), 2.8, 3.6));
        }
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
