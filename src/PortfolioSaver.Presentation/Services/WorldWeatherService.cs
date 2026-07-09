// ============================================================================
// Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
// Proprietary rights reserved except as expressly licensed herein.
//
// DO NOT PANIC PORTFOLIO VISUALIZER
// This file is governed by the SANYALnet Labs Non-Commercial License in the
// root LICENSE file. Non-Commercial use is permitted; Commercial Use and use
// for AI/ML model training are prohibited unless separately authorized.
//
// Attribution is required: "Based on original work by Supratim Sanyal of
// SANYALnet Labs." See LICENSE for full terms, warranty disclaimer, termination,
// patent, trademark, and governing-law provisions.
// ============================================================================
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using PortfolioSaver.Data.Services;
using PortfolioSaver.Render.ViewModels;
using PortfolioSaver.Shared.Helpers;

namespace PortfolioSaver.Presentation.Services;

public sealed class WorldWeatherService
{
    private const string CacheFileName = "world-weather-cache.json";
    private const int MaxConcurrentWeatherFetches = 5;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _cachePath;
    private readonly Func<TimeSpan, HttpClient> _httpClientFactory;
    private readonly SemaphoreSlim _cacheOperationGate = new(1, 1);

    public WorldWeatherService()
        : this(
            Path.Combine(PathHelper.GetLocalDataDirectory(), CacheFileName),
            timeout => HttpClientFactory.Create(timeout))
    {
    }

    internal WorldWeatherService(string cachePath, Func<TimeSpan, HttpClient> httpClientFactory)
    {
        _cachePath = cachePath;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IReadOnlyDictionary<string, WeatherSnapshot>> GetWeatherAsync(
        IEnumerable<ClockCityViewModel> cities,
        bool networkAvailable,
        CancellationToken cancellationToken = default)
    {
        await _cacheOperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, WeatherSnapshot> cached = await LoadCacheAsync(cancellationToken).ConfigureAwait(false);
            if (!networkAvailable)
                return cached;

            using HttpClient client = _httpClientFactory(TimeSpan.FromSeconds(10));
            Dictionary<string, WeatherSnapshot> results = new(StringComparer.OrdinalIgnoreCase);
            List<ClockCityViewModel> weatherCities = cities
                .Where(city => city.SupportsWeather && !string.IsNullOrWhiteSpace(city.Key))
                .ToList();
            using SemaphoreSlim fetchGate = new(MaxConcurrentWeatherFetches);
            Task<KeyValuePair<string, WeatherSnapshot>?>[] fetchTasks = weatherCities
                .Select(city => FetchWeatherWithGateAsync(client, city, cached, fetchGate, cancellationToken))
                .ToArray();
            KeyValuePair<string, WeatherSnapshot>?[] fetched = await Task.WhenAll(fetchTasks).ConfigureAwait(false);
            foreach (KeyValuePair<string, WeatherSnapshot>? pair in fetched)
            {
                if (pair.HasValue)
                    results[pair.Value.Key] = pair.Value.Value;
            }

            // Keep the persisted cache scoped to the active city set so removed cities do not linger forever.
            await SaveCacheAsync(results, cancellationToken).ConfigureAwait(false);
            return results;
        }
        finally
        {
            _cacheOperationGate.Release();
        }
    }

    private static async Task<KeyValuePair<string, WeatherSnapshot>?> FetchWeatherWithGateAsync(
        HttpClient client,
        ClockCityViewModel city,
        IReadOnlyDictionary<string, WeatherSnapshot> cached,
        SemaphoreSlim fetchGate,
        CancellationToken cancellationToken)
    {
        await fetchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WeatherSnapshot snapshot = await FetchWeatherAsync(client, city, cancellationToken).ConfigureAwait(false);
            return new KeyValuePair<string, WeatherSnapshot>(city.Key, snapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return cached.TryGetValue(city.Key, out WeatherSnapshot? fallback)
                ? new KeyValuePair<string, WeatherSnapshot>(city.Key, fallback)
                : null;
        }
        finally
        {
            fetchGate.Release();
        }
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
        string? cacheDirectory = Path.GetDirectoryName(_cachePath);
        if (!string.IsNullOrWhiteSpace(cacheDirectory))
            Directory.CreateDirectory(cacheDirectory);
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
