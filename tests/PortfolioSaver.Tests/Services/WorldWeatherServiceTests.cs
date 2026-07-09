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
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using PortfolioSaver.Presentation.Services;
using PortfolioSaver.Render.ViewModels;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class WorldWeatherServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [Fact]
    public async Task GetWeatherAsync_FetchesCitiesInParallel_WithConcurrencyLimit()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        string cachePath = Path.Combine(tempRoot, "weather-cache.json");
        using ConcurrencyProbeHandler handler = new(expectedFirstWave: 5);
        WorldWeatherService service = new(cachePath, _ => new HttpClient(handler));
        IReadOnlyList<ClockCityViewModel> cities = Enumerable.Range(0, 7)
            .Select(index => new ClockCityViewModel
            {
                Key = $"CITY{index}",
                SupportsWeather = true,
                Latitude = index,
                Longitude = index
            })
            .ToList();

        try
        {
            Task<IReadOnlyDictionary<string, WeatherSnapshot>> fetchTask = service.GetWeatherAsync(cities, networkAvailable: true);
            await handler.FirstWaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(5, Volatile.Read(ref handler.RequestCount));
            Assert.Equal(5, Volatile.Read(ref handler.MaxActiveRequests));

            handler.ReleaseResponses.SetResult();
            IReadOnlyDictionary<string, WeatherSnapshot> results = await fetchTask;

            Assert.Equal(7, results.Count);
            Assert.True(handler.MaxActiveRequests <= 5);
        }
        finally
        {
            handler.ReleaseResponses.TrySetResult();
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GetWeatherAsync_UsesCachedFallback_WhenFetchFails()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        string cachePath = Path.Combine(tempRoot, "weather-cache.json");
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(
            new Dictionary<string, WeatherSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                ["FAIL"] = new()
                {
                    CityKey = "FAIL",
                    TemperatureCelsius = 12.25,
                    WeatherCode = 3,
                    IsDay = false,
                    FetchTimestampUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
                }
            },
            JsonOptions));
        using StaticWeatherHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        WorldWeatherService service = new(cachePath, _ => new HttpClient(handler));

        try
        {
            IReadOnlyDictionary<string, WeatherSnapshot> results = await service.GetWeatherAsync([CreateCity("FAIL")], networkAvailable: true);

            WeatherSnapshot snapshot = Assert.Single(results).Value;
            Assert.Equal(12.25, snapshot.TemperatureCelsius);
            Assert.Equal(3, snapshot.WeatherCode);
            Assert.False(snapshot.IsDay);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GetWeatherAsync_OmitsFailedCity_WhenNoCachedFallbackExists()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        string cachePath = Path.Combine(tempRoot, "weather-cache.json");
        using StaticWeatherHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        WorldWeatherService service = new(cachePath, _ => new HttpClient(handler));

        try
        {
            IReadOnlyDictionary<string, WeatherSnapshot> results = await service.GetWeatherAsync([CreateCity("MISS")], networkAvailable: true);

            Assert.Empty(results);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GetWeatherAsync_TrimsStaleCacheEntries_NotPresentInRequestedCities()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        string cachePath = Path.Combine(tempRoot, "weather-cache.json");
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(
            new Dictionary<string, WeatherSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                ["STALE"] = new() { CityKey = "STALE", TemperatureCelsius = 9, WeatherCode = 2, IsDay = true },
                ["FRESH"] = new() { CityKey = "FRESH", TemperatureCelsius = 10, WeatherCode = 1, IsDay = true }
            },
            JsonOptions));
        using StaticWeatherHandler handler = new(_ => CreateWeatherResponse(24.5, weatherCode: 0, isDay: true));
        WorldWeatherService service = new(cachePath, _ => new HttpClient(handler));

        try
        {
            IReadOnlyDictionary<string, WeatherSnapshot> results = await service.GetWeatherAsync([CreateCity("FRESH")], networkAvailable: true);
            Dictionary<string, WeatherSnapshot>? saved = JsonSerializer.Deserialize<Dictionary<string, WeatherSnapshot>>(
                await File.ReadAllTextAsync(cachePath),
                JsonOptions);

            Assert.Single(results);
            Assert.True(results.ContainsKey("FRESH"));
            Assert.NotNull(saved);
            Assert.Single(saved!);
            Assert.True(saved!.ContainsKey("FRESH"));
            Assert.False(saved.ContainsKey("STALE"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GetWeatherAsync_ReturnsCachedWeatherWithoutHttp_WhenNetworkUnavailable()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        string cachePath = Path.Combine(tempRoot, "weather-cache.json");
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(
            new Dictionary<string, WeatherSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                ["OFFLINE"] = new()
                {
                    CityKey = "OFFLINE",
                    TemperatureCelsius = 17,
                    WeatherCode = 2,
                    IsDay = true,
                    FetchTimestampUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
                }
            },
            JsonOptions));
        using StaticWeatherHandler handler = new(_ => throw new InvalidOperationException("HTTP should not be used while offline."));
        WorldWeatherService service = new(cachePath, _ => new HttpClient(handler));

        try
        {
            IReadOnlyDictionary<string, WeatherSnapshot> results = await service.GetWeatherAsync(
                [CreateCity("OFFLINE")],
                networkAvailable: false);

            WeatherSnapshot snapshot = Assert.Single(results).Value;
            Assert.Equal(17, snapshot.TemperatureCelsius);
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GetWeatherAsync_PropagatesCancellation_AndReleasesFetchGate()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        string cachePath = Path.Combine(tempRoot, "weather-cache.json");
        using ConcurrencyProbeHandler handler = new(expectedFirstWave: 1);
        WorldWeatherService service = new(cachePath, _ => new HttpClient(handler));
        using CancellationTokenSource cancellation = new();

        try
        {
            Task<IReadOnlyDictionary<string, WeatherSnapshot>> fetchTask = service.GetWeatherAsync(
                [CreateCity("CANCEL")],
                networkAvailable: true,
                cancellation.Token);
            await handler.FirstWaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cancellation.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetchTask);
            Assert.Equal(0, Volatile.Read(ref handler.ActiveRequests));
        }
        finally
        {
            handler.ReleaseResponses.TrySetResult();
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static ClockCityViewModel CreateCity(string key) => new()
    {
        Key = key,
        SupportsWeather = true,
        Latitude = 1,
        Longitude = 1
    };

    private static HttpResponseMessage CreateWeatherResponse(double temperature, int weatherCode, bool isDay) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            $$"""
            {
              "current": {
                "temperature_2m": {{temperature.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
                "weather_code": {{weatherCode}},
                "is_day": {{(isDay ? 1 : 0)}}
              }
            }
            """)
    };

    private sealed class ConcurrencyProbeHandler : HttpMessageHandler
    {
        private readonly int _expectedFirstWave;
        public ConcurrencyProbeHandler(int expectedFirstWave)
        {
            _expectedFirstWave = expectedFirstWave;
        }

        public int ActiveRequests;
        public int RequestCount;
        public int MaxActiveRequests;
        public TaskCompletionSource FirstWaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseResponses { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int active = Interlocked.Increment(ref ActiveRequests);
            int currentMax;
            do
            {
                currentMax = Volatile.Read(ref MaxActiveRequests);
                if (active <= currentMax)
                    break;
            }
            while (Interlocked.CompareExchange(ref MaxActiveRequests, active, currentMax) != currentMax);

            int requestCount = Interlocked.Increment(ref RequestCount);
            if (requestCount == _expectedFirstWave)
                FirstWaveStarted.TrySetResult();

            try
            {
                await ReleaseResponses.Task.WaitAsync(cancellationToken);
                return CreateWeatherResponse(21.5, weatherCode: 1, isDay: true);
            }
            finally
            {
                Interlocked.Decrement(ref ActiveRequests);
            }
        }
    }

    private sealed class StaticWeatherHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;
        private int _requestCount;

        public StaticWeatherHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return Task.FromResult(_responseFactory(request));
        }

        public int RequestCount => Volatile.Read(ref _requestCount);
    }
}
