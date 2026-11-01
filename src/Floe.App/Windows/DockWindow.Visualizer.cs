using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Floe.Core.Interop;
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
                // the OLED panel stays on without music: its robot comes out when nothing plays;
                // a cyber theme has its own strips, so a Tron look left behind must not keep them lit
                bool panel = !GlassSettings.Cyber && GlassSettings.VisualizerLook is (VisualizerStyle.Oled or VisualizerStyle.Led);
                _visualizerRunning = GlassSettings.Visualizer && IsVisible && _shown && !_fullscreenHidden && (playing || panel);
                ApplyVisualizerState();
                // the catalog's looks read the cover and where the player sits in the dock
                bool novel = Effects.Gpu.GpuSceneCatalog.Has(GlassSettings.VisualizerLook) && _visualizerRunning;
                if ((panel || novel) && _visualizerWindow is not null)
                {
                    var media = await MediaService.GetAsync();
                    _visualizerWindow.SetMusic(playing, media, PlayerIconX(media?.Source));
                }
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
        var dockRect = new Rect(new Point(ToScreenX(_capsuleLeft), ToScreenY(RestTop)), new Point(ToScreenX(_capsuleLeft + _capsuleWidth), ToScreenY(RestTop + RestBarHeight)));
        // the feedback looks fill their whole strip, so they stand above the dock instead of hiding behind it;
        // the strip takes the extra height only when the GPU layer actually carries the look. It is placed at the
        // height it already has: a passing size would resize the layer, and a resize wipes a feedback field
        void Place(bool tall)
        {
            var height = RestBarHeight + BottomMargin + (tall ? RestBarHeight * .12 : 0);
            _visualizerWindow.Place(new Rect(area.Left / scale, (area.Bottom / scale) - height, (area.Right - area.Left) / scale, height), scale, _hwnd, dockRect);
        }

        bool tall = _visualizerWindow.CyberGpuPlaying;
        Place(tall);
        _visualizerWindow.SetRunning(_visualizerRunning);
        if (_visualizerWindow.CyberGpuPlaying != tall) Place(!tall);

        SinkBelowVisualizer();
    }

    // where the player's icon sits along the visualizer strip (0..1); the middle when it is not in the dock
    private double PlayerIconX(string? source)
    {
        if (PlayerSlot(source) is not { } slot) return 0.5;
        var area = _monitorInfo.Monitor;
        var scale = DockScale;
        var left = area.Left / scale;
        var width = Math.Max(1, (area.Right - area.Left) / scale);
        return Math.Clamp((ToScreenX(_restCapsuleLeft + slot.CenterLocal) - left) / width, 0, 1);
    }

    // the player's name as a process would carry it: Spotify.exe, or SpotifyAB.SpotifyMusic_...!Spotify
    private static string PlayerKey(string source) => source.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? source[..^4] : source;

    private IconSlot? PlayerSlot(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        var key = PlayerKey(source);
        foreach (var slot in _slots)
        {
            var name = slot.Item.ProcessName;
            if (!string.IsNullOrEmpty(name) && key.Contains(name, StringComparison.OrdinalIgnoreCase)) return slot;
        }

        return null;
    }

    // The cover was clicked: bring the app that plays forward, from its dock icon, its process or its package.
    private void BringPlayerForward(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return;
        if (PlayerSlot(source) is { } slot && slot.Item.Windows.Count > 0 && WindowActivator.Activate(slot.Item.Windows[0])) return;

        var key = PlayerKey(source);
        if (!key.Contains('!'))
        {
            foreach (var process in System.Diagnostics.Process.GetProcessesByName(key))
            {
                using (process)
                {
                    if (process.MainWindowHandle != IntPtr.Zero && WindowActivator.Activate(process.MainWindowHandle)) return;
                }
            }

            return;
        }

        // a store app: asking the shell to start it again brings its window up
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + key) { UseShellExecute = true })?.Dispose();
        }
        catch
        {
            // the shell may refuse; nothing to bring forward then
        }
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
