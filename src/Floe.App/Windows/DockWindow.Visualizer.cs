using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Floe.App.Effects.Visualizer;
using Floe.App.Services;

namespace Floe.App.Windows;

public partial class DockWindow
{
    private VisualizerWindow? _visualizerWindow;
    private DispatcherTimer? _visualizerPoll;
    private bool _visualizerPolling;
    private bool _visualizerRunning;
    private bool _iconsDimmed;
    private const double IdleIconOpacity = 0.6;

    private void InitializeVisualizer()
    {
        if (_glassPreview || NoFx.Contains("viz"))
        {
            return;
        }

        SetIconsDimmed(true);

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
                _visualizerRunning = GlassSettings.Visualizer && IsVisible && _shown && !_fullscreenHidden && playing;
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
            GlassSettings.Changed -= OnVisualizerSettingChanged;
            _visualizerWindow?.Close();
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
        if (!_visualizerRunning && _visualizerWindow is null)
        {
            return;
        }

        if (_visualizerWindow is null)
        {
            _visualizerWindow = new VisualizerWindow();
            _visualizerWindow.Show();
        }

        // the strip covers the space the dock reserves at the bottom of the monitor
        var scale = DockScale;
        var area = _monitorInfo.Monitor;
        var height = RestBarHeight + BottomMargin;
        _visualizerWindow.Place(new Rect(area.Left / scale, (area.Bottom / scale) - height, (area.Right - area.Left) / scale, height), _hwnd);
        _visualizerWindow.SetRunning(_visualizerRunning);
        SinkBelowVisualizer();
    }

    /// <summary>Icons rest a little faded while the pointer is away, so the glass reads through.</summary>
    private void SetIconsDimmed(bool dimmed)
    {
        if (_glassPreview || _iconsDimmed == dimmed)
        {
            return;
        }

        _iconsDimmed = dimmed;
        var animation = new DoubleAnimation(dimmed ? IdleIconOpacity : 1, TimeSpan.FromMilliseconds(dimmed ? 450 : 160))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        IconLayer.BeginAnimation(OpacityProperty, animation);
    }
}
