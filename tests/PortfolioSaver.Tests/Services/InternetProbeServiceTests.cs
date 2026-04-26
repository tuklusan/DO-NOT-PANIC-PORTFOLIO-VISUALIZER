using System.Reflection;
using PortfolioSaver.Shared.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class InternetProbeServiceTests
{
    [Fact]
    public void Constructor_DefaultsToBaiduAndFiveAttempts()
    {
        InternetProbeService service = new();

        Assert.Equal("baidu.com", GetPrivateField<string>(service, "_host"));
        Assert.Equal(5, GetPrivateField<int>(service, "_attempts"));
    }

    [Fact]
    public void IsInternetAvailable_UsesCachedResult_WhenCacheIsFresh()
    {
        InternetProbeService service = new(
            host: "invalid.invalid",
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
            host: "invalid.invalid",
            attempts: 1,
            timeoutMilliseconds: 250,
            cacheDuration: TimeSpan.FromHours(1));

        SetPrivateField(service, "_lastProbeUtc", DateTimeOffset.UtcNow);
        SetPrivateField(service, "_lastProbeResult", true);

        service.InvalidateCache();
        bool available = service.IsInternetAvailable();

        Assert.False(available);
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
