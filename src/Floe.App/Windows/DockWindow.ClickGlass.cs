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
        foreach (var previous in grid.Children.OfType<Image>().Where(i => i.Effect is ClickGlassEffect).ToArray())
            grid.Children.Remove(previous);

        var effect = new ClickGlassEffect(IconBox)
        {
            Field = IconField.For(icon.Source, IconBox, solid: true),
            Tone = (double)AuraSettings.ClickTone,
            Palette = (double)(GlassSettings.Mordor ? ClickPalette.Icon : AuraSettings.ClickPalette),
        };
        if ((GlassSettings.Mordor || AuraSettings.ClickPalette == ClickPalette.Icon) && AuraSettings.GetColors(item) is { Length: > 0 } colors)
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
        animation.Completed += (_, _) => grid.Children.Remove(overlay);
        overlay.Unloaded += (_, _) =>
        {
            effect.BeginAnimation(ClickGlassEffect.ProgressProperty, null);
            overlay.Effect = null;
        };
        grid.Children.Add(overlay);
        effect.BeginAnimation(ClickGlassEffect.ProgressProperty, animation);
    }
}
