using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;

namespace Floe.App.Services;

public sealed record MediaInfo(string Title, string Artist, double Progress, ImageSource? Artwork)
{
    /// <summary>Where the track is and how long it runs, in seconds; 0 when the player does not say.</summary>
    public double Position { get; init; }
    public double Duration { get; init; }

    public bool Playing { get; init; }
}

public static class MediaService
{
    private static GlobalSystemMediaTransportControlsSessionManager? _manager;

    // the artwork of the current track, so the 1 s poll does not decode it again
    private static string? _artworkTitle;
    private static string? _artworkArtist;
    private static ImageSource? _artwork;

    public static async Task<MediaInfo?> GetAsync()
    {
        try
        {
            var session = await GetSessionAsync();
            if (session is null)
            {
                return null;
            }

            var properties = await session.TryGetMediaPropertiesAsync();
            if (properties is null)
            {
                return null;
            }

            var progress = 0.0;
            double position = 0, duration = 0;
            try
            {
                var timeline = session.GetTimelineProperties();
                var total = (timeline.EndTime - timeline.StartTime).TotalSeconds;
                if (total > 0)
                {
                    // the player reports its position only now and then; carry it on to now while it plays
                    var since = session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                        ? Math.Max(0, (DateTimeOffset.Now - timeline.LastUpdatedTime).TotalSeconds)
                        : 0;
                    if (since > total) since = 0;
                    position = Math.Clamp((timeline.Position - timeline.StartTime).TotalSeconds + since, 0, total);
                    duration = total;
                    progress = position / total;
                }
            }
            catch
            {
                // timeline may be unavailable
            }

            var title = properties.Title ?? string.Empty;
            var artist = properties.Artist ?? string.Empty;
            bool playing = false;
            try
            {
                playing = session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            }
            catch
            {
                // playback info may be unavailable
            }

            return new MediaInfo(
                title,
                artist,
                progress,
                await ReadArtworkAsync(properties, title, artist))
            {
                Position = position,
                Duration = duration,
                Playing = playing,
            };
        }
        catch
        {
            return null;
        }
    }

    private static async Task<ImageSource?> ReadArtworkAsync(
        GlobalSystemMediaTransportControlsSessionMediaProperties properties,
        string title,
        string artist)
    {
        if (title == _artworkTitle && artist == _artworkArtist)
        {
            return _artwork;
        }

        var artwork = await DecodeArtworkAsync(properties);
        if (artwork is not null)
        {
            // no artwork is not cached: a late thumbnail for the same track must still appear
            _artworkTitle = title;
            _artworkArtist = artist;
            _artwork = artwork;
        }

        return artwork;
    }

    private static async Task<ImageSource?> DecodeArtworkAsync(GlobalSystemMediaTransportControlsSessionMediaProperties properties)
    {
        try
        {
            if (properties.Thumbnail is null)
            {
                return null;
            }

            using var stream = await properties.Thumbnail.OpenReadAsync();
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream.AsStreamForRead();
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public static async Task TogglePlayPauseAsync()
    {
        try
        {
            var session = await GetSessionAsync();
            if (session is not null)
            {
                await session.TryTogglePlayPauseAsync();
            }
        }
        catch
        {
            // ignore
        }
    }

    public static async Task PreviousAsync()
    {
        try
        {
            var session = await GetSessionAsync();
            if (session is not null)
            {
                await session.TrySkipPreviousAsync();
            }
        }
        catch
        {
            // ignore
        }
    }

    public static async Task NextAsync()
    {
        try
        {
            var session = await GetSessionAsync();
            if (session is not null)
            {
                await session.TrySkipNextAsync();
            }
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>True when the current SMTC session reports Playing. Never throws.</summary>
    public static async Task<bool> IsPlayingAsync()
    {
        try
        {
            var session = await GetSessionAsync();
            return session?.GetPlaybackInfo()?.PlaybackStatus ==
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<GlobalSystemMediaTransportControlsSession?> GetSessionAsync()
    {
        if (_manager is null)
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }

        try
        {
            if (_manager is null)
            {
                return null;
            }

            // a long-lived manager can keep a paused tab as "current" while another app plays
            foreach (var session in _manager.GetSessions())
            {
                if (session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    return session;
                }
            }

            return _manager.GetCurrentSession();
        }
        catch
        {
            // a dead manager is re-requested on the next call
            _manager = null;
            return null;
        }
    }
}
