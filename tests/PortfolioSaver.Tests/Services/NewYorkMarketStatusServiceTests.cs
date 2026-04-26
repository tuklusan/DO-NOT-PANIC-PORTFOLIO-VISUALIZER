using Xunit;
using PortfolioSaver.Core.Services;

namespace PortfolioSaver.Tests.Services;

public sealed class NewYorkMarketStatusServiceTests
{
    private readonly NewYorkMarketStatusService _service = new();

    [Fact]
    public void FormatStatusLine_DuringRegularSession_ShowsClosingCountdown()
    {
        DateTimeOffset utc = new(2026, 1, 15, 15, 0, 0, TimeSpan.Zero); // 10:00 AM ET

        string status = _service.FormatStatusLine(utc);

        Assert.Contains("Market (New York): Regular", status);
        Assert.Contains("Closing in 06:00", status);
    }

    [Fact]
    public void FormatStatusLine_DuringPreMarket_ShowsOpeningCountdown()
    {
        DateTimeOffset utc = new(2026, 1, 15, 14, 0, 0, TimeSpan.Zero); // 9:00 AM ET

        string status = _service.FormatStatusLine(utc);

        Assert.Contains("Pre-Market", status);
        Assert.Contains("Opening in 00h30m", status);
    }

    [Fact]
    public void FormatStatusLine_DuringWeekend_ShowsOpeningCountdownWithDayPortion()
    {
        DateTimeOffset utc = new(2026, 1, 17, 15, 0, 0, TimeSpan.Zero); // Saturday

        string status = _service.FormatStatusLine(utc);

        Assert.Contains("Closed", status);
        Assert.Contains("Opening in 02d23h30m", status);
    }

    [Fact]
    public void FormatStatusLine_DuringHoliday_ShowsClosed()
    {
        DateTimeOffset utc = new(2026, 12, 25, 15, 0, 0, TimeSpan.Zero); // Christmas (NYSE closed)

        string status = _service.FormatStatusLine(utc);

        Assert.Contains("Closed", status);
        Assert.Contains("Opening in", status);
    }

    [Fact]
    public void FormatStatusLine_DuringEarlyCloseDay_UsesEarlyCloseCountdown()
    {
        DateTimeOffset utc = new(2026, 11, 27, 17, 0, 0, TimeSpan.Zero); // Day after Thanksgiving, noon ET

        string status = _service.FormatStatusLine(utc);

        Assert.Contains("Regular", status);
        Assert.Contains("Closing in 01:00", status);
    }
}
