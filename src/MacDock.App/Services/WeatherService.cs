using System.Net.Http;
using System.Text.Json;

namespace MacDock.App.Services;

public sealed record WeatherInfo(string Location, double Temperature, string Description, string Icon, string Condition);

public static class WeatherService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private const double _latitude = 0;
    private const double _longitude = 0;
    private const string _location = "Night City";

    public static async Task<WeatherInfo?> GetAsync()
    {
        try
        {
            var url = $"https://api.open-meteo.com/v1/forecast?latitude={_latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}&longitude={_longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}&current=temperature_2m,weather_code&timezone=auto";
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
            return new WeatherInfo(_location, temperature, description, icon, condition);
        }
        catch
        {
            return null;
        }
    }

    private static (string Condition, string Description, string Icon) Describe(int code) => code switch
    {
        0 => ("sunny", "Açık", "☀"),
        1 => ("mostly_sunny", "Az bulutlu", "🌤"),
        2 => ("partly_cloudy", "Parçalı bulutlu", "⛅"),
        3 => ("overcast", "Kapalı", "☁"),
        45 or 48 => ("fog", "Sisli", "🌫"),
        51 or 53 or 55 => ("light_rain", "Çisenti", "🌦"),
        56 or 57 => ("sleet", "Donan çisenti", "🌧"),
        61 => ("light_rain", "Hafif yağmurlu", "🌦"),
        63 => ("rain", "Yağmurlu", "🌧"),
        65 => ("heavy_rain", "Şiddetli yağmur", "🌧"),
        66 or 67 => ("sleet", "Donan yağmur", "🌧"),
        71 => ("light_snow", "Hafif kar", "🌨"),
        73 => ("snow", "Kar", "❄"),
        75 => ("heavy_snow", "Yoğun kar", "❄"),
        77 => ("snow", "Kar taneleri", "🌨"),
        80 => ("light_rain", "Hafif sağanak", "🌦"),
        81 => ("rain", "Sağanak", "🌦"),
        82 => ("heavy_rain", "Şiddetli sağanak", "🌧"),
        85 => ("snow", "Kar sağanağı", "🌨"),
        86 => ("heavy_snow", "Yoğun kar sağanağı", "🌨"),
        95 => ("thunderstorm", "Gök gürültülü", "⛈"),
        96 or 99 => ("thunderstorm", "Dolu fırtınası", "⛈"),
        _ => ("cloudy", "—", "🌡"),
    };
}
