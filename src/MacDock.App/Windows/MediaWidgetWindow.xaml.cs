using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MacDock.App.Services;
using MacDock.Core.Interop;

namespace MacDock.App.Windows;

public partial class MediaWidgetWindow : Window, IBubbleWidget
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly BubbleController _bubble;
    private bool _refreshing;

    public bool IsBubbleDragging => _bubble.IsDragging;

    public bool Blur
    {
        get => _bubble.Blur;
        set => _bubble.Blur = value;
    }

    public MediaWidgetWindow()
    {
        InitializeComponent();
        WidgetChrome.Apply(this, Root, Panel);
        _bubble = new BubbleController(this, Backdrop, BackdropBrush, RootScale) { LayoutId = "media" };

        PreviousButton.Click += async (_, _) => await MediaService.PreviousAsync();
        PlayPauseButton.Click += async (_, _) => await MediaService.TogglePlayPauseAsync();
        NextButton.Click += async (_, _) => await MediaService.NextAsync();
        SettingsButton.Click += (_, _) => Keyboard.SendWinKey(0x41);

        _timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) =>
        {
            SetGlyph(PreviousGlyph, "previous");
            SetGlyph(NextGlyph, "next");
            SetGlyph(SettingsGlyph, "settings");
            SetGlyph(PlayPauseGlyph, "play");
            await RefreshAsync();
            _timer.Start();
        };

        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _bubble.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _bubble.Stop();
        base.OnClosed(e);
    }

    private void SetGlyph(System.Windows.Controls.Image target, string key)
    {
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        target.Source = DotMatrixIcons.Render(key, target.Width, Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF), dpi);
    }

    private async Task RefreshAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            var media = await MediaService.GetAsync();
            if (media is null || string.IsNullOrWhiteSpace(media.Title))
            {
                MediaTitle.Text = "—";
                MediaArtist.Text = string.Empty;
                Artwork.Source = null;
                ProgressScale.ScaleX = 0;
                return;
            }

            SetGlyph(PlayPauseGlyph, await MediaService.IsPlayingAsync() ? "pause" : "play");
            MediaTitle.Text = media.Title;
            MediaArtist.Text = media.Artist;
            Artwork.Source = media.Artwork;
            ProgressScale.ScaleX = media.Progress;
        }
        finally
        {
            _refreshing = false;
        }
    }
}


