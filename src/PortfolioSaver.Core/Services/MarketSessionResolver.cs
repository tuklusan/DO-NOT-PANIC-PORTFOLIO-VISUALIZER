using PortfolioSaver.Core.Enums;

namespace PortfolioSaver.Core.Services;

public sealed class MarketSessionResolver
{
    public MarketSession Resolve(DateTimeOffset utcNow)
    {
        TimeZoneInfo eastern;
        try
        {
            eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
        catch
        {
            return MarketSession.Unknown;
        }

        DateTimeOffset easternNow = TimeZoneInfo.ConvertTime(utcNow, eastern);
        if (easternNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return MarketSession.Closed;

        TimeOnly time = TimeOnly.FromDateTime(easternNow.DateTime);
        if (time >= new TimeOnly(4, 0) && time < new TimeOnly(9, 30))
            return MarketSession.PreMarket;
        if (time >= new TimeOnly(9, 30) && time < new TimeOnly(16, 0))
            return MarketSession.Regular;
        if (time >= new TimeOnly(16, 0) && time < new TimeOnly(20, 0))
            return MarketSession.AfterHours;

        return MarketSession.Closed;
    }
}
