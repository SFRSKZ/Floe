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
        if (!Performance.RichClick || !AuraSettings.ClickEffect || NoFx.Contains("click", StringComparison.OrdinalIgnoreCase)
            || !ClickGlassEffect.IsAvailable
            || _slots.FirstOrDefault(s => ReferenceEquals(s.Item, item)) is not { } slot
            || slot.Button.Content is not Grid grid || grid.Children[0] is not Image icon
            || icon.Source is null) return;

        // a repeated click replaces the previous drop; the parent carries scale and bounce
        foreach (var previous in grid.Children.OfType<Image>().Where(i => i.Effect is ClickGlassEffect && !ReferenceEquals(i, _glassLoop)).ToArray())
            grid.Children.Remove(previous);

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
            grid.Children.Remove(overlay);
            if (loop is not null) loop.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
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
        _glassLoopEffect?.BeginAnimation(ClickGlassEffect.LoopTimeProperty, null);
        if (_glassLoop?.Parent is Grid grid) grid.Children.Remove(_glassLoop);
        if (_glassLoop is not null) _glassLoop.Effect = null;
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
}
