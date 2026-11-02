using System.Net.Http;
using System.Text.Json;

namespace Floe.App.Services;

public sealed record WeatherInfo(string Location, double Temperature, string Description, string Icon, string Condition);

public sealed record WeatherCity(string Name, string? Country, double Latitude, double Longitude);

public static class WeatherService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>The name the dock shows: the CP77 dock always lives in Night City, the weather stays the real city's.</summary>
    public static string Place => Floe.Core.Services.Product.Cp77 ? "Night City" : GlassSettings.WeatherCity ?? Loc.T("Şehir seç");

    /// <summary>The best match for a typed city, or null when there is none or the search failed.</summary>
    public static async Task<WeatherCity?> FindCityAsync(string name)
    {
        try
        {
            var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(name)}&count=1&language=tr&format=json";
            using var response = await Http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!document.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            {
                return null;
            }

            var best = results[0];
            var country = best.TryGetProperty("country", out var value) ? value.GetString() : null;
            return new WeatherCity(best.GetProperty("name").GetString() ?? name, country, best.GetProperty("latitude").GetDouble(), best.GetProperty("longitude").GetDouble());
        }
        catch
        {
            return null;
        }
    }

    public static async Task<WeatherInfo?> GetAsync()
    {
        if (GlassSettings.WeatherCity is null)
        {
            return null;
        }

        try
        {
            var latitude = GlassSettings.WeatherLatitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var longitude = GlassSettings.WeatherLongitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var url = $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,weather_code&timezone=auto";
            using var response = await Http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!document.RootElement.TryGetProperty("current", out var current))
            {
                return null;
            }

            var temperature = current.GetProperty("temperature_2m").GetDouble();
            var code = current.GetProperty("weather_code").GetInt32();
            var (condition, description, icon) = Describe(code);
            return new WeatherInfo(Place, temperature, description, icon, condition);
        }
        catch
        {
            return null;
        }
    }

    private static (string Condition, string Description, string Icon) Describe(int code) => code switch
    {
        0 => ("sunny", Loc.English ? "Clear" : "Açık", "☀"),
        1 => ("mostly_sunny", Loc.T("Az bulutlu"), "🌤"),
        2 => ("partly_cloudy", Loc.T("Parçalı bulutlu"), "⛅"),
        3 => ("overcast", Loc.T("Kapalı"), "☁"),
        45 or 48 => ("fog", Loc.T("Sisli"), "🌫"),
        51 or 53 or 55 => ("light_rain", Loc.T("Çisenti"), "🌦"),
        56 or 57 => ("sleet", Loc.T("Donan çisenti"), "🌧"),
        61 => ("light_rain", Loc.T("Hafif yağmurlu"), "🌦"),
        63 => ("rain", Loc.T("Yağmurlu"), "🌧"),
        65 => ("heavy_rain", Loc.T("Şiddetli yağmur"), "🌧"),
        66 or 67 => ("sleet", Loc.T("Donan yağmur"), "🌧"),
        71 => ("light_snow", Loc.T("Hafif kar"), "🌨"),
        73 => ("snow", Loc.T("Kar"), "❄"),
        75 => ("heavy_snow", Loc.T("Yoğun kar"), "❄"),
        77 => ("snow", Loc.T("Kar taneleri"), "🌨"),
        80 => ("light_rain", Loc.T("Hafif sağanak"), "🌦"),
        81 => ("rain", Loc.T("Sağanak"), "🌦"),
        82 => ("heavy_rain", Loc.T("Şiddetli sağanak"), "🌧"),
        85 => ("snow", Loc.T("Kar sağanağı"), "🌨"),
        86 => ("heavy_snow", Loc.T("Yoğun kar sağanağı"), "🌨"),
        95 => ("thunderstorm", Loc.T("Gök gürültülü"), "⛈"),
        96 or 99 => ("thunderstorm", Loc.T("Dolu fırtınası"), "⛈"),
        _ => ("cloudy", "—", "🌡"),
    };
}
