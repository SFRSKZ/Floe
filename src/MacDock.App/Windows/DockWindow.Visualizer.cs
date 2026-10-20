using System;
using System.Windows;
using System.Windows.Threading;
using MacDock.App.Effects.Visualizer;
using MacDock.App.Services;

namespace MacDock.App.Windows;

public partial class DockWindow
{
    private readonly VisualizerGlow _visualizer = new();
    private DispatcherTimer? _visualizerPoll;
    private bool _visualizerPolling;

    private void InitializeVisualizer()
    {
        if (_glassPreview)
        {
            return;
        }

        // Above the backdrop so the light shows through the glass tint and spills below the capsule.
        ShapeLayer.Children.Insert(1, _visualizer);
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
                var run = GlassSettings.Visualizer && IsVisible && await VisualizerGlow.ShouldRunAsync();
                _visualizer.IsActive = run;
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
            GlassSettings.Changed -= OnVisualizerSettingChanged;
        };
    }

    private void OnVisualizerSettingChanged()
    {
        if (!GlassSettings.Visualizer)
        {
            Dispatcher.BeginInvoke(() => _visualizer.IsActive = false);
        }
    }

    private void UpdateVisualizerCapsule()
    {
        _visualizer.Width = _windowWidth;
        _visualizer.Height = WindowHeight;
        _visualizer.Capsule = new Rect(_capsuleLeft, RestTop, Math.Max(1, _capsuleWidth), RestBarHeight);
    }
}
