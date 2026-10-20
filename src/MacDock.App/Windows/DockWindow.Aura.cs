using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using MacDock.App.Services;
using MacDock.App.ViewModels;
using MacDock.Core.Interop;
using MacDock.Core.Models;
using MacDock.Core.Services;

namespace MacDock.App.Windows;

public partial class DockWindow
{

    private void UpdateAura()
    {
        _activeIndex = -1;
        if (!string.IsNullOrEmpty(_activeProcess))
        {
            for (var i = 0; i < _slots.Count; i++)
            {
                var candidate = _slots[i].Item;
                if (candidate.IsRunning
                    && AppMatcher.Matches(candidate.Name, candidate.ProcessName, candidate.App.Target, _activeProcess, _activeTitle, _activePath))
                {
                    _activeIndex = i;
                    break;
                }
            }
        }

        _auraAttention = false;
        if (_activeIndex < 0 && _attentionItem is not null)
        {
            for (var i = 0; i < _slots.Count; i++)
            {
                if (ReferenceEquals(_slots[i].Item, _attentionItem))
                {
                    _activeIndex = i;
                    _auraAttention = true;
                    break;
                }
            }
        }

        if (_activeIndex < 0 || _slots[_activeIndex].Item.Icon is not { } icon)
        {
            _auraGrid.Visibility = Visibility.Collapsed;
            SetSpectralVisible(false);
            for (var k = 0; k < _auraEffects.Length; k++)
            {
                _auraEffects[k].BeginAnimation(DropShadowEffect.DirectionProperty, null);
            }

            return;
        }

        var item = _slots[_activeIndex].Item;

        if (AuraSettings.Effect == AuraEffect.Burst)
        {
            _auraGrid.Visibility = Visibility.Collapsed;
            for (var k = 0; k < _auraEffects.Length; k++)
            {
                _auraEffects[k].BeginAnimation(DropShadowEffect.DirectionProperty, null);
            }

            UpdateSpectral(item);
            PositionSpectral(_slots[_activeIndex]);
            SetSpectralVisible(true);
            return;
        }

        SetSpectralVisible(false);

        var colors = AuraSettings.GetColors(item);
        for (var k = 0; k < _auraImages.Length; k++)
        {
            _auraImages[k].Source = icon;
            _auraEffects[k].Color = colors[k % colors.Length];
            _auraEffects[k].BeginAnimation(DropShadowEffect.DirectionProperty, _auraDirAnims[k]);
        }

        _auraGrid.Visibility = Visibility.Visible;
        PositionAura(_slots[_activeIndex]);
    }

    private void PositionAura(IconSlot slot)
    {
        Canvas.SetLeft(_auraGrid, Canvas.GetLeft(slot.Button));
        Canvas.SetTop(_auraGrid, Canvas.GetTop(slot.Button));
        if (_auraGrid.RenderTransform is ScaleTransform aura)
        {
            aura.ScaleX = slot.Scale.ScaleX;
            aura.ScaleY = slot.Scale.ScaleY;
        }
    }

    private void SetSpectralVisible(bool visible)
    {
        if (visible == (_spectralLayer.Visibility == Visibility.Visible))
        {
            return;
        }

        _spectralLayer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        if (visible)
        {
            _spectralLayer.BeginAnimation(OpacityProperty, _auraAttention ? _spectralBreathFast : _spectralBreath);
            _spectralRim.Opacity = _auraAttention ? 1.0 : 0.82;
            _spectralRim.BeginAnimation(Shape.StrokeDashOffsetProperty, _spectralDash);
            _spectralWaveSpin.BeginAnimation(RotateTransform.AngleProperty, _spectralWaveAnim);
            for (var i = 0; i < _spectralWisps.Count; i++)
            {
                _spectralWisps[i].Spin.BeginAnimation(RotateTransform.AngleProperty, _spectralWispSpins[i]);
            }
        }
        else
        {
            _spectralLayer.BeginAnimation(OpacityProperty, null);
            _spectralRim.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            _spectralWaveSpin.BeginAnimation(RotateTransform.AngleProperty, null);
            for (var i = 0; i < _spectralWisps.Count; i++)
            {
                _spectralWisps[i].Spin.BeginAnimation(RotateTransform.AngleProperty, null);
            }
        }
    }

