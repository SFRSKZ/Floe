using Windows.Media.Control;

namespace MacDock.App.Services;

public sealed record MediaInfo(string Title, string Artist);

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

            return new MediaInfo(properties.Title ?? string.Empty, properties.Artist ?? string.Empty);
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

    private static async Task<GlobalSystemMediaTransportControlsSession?> GetSessionAsync()
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        return manager?.GetCurrentSession();
    }
}
