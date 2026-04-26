using System.Reflection;
using System.Text.Json;
using PortfolioSaver.Screensaver.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class ExchangeMarketCalendarServiceTests
{
    [Fact]
    public void IsClosedHoliday_ExplicitClosedStatus_ReturnsTrue()
    {
        bool result = InvokeIsClosedHoliday("""{"date":"2026-01-15","status":"closed"}""");
        Assert.True(result);
    }

    [Fact]
    public void IsClosedHoliday_ClosedInName_ReturnsTrue()
    {
        bool result = InvokeIsClosedHoliday("""{"date":"2026-01-15","name":"Exchange closed for maintenance"}""");
        Assert.True(result);
    }

    [Fact]
    public void IsClosedHoliday_AmbiguousPayload_DoesNotAssumeClosed()
    {
        bool result = InvokeIsClosedHoliday("""{"date":"2026-01-15","name":"Special observance"}""");
        Assert.False(result);
    }

    [Fact]
    public void IsClosedHoliday_NoSignal_DoesNotAssumeClosed()
    {
        bool result = InvokeIsClosedHoliday("""{"date":"2026-01-15"}""");
        Assert.False(result);
    }

    private static bool InvokeIsClosedHoliday(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        MethodInfo? method = typeof(ExchangeMarketCalendarService).GetMethod(
            "IsClosedHoliday",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        object? value = method!.Invoke(null, [doc.RootElement]);
        return Assert.IsType<bool>(value);
    }
}
