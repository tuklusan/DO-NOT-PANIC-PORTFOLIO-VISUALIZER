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
}
