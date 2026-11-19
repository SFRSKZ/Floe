using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Floe.App.Effects.ActiveIcon;
using Floe.App.Services;
using Floe.App.ViewModels;

namespace Floe.App.Windows;

public partial class DockWindow
{
    private void PlayClickGlass(DockItemViewModel item)
    {
        // the cubes take the click's place in either theme
        if (AuraSettings.ClickCubes && AuraSettings.ClickEffect && !NoFx.Contains("click", StringComparison.OrdinalIgnoreCase))
        {
            if (_slots.FirstOrDefault(s => ReferenceEquals(s.Item, item)) is { } hit) PlayClickCubes(hit);
            return;
        }

        if (GlassSettings.Cyber)
        {
            if (AuraSettings.ClickEffect && !NoFx.Contains("click", StringComparison.OrdinalIgnoreCase)) PlayCyberClick(item);
            return;
        }

        if (!Performance.RichClick || !AuraSettings.ClickEffect || NoFx.Contains("click", StringComparison.OrdinalIgnoreCase)
            || !ClickGlassEffect.IsAvailable
            || _slots.FirstOrDefault(s => ReferenceEquals(s.Item, item)) is not { } slot
            || slot.Button.Content is not Grid grid || grid.Children[0] is not Image icon
            || icon.Source is null) return;

        // a repeated click replaces the previous drop; the parent carries scale and bounce
        foreach (var previous in grid.Children.OfType<Image>().Where(i => i.Effect is ClickGlassEffect && !ReferenceEquals(i, _glassLoop) && !IsRestGlass(i)).ToArray())
            grid.Children.Remove(previous);

        // the resting glass steps aside while the click plays; the click ends in the same glass and hands back to it
        SetRestGlassVisible(grid, false);

        // the active icon's looping drop steps aside while the click plays, and picks up after it
        var loop = grid.Children.Contains(_glassLoop!) ? _glassLoop : null;
        if (loop is not null) loop.Opacity = 0;

        var effect = new ClickGlassEffect(IconBox, paint: AuraSettings.ClickPaint && ClickGlassEffect.IsPaintAvailable)
        {
            Screen = AuraSettings.ClickScreen ? 1 : 0, // the LED-screen glass, when picked in the settings
            Field = IconField.For(icon.Source, IconBox, solid: true),
            Tone = (double)AuraSettings.ClickTone,
            Palette = (double)AuraSettings.ClickPalette,
        };
        if (AuraSettings.ClickPalette == ClickPalette.Icon && AuraSettings.GetColors(item) is { Length: > 0 } colors)
        {
            effect.ColorA = colors[0];
            effect.ColorB = colors[1 % colors.Length];
        }

        var overlay = new Image
        {
            Source = icon.Source, Width = icon.Width, Height = icon.Height,
            Stretch = icon.Stretch, Margin = icon.Margin,
            HorizontalAlignment = icon.HorizontalAlignment, VerticalAlignment = icon.VerticalAlignment,
            RenderTransform = icon.RenderTransform, RenderTransformOrigin = icon.RenderTransformOrigin,
            IsHitTestVisible = false, Effect = effect,
        };
        RenderOptions.SetBitmapScalingMode(overlay, BitmapScalingMode.HighQuality);
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(1050 / AuraSettings.ClickSpeed));
        animation.Completed += (_, _) =>
        {
            // the finished drop and the glass that stays cross-fade instead of cutting over
            FadeOutGlass(overlay, GlassReturnMs);
            if (!ReferenceEquals(_glassLoop?.Parent, grid)) SetRestGlassVisible(grid, true);
            if (loop is not null) loop.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(GlassReturnMs)));
        };
        overlay.Unloaded += (_, _) =>
        {
            effect.BeginAnimation(ClickGlassEffect.ProgressProperty, null);
            overlay.Effect = null;
        };
        grid.Children.Add(overlay);
        effect.BeginAnimation(ClickGlassEffect.ProgressProperty, animation);
    }

    // The active icon keeps the click's glass going: a wave runs in from the edge to the middle, where the
    // drop opens again and swells out to the edge, over and over (ClickGlassEffect.LoopAt).

    private Image? _glassLoop;
    private ClickGlassEffect? _glassLoopEffect;

    private void StartGlassLoop(Image icon, DockItemViewModel item)
    {
        StopGlassLoop();
        if (!ClickGlassEffect.IsAvailable || icon.Source is null || icon.Parent is not Grid grid) return;

        var effect = new ClickGlassEffect(IconBox)
        {
            Sharp = 1, // the glass stays on the icon, so it may light it but never bend it
            Field = IconField.For(icon.Source, IconBox, solid: true),
            Tone = (double)AuraSettings.ClickTone,
            Palette = (double)AuraSettings.ClickPalette,
        };
        if (AuraSettings.ClickPalette == ClickPalette.Icon && AuraSettings.GetColors(item) is { Length: > 0 } colors)
        {
            effect.ColorA = colors[0];
            effect.ColorB = colors[1 % colors.Length];
        }

        var overlay = new Image
        {
            Source = icon.Source, Width = icon.Width, Height = icon.Height,
            Stretch = icon.Stretch, Margin = icon.Margin,
            HorizontalAlignment = icon.HorizontalAlignment, VerticalAlignment = icon.VerticalAlignment,
            RenderTransform = icon.RenderTransform, RenderTransformOrigin = icon.RenderTransformOrigin,
            IsHitTestVisible = false, Effect = effect, Opacity = 0,
        };
        RenderOptions.SetBitmapScalingMode(overlay, BitmapScalingMode.HighQuality);
        SetRestGlassVisible(grid, false);
        grid.Children.Add(overlay);
        _glassLoop = overlay;
        _glassLoopEffect = effect;
        RunGlassLoop();
        overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)));
    }

    private void RunGlassLoop() => _glassLoopEffect?.BeginAnimation(ClickGlassEffect.LoopTimeProperty,
        new DoubleAnimation(0, ClickGlassEffect.LoopSeconds, TimeSpan.FromSeconds(ClickGlassEffect.LoopSeconds)) { RepeatBehavior = RepeatBehavior.Forever });

    private void StopGlassLoop()
    {
        if (_glassLoop?.Parent is Grid grid)
        {
            // the loop keeps running while it fades, so it does not freeze mid-wave on its way out
            var leaving = _glassLoop;
            var leavingEffect = _glassLoopEffect;
            FadeOutGlass(leaving, GlassReturnMs, () =>
            {
                leavingEffect?.BeginAnimation(ClickGlassEffect.LoopTimeProperty, null);
                leaving.Effect = null;
            });
            SetRestGlassVisible(grid, true);
        }
        else
        {
            _glassLoopEffect?.BeginAnimation(ClickGlassEffect.LoopTimeProperty, null);
            if (_glassLoop is not null) _glassLoop.Effect = null;
        }

        _glassLoop = null;
        _glassLoopEffect = null;
    }

    // a hidden dock pauses the loop so it costs no frames
    private void SetGlassLoopVisible(bool visible)
    {
        if (_glassLoop is null) return;
        _glassLoop.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
        if (visible) RunGlassLoop();
        else _glassLoopEffect?.BeginAnimation(ClickGlassEffect.LoopTimeProperty, null);
    }

    // At rest every icon sits in the glass a click leaves behind: the drop held where it has settled into the
    // icon's shape, lighting its edge without bending the icon (the active icon's loop takes its place).

    private const string RestGlassTag = "rest-glass";

    private static bool IsRestGlass(Image image) => Equals(image.Tag, RestGlassTag);

    private void ApplyRestGlass(IconSlot slot, bool rebuild = false)
    {
        if (slot.Button.Content is not Grid grid || grid.Children[0] is not Image icon) return;
        var current = grid.Children.OfType<Image>().FirstOrDefault(IsRestGlass);
        if (current is not null && rebuild)
        {
            grid.Children.Remove(current);
            current.Effect = null;
            current = null;
        }

        var wanted = AuraSettings.RestGlass && !GlassSettings.Cyber && ClickGlassEffect.IsAvailable && icon.Source is not null
            && !NoFx.Contains("rest", StringComparison.OrdinalIgnoreCase);
        if (!wanted)
        {
            if (current is null) return;
            grid.Children.Remove(current);
            current.Effect = null;
            return;
        }

        if (current is not null) return;
        var effect = new ClickGlassEffect(IconBox)
        {
            Progress = .30, // where the click's drop has settled into the icon's shape
            Sharp = 1,
            Field = IconField.For(icon.Source!, IconBox, solid: true),
            Tone = (double)AuraSettings.ClickTone,
            Palette = (double)AuraSettings.ClickPalette,
        };
        if (AuraSettings.ClickPalette == ClickPalette.Icon && AuraSettings.GetColors(slot.Item) is { Length: > 0 } colors)
        {
            effect.ColorA = colors[0];
            effect.ColorB = colors[1 % colors.Length];
        }

        var overlay = new Image
        {
            Source = icon.Source, Width = icon.Width, Height = icon.Height,
            Stretch = icon.Stretch, Margin = icon.Margin,
            HorizontalAlignment = icon.HorizontalAlignment, VerticalAlignment = icon.VerticalAlignment,
            RenderTransform = icon.RenderTransform, RenderTransformOrigin = icon.RenderTransformOrigin,
            IsHitTestVisible = false, Effect = effect, Tag = RestGlassTag,
            Visibility = ReferenceEquals(_glassLoop?.Parent, grid) ? Visibility.Hidden : Visibility.Visible,
        };
        RenderOptions.SetBitmapScalingMode(overlay, BitmapScalingMode.HighQuality);
        grid.Children.Insert(1, overlay);
    }

    // how long the glass layers take to hand over to each other: a cut between two lit glasses reads as a flicker
    private const double GlassHandOverMs = 140, GlassReturnMs = 420;

    private static void SetRestGlassVisible(Grid grid, bool visible)
    {
        foreach (var rest in grid.Children.OfType<Image>().Where(IsRestGlass))
        {
            if (visible)
            {
                var from = rest.Visibility == Visibility.Visible ? rest.Opacity : 0;
                rest.Visibility = Visibility.Visible;
                rest.BeginAnimation(OpacityProperty, new DoubleAnimation(from, 1, TimeSpan.FromMilliseconds(GlassReturnMs))
                {
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                });
            }
            else
            {
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(GlassHandOverMs));
                fade.Completed += (_, _) =>
                {
                    // hidden once faded (it costs no frames), unless it was asked back meanwhile
                    if (rest.Opacity <= 0.001) rest.Visibility = Visibility.Hidden;
                };
                rest.BeginAnimation(OpacityProperty, fade);
            }
        }
    }

    /// <summary>Fades a glass overlay out and takes it off its grid once gone.</summary>
    private static void FadeOutGlass(Image overlay, double ms, Action? removed = null)
    {
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        fade.Completed += (_, _) =>
        {
            (overlay.Parent as Grid)?.Children.Remove(overlay);
            removed?.Invoke();
        };
        overlay.BeginAnimation(OpacityProperty, fade);
    }
}
