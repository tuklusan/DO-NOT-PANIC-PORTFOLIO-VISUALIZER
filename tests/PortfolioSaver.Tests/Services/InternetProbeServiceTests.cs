using System.Net;
using System.Reflection;
using PortfolioSaver.Shared.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class InternetProbeServiceTests
{
    [Fact]
    public void Constructor_DefaultsToHttpProbeEndpointsAndTwoAttempts()
    {
        InternetProbeService service = new();

        Assert.Equal(2, GetPrivateField<int>(service, "_attempts"));
        Assert.Equal(2, GetPrivateField<string[]>(service, "_probeUrls").Length);
        Assert.Contains("https://www.msftconnecttest.com/connecttest.txt", GetPrivateField<string[]>(service, "_probeUrls"));
    }

    [Fact]
    public void IsInternetAvailable_UsesCachedResult_WhenCacheIsFresh()
    {
        InternetProbeService service = new(
            probeUrls: ["https://invalid.invalid"],
            attempts: 1,
            timeoutMilliseconds: 250,
            cacheDuration: TimeSpan.FromHours(1));

        SetPrivateField(service, "_lastProbeUtc", DateTimeOffset.UtcNow);
        SetPrivateField(service, "_lastProbeResult", true);

        bool available = service.IsInternetAvailable();

        Assert.True(available);
    }

    [Fact]
    public void InvalidateCache_ClearsCacheAndForcesFreshProbe()
    {
        InternetProbeService service = new(
            probeUrls: ["https://invalid.invalid"],
            attempts: 1,
            timeoutMilliseconds: 250,
            cacheDuration: TimeSpan.FromHours(1));

        SetPrivateField(service, "_lastProbeUtc", DateTimeOffset.UtcNow);
        SetPrivateField(service, "_lastProbeResult", true);

        service.InvalidateCache();
        bool available = service.IsInternetAvailable();

        Assert.False(available);
    }

    [Fact]
    public void Constructor_NormalizesBareHostsToHttpsUrls()
    {
        InternetProbeService service = new(
            probeUrls: ["example.com"],
            attempts: 1,
            timeoutMilliseconds: 250,
            cacheDuration: TimeSpan.FromHours(1));

        Assert.Equal(new[] { "https://example.com" }, GetPrivateField<string[]>(service, "_probeUrls"));
    }

    [Fact]
    public async Task IsInternetAvailableAsync_CollapsesConcurrentCacheMissesToSingleProbe()
    {
        int requestCount = 0;
        InternetProbeService service = new(
            probeUrls: ["https://probe.test"],
            attempts: 1,
            timeoutMilliseconds: 1000,
            cacheDuration: TimeSpan.FromMinutes(1),
            messageHandlerFactory: () => new FakeProbeHandler(async (_, cancellationToken) =>
            {
                Interlocked.Increment(ref requestCount);
                await Task.Delay(100, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }));

        bool[] results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.IsInternetAvailableAsync()));

        Assert.All(results, Assert.True);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task IsInternetAvailableAsync_CollapsesConcurrentExpiredCacheRefreshesToSingleProbe()
    {
        int requestCount = 0;
        InternetProbeService service = new(
            probeUrls: ["https://probe.test"],
            attempts: 1,
            timeoutMilliseconds: 1000,
            cacheDuration: TimeSpan.FromMilliseconds(1),
            messageHandlerFactory: () => new FakeProbeHandler(async (_, cancellationToken) =>
            {
                Interlocked.Increment(ref requestCount);
                await Task.Delay(100, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }));
        SetPrivateField(service, "_lastProbeUtc", DateTimeOffset.UtcNow.AddMinutes(-1));
        SetPrivateField(service, "_lastProbeResult", false);

        bool[] results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => service.IsInternetAvailableAsync()));

        Assert.All(results, Assert.True);
        Assert.Equal(1, requestCount);
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        FieldInfo? field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        object? value = field!.GetValue(instance);
        return Assert.IsType<T>(value);
    }

    private static void SetPrivateField<T>(object instance, string fieldName, T value)
    {
        FieldInfo? field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(instance, value);
    }

    private sealed class FakeProbeHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request, cancellationToken);
    }
}
