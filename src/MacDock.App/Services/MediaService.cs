using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;

namespace MacDock.App.Services;

public sealed record MediaInfo(string Title, string Artist, double Progress, ImageSource? Artwork);

public static class MediaService
{
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
            try
            {
                var timeline = session.GetTimelineProperties();
                var total = (timeline.EndTime - timeline.StartTime).TotalSeconds;
                if (total > 0)
                {
                    progress = Math.Clamp((timeline.Position - timeline.StartTime).TotalSeconds / total, 0, 1);
                }
            }
            catch
            {
                // timeline may be unavailable
            }

            return new MediaInfo(
                properties.Title ?? string.Empty,
                properties.Artist ?? string.Empty,
                progress,
                await ReadArtworkAsync(properties));
        }
        catch
        {
            return null;
        }
    }

    private static async Task<ImageSource?> ReadArtworkAsync(GlobalSystemMediaTransportControlsSessionMediaProperties properties)
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
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        return manager?.GetCurrentSession();
    }
}
