using System;
using System.Windows;
using System.Windows.Threading;
using MacDock.App.Effects.Visualizer;
using MacDock.App.Services;

namespace MacDock.App.Windows;

public partial class DockWindow
{
    private readonly VisualizerGlow _visualizer = new();
    private readonly HologramVisualizer _hologram = new();
    private DispatcherTimer? _visualizerPoll;
    private bool _visualizerPolling;
    private bool _visualizerRunning;

    private void InitializeVisualizer()
    {
        if (_glassPreview)
        {
            return;
        }

        // Above the backdrop so the light shows through the glass tint and spills below the capsule.
        ShapeLayer.Children.Insert(1, _visualizer);
        ShapeLayer.Children.Insert(2, _hologram);
        UpdateVisualizerCapsule();

        _visualizerPoll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1.5) };
        _visualizerPoll.Tick += async (_, _) =>
        {
            if (_visualizerPolling)
            {
                return;
            }

            _visualizerPolling = true;
            try
            {
                var playing = await VisualizerGlow.ShouldRunAsync();
                _visualizerRunning = GlassSettings.Visualizer && IsVisible && playing;
                ApplyVisualizerState();
            }
            finally
            {
                _visualizerPolling = false;
            }
        };
        _visualizerPoll.Start();
        GlassSettings.Changed += OnVisualizerSettingChanged;
        Closed += (_, _) =>
        {
            _visualizerPoll?.Stop();
            _visualizer.IsActive = false;
            _hologram.IsActive = false;
            GlassSettings.Changed -= OnVisualizerSettingChanged;
        };
    }

    private void OnVisualizerSettingChanged()
    {
        Dispatcher.BeginInvoke(() =>
        {
            _visualizerRunning &= GlassSettings.Visualizer;
            ApplyVisualizerState();
        });
    }

    private void ApplyVisualizerState()
    {
        var hologram = GlassSettings.VisualizerLook == VisualizerStyle.Hologram;
        _visualizer.IsActive = _visualizerRunning && !hologram;
        _hologram.IsActive = _visualizerRunning && hologram;
    }

    private void UpdateVisualizerCapsule()
    {
        var capsule = new Rect(_capsuleLeft, RestTop, Math.Max(1, _capsuleWidth), RestBarHeight);
        foreach (var element in new FrameworkElement[] { _visualizer, _hologram })
        {
            element.Width = _windowWidth;
            element.Height = WindowHeight;
        }

        _visualizer.Capsule = capsule;
        _hologram.Capsule = capsule;
    }
}
