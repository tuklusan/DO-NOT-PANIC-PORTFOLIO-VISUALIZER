using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using PortfolioSaver.Data.Services;
using PortfolioSaver.Render.ViewModels;
using PortfolioSaver.Shared.Helpers;

namespace PortfolioSaver.Screensaver.Services;

public sealed class WorldWeatherService
{
    private const string CacheFileName = "world-weather-cache.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _cachePath = Path.Combine(PathHelper.GetLocalDataDirectory(), CacheFileName);

    public async Task<IReadOnlyDictionary<string, WeatherSnapshot>> GetWeatherAsync(
        IEnumerable<ClockCityViewModel> cities,
        bool networkAvailable,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, WeatherSnapshot> cached = await LoadCacheAsync(cancellationToken);
        if (!networkAvailable)
            return cached;

        using HttpClient client = HttpClientFactory.Create(TimeSpan.FromSeconds(10));
        Dictionary<string, WeatherSnapshot> results = new(StringComparer.OrdinalIgnoreCase);
        foreach (ClockCityViewModel city in cities.Where(city => city.SupportsWeather))
        {
            try
            {
                WeatherSnapshot snapshot = await FetchWeatherAsync(client, city, cancellationToken);
                results[city.Key] = snapshot;
            }
            catch
            {
                if (cached.TryGetValue(city.Key, out WeatherSnapshot? fallback))
                    results[city.Key] = fallback;
            }
        }

        foreach ((string key, WeatherSnapshot value) in cached)
        {
            if (!results.ContainsKey(key))
                results[key] = value;
        }

        await SaveCacheAsync(results, cancellationToken);
        return results;
    }

    private static async Task<WeatherSnapshot> FetchWeatherAsync(HttpClient client, ClockCityViewModel city, CancellationToken cancellationToken)
    {
        string latitude = city.Latitude.ToString("0.####", CultureInfo.InvariantCulture);
        string longitude = city.Longitude.ToString("0.####", CultureInfo.InvariantCulture);
        string url = $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,weather_code,is_day&temperature_unit=celsius&forecast_days=1";

        using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        JsonElement current = document.RootElement.GetProperty("current");

        double temperature = current.GetProperty("temperature_2m").GetDouble();
        int weatherCode = current.GetProperty("weather_code").GetInt32();
        bool isDay = current.GetProperty("is_day").GetInt32() == 1;

        return new WeatherSnapshot
        {
            CityKey = city.Key,
            TemperatureCelsius = temperature,
            WeatherCode = weatherCode,
            IsDay = isDay,
            FetchTimestampUtc = DateTimeOffset.UtcNow
        };
    }

    private async Task<Dictionary<string, WeatherSnapshot>> LoadCacheAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_cachePath))
            return new Dictionary<string, WeatherSnapshot>(StringComparer.OrdinalIgnoreCase);

        await using FileStream stream = File.OpenRead(_cachePath);
        Dictionary<string, WeatherSnapshot>? cache = await JsonSerializer.DeserializeAsync<Dictionary<string, WeatherSnapshot>>(stream, JsonOptions, cancellationToken);
        return cache ?? new Dictionary<string, WeatherSnapshot>(StringComparer.OrdinalIgnoreCase);
    }

    private async Task SaveCacheAsync(IReadOnlyDictionary<string, WeatherSnapshot> cache, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        await using FileStream stream = File.Create(_cachePath);
        await JsonSerializer.SerializeAsync(stream, cache, JsonOptions, cancellationToken);
    }

    public static string GetGlyph(int weatherCode, bool isDay) => weatherCode switch
    {
        0 => isDay ? "\u2600" : "\u263E",
        1 => isDay ? "\u2600" : "\u263E",
        2 => "\u26C5",
        3 => "\u2601",
        45 or 48 => "\u224B",
        51 or 53 or 55 or 56 or 57 => "\u22F1",
        61 or 63 or 65 or 66 or 67 => "\u2602",
        71 or 73 or 75 or 77 => "\u2744",
        80 or 81 or 82 => "\u2602",
        85 or 86 => "\u2744",
        95 or 96 or 99 => "\u26C8",
        _ => "\u2601"
    };
}

public sealed class WeatherSnapshot
{
    public string CityKey { get; set; } = string.Empty;
    public double TemperatureCelsius { get; set; }
    public int WeatherCode { get; set; }
    public bool IsDay { get; set; }
    public DateTimeOffset FetchTimestampUtc { get; set; }
}
