using System.Windows;
using System.Windows.Input;
using Floe.App.Services;
using Floe.Core.Services;

namespace Floe.App.Windows;

public partial class SettingsWindow
{
    private void InitializeWeatherCity()
    {
        WeatherCityBox.Text = GlassSettings.WeatherCity ?? string.Empty;
        WeatherCityStatus.Text = GlassSettings.WeatherCity is null ? Loc.T("Şehrini yazıp Bul'a bas.") : CityNote(GlassSettings.WeatherCity);
        WeatherCityFind.Click += async (_, _) => await FindCityAsync();
        WeatherCityBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) await FindCityAsync();
        };
    }

    private static string CityNote(string place) => Product.Cp77 ? string.Format(Loc.T("{0} havası, dockta Night City yazar."), place) : place;

    private async Task FindCityAsync()
    {
        var name = WeatherCityBox.Text.Trim();
        if (name.Length == 0) return;

        WeatherCityFind.IsEnabled = false;
        var city = await WeatherService.FindCityAsync(name);
        WeatherCityFind.IsEnabled = true;
        if (city is null)
        {
            WeatherCityStatus.Text = Loc.T("Bulunamadı, başka bir yazımla dene.");
            return;
        }

        GlassSettings.SetWeatherCity(city.Name, city.Latitude, city.Longitude);
        WeatherCityBox.Text = city.Name;
        WeatherCityStatus.Text = CityNote(city.Country is null ? city.Name : $"{city.Name}, {city.Country}");
    }
}
