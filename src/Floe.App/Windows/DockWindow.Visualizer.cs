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
    private bool _musicPlaying;
    private bool _iconsDimmed;
    // the icons stay at full strength with the pointer away too (they used to rest at 0.6)
    private const double IdleIconOpacity = 1;

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

            // with nothing that could show a visualizer, the player is not asked at all
            if (!GlassSettings.Visualizer || !IsVisible || !_shown || _fullscreenHidden)
            {
                _visualizerRunning = false;
                ApplyVisualizerState();
                return;
            }

            _visualizerPolling = true;
            try
            {
                var playing = await VisualizerGlow.ShouldRunAsync();
                // the OLED panel stays on without music: its robot comes out when nothing plays;
                // a cyber theme shows its subtle standby HUD when no music is playing
                bool panel = !GlassSettings.Cyber && GlassSettings.VisualizerLook is (VisualizerStyle.Oled or VisualizerStyle.Led);
                bool cyberIdle = GlassSettings.Cyber && GlassSettings.GpuVisualizer;
                _musicPlaying = playing;
                _visualizerRunning = GlassSettings.Visualizer && IsVisible && _shown && !_fullscreenHidden && (playing || panel || cyberIdle);
                LogVisualizerState($"visualizer run={_visualizerRunning} playing={playing} visible={IsVisible} shown={_shown} fullscreen={_fullscreenHidden} look={GlassSettings.VisualizerLook} taskbar={DockSettings.Taskbar}");
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

    private string _visualizerLogged = "";

    // the poll asks every 1.5 s; the log only hears when the answer changes
    private void LogVisualizerState(string state)
    {
        if (state == _visualizerLogged) return;
        _visualizerLogged = state;
        Log(state);
    }

    private void OnVisualizerSettingChanged()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!GlassSettings.Visualizer) _visualizerRunning = false;
            else if (GlassSettings.Cyber && GlassSettings.GpuVisualizer && IsVisible && _shown && !_fullscreenHidden) _visualizerRunning = true;
            ApplyVisualizerState();
        });
    }

    private void ApplyVisualizerState()
    {
        HangWatch.Mark("visualizer state");
        if (!_visualizerRunning && _visualizerWindow is null)
        {
            return;
        }

        if (_visualizerWindow is null)
        {
            _visualizerWindow = new VisualizerWindow();
            _visualizerWindow.Show();
        }

        if (TaskbarOn)
        {
            // the strip lies over the bar, in the free glass between the start button, the icons and the clock
            PlaceTaskbarVisualizer();
            _visualizerWindow.SetRunning(_visualizerRunning, idle: GlassSettings.Cyber && !_musicPlaying);
            PlaceTaskbarVisualizer();
            PutDockUnderVisualizer();
            return;
        }

        // the strip covers the space the dock reserves at the bottom of the monitor: the dock's own floor, so it
        // stands where the dock does, above a Windows taskbar that shows
        var scale = DockScale;
        var area = AppBarBounds;
        var bounds = CapsuleScreenBounds();
        var dockRect = new Rect(bounds.Left / scale, bounds.Top / scale, (bounds.Right - bounds.Left) / scale, (bounds.Bottom - bounds.Top) / scale);
        // the feedback looks fill their whole strip, so they stand above the dock instead of hiding behind it;
        // the strip takes the extra height only when the GPU layer actually carries the look. It is placed at the
        // height it already has: a passing size would resize the layer, and a resize wipes a feedback field
        void Place(bool tall)
        {
            var height = (RestBarHeight + BottomMargin + (tall ? RestBarHeight * .12 : 0)) * FullScale;
            double left = area.Left / scale, top = area.Top / scale, width = (area.Right - area.Left) / scale, length = (area.Bottom - area.Top) / scale;
            // the strip lies along the dock's edge, as thick as the dock stands off it
            var strip = DockSettings.Edge switch
            {
                Floe.Core.Models.DockEdge.Top => new Rect(left, top, width, height),
                Floe.Core.Models.DockEdge.Left => new Rect(left, top, height, length),
                Floe.Core.Models.DockEdge.Right => new Rect((area.Right / scale) - height, top, height, length),
                _ => new Rect(left, (area.Bottom / scale) - height, width, height),
            };
            _visualizerWindow.Place(strip, scale, _hwnd, dockRect);
        }

        bool tall = _visualizerWindow.CyberGpuPlaying;
        Place(tall);
        _visualizerWindow.SetRunning(_visualizerRunning, idle: GlassSettings.Cyber && !_musicPlaying);
        if (_visualizerWindow.CyberGpuPlaying != tall) Place(!tall);

        SinkBelowVisualizer();
    }

    // the GPU looks' dock span as it was last handed over, in the strip's DIPs; empty until one is handed over,
    // so the first frame after the taskbar goes off is not taken for the span it left behind
    private Rect _placedGpuDock = Rect.Empty;

    /// <summary>
    /// Off the taskbar the GPU looks keep the capsule clear: the strip's gap is the dock's own rect there. The icons
    /// grow while the pointer runs over them, so the span follows the capsule as it stands this frame, the Cyberpunk
    /// card's frame around the logo included, instead of waiting for the next visualizer poll.
    /// </summary>
    private void FollowDockGpu()
    {
        if (TaskbarOn || _visualizerWindow is not { Showing: true }) return;
        static double Q(double v) => Math.Round(v * 2) / 2;
        var scale = DockScale;
        var bounds = CapsuleScreenBounds();
        // a card reaches this far past its icon's box, and grows with it: the hover's largest size is the widest
        var pay = _slots.Exists(slot => slot.Card is not null) ? CardBleed * HoverMaxScale : 0;
        var dock = new Rect(
            Q((bounds.Left / scale) - pay),
            Q((bounds.Top / scale) - pay),
            Q(((bounds.Right - bounds.Left) / scale) + (2 * pay)),
            Q(((bounds.Bottom - bounds.Top) / scale) + (2 * pay)));
        if (dock == _placedGpuDock) return;
        _placedGpuDock = dock;
        _visualizerWindow.SetGpuDock(dock);
    }

    // where the player's icon sits along the visualizer strip (0..1); the middle when it is not in the dock
    private double PlayerIconX(string? source)
    {
        if (PlayerSlot(source) is not { } slot) return 0.5;
        var area = _monitorInfo.Monitor;
        var scale = DockScale;
        var left = area.Left / scale;
        var width = Math.Max(1, (area.Right - area.Left) / scale);
        // the taskbar's icons do not stand where the dock's resting row would put them
        var x = TaskbarOn ? slot.Center : _restCapsuleLeft + slot.CenterLocal;
        if (DockSettings.Vertical)
        {
            // down a side the strip runs along the screen's height, from the top
            var top = area.Top / scale;
            var height = Math.Max(1, (area.Bottom - area.Top) / scale);
            return Math.Clamp((ToScreenDips(x, RestTop + (RestBarHeight / 2)).Y - top) / height, 0, 1);
        }

        return Math.Clamp((ToScreenX(x) - left) / width, 0, 1);
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