    private static Color Spectral(Color color)
    {
        // ghostly: desaturate toward a pale blue-white (unseen-world tint)
        static byte Mix(byte value, byte target, double amount) => (byte)Math.Round(value + ((target - value) * amount));
        return Color.FromRgb(Mix(color.R, 0xBA, 0.58), Mix(color.G, 0xE8, 0.58), Mix(color.B, 0xF4, 0.58));
    }

    private void UpdateSpectral(DockItemViewModel item)
    {
        var colors = AuraSettings.GetColors(item);
        if (colors.Length == 0)
        {
            return;
        }

        var first = Spectral(colors[0]);
        var second = Spectral(colors[1 % colors.Length]);
        var third = Spectral(colors[2 % colors.Length]);
        var k = AuraSettings.SpectralIntensity;

        // soft radial distortion around the icon edges
        _spectralHaloBrush.GradientStops[0].Color = Color.FromArgb(0x00, first.R, first.G, first.B);
        _spectralHaloBrush.GradientStops[1].Color = Color.FromArgb(Alpha(0x3E, k), first.R, first.G, first.B);
        _spectralHaloBrush.GradientStops[2].Color = Color.FromArgb(0x00, second.R, second.G, second.B);

        // irregular shimmering outline (slight refraction)
        _spectralRimBrush.GradientStops.Clear();
        _spectralRimBrush.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(0xB4, k), first.R, first.G, first.B), 0.0));
        _spectralRimBrush.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(0x44, k), second.R, second.G, second.B), 0.45));
        _spectralRimBrush.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(0xB4, k), third.R, third.G, third.B), 1.0));

        // flowing wave-like energy around the perimeter
        _spectralWaveBrush.GradientStops.Clear();
        _spectralWaveBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, second.R, second.G, second.B), 0.0));
        _spectralWaveBrush.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(0x70, k), third.R, third.G, third.B), 0.5));
        _spectralWaveBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, first.R, first.G, first.B), 1.0));

        // faint smoky wisps
        for (var i = 0; i < _spectralWisps.Count; i++)
        {
            var color = Spectral(colors[i % colors.Length]);
            _spectralWisps[i].Wisp.Fill = new SolidColorBrush(Color.FromArgb(Alpha(0x50, k), color.R, color.G, color.B));
        }
    }

    private static byte Alpha(int alpha, double intensity) => (byte)Math.Clamp(alpha * intensity, 0, 255);

    private void PositionSpectral(IconSlot slot)
    {
        var centerX = slot.Center;
        var centerY = IconBottom - (IconBox / 2);

        Canvas.SetLeft(_spectralHalo, centerX - (_spectralHalo.Width / 2));
        Canvas.SetTop(_spectralHalo, centerY - (_spectralHalo.Height / 2));
        Canvas.SetLeft(_spectralRim, centerX - (_spectralRim.Width / 2));
        Canvas.SetTop(_spectralRim, centerY - (_spectralRim.Height / 2));
        Canvas.SetLeft(_spectralWave, centerX - (_spectralWave.Width / 2));
        Canvas.SetTop(_spectralWave, centerY - (_spectralWave.Height / 2));

        const double orbit = 30;
        foreach (var (wisp, _) in _spectralWisps)
        {
            Canvas.SetLeft(wisp, centerX - (wisp.Width / 2));
            Canvas.SetTop(wisp, centerY - orbit - (wisp.Height / 2));
        }
    }

    private void OnAuraSettingsChanged() => UpdateAura();

}
