using System.Linq;
using PortfolioSaver.Render.ViewModels;

namespace PortfolioSaver.Render.Services;

public sealed class FloatingClockBuilder
{
    private static readonly IReadOnlyList<ExchangeEntry> ExchangeEntries =
    [
        new("NewYork", "New York", "S&P 500", "^SPX", "NYSE", "Eastern Standard Time", "America/New_York", "US", 40.7128, -74.0060),
        new("London", "London", "FTSE 100", "^FTSE", "LSE", "GMT Standard Time", "Europe/London", "GB", 51.5072, -0.1276),
        new("Tokyo", "Tokyo", "Nikkei 225", "^N225", "TSE", "Tokyo Standard Time", "Asia/Tokyo", "JP", 35.6762, 139.6503),
        new("Shanghai", "Shanghai", "SSE Composite", "^SSEC", "SSE", "China Standard Time", "Asia/Shanghai", "CN", 31.2304, 121.4737),
        new("HongKong", "Hong Kong", "Hang Seng", "^HSI", "HKEX", "China Standard Time", "Asia/Hong_Kong", "HK", 22.3193, 114.1694),
        new("Mumbai", "Mumbai", "India 50 ETF", "INDY.US", "NSE", "India Standard Time", "Asia/Kolkata", "IN", 19.0760, 72.8777),
        new("Frankfurt", "Frankfurt", "DAX", "^GDAXI", "XETRA", "W. Europe Standard Time", "Europe/Berlin", "DE", 50.1109, 8.6821),
        new("Paris", "Paris", "CAC 40", "^FCHI", "EPA", "Romance Standard Time", "Europe/Paris", "FR", 48.8566, 2.3522),
        new("Toronto", "Toronto", "S&P/TSX", "^GSPTSE", "TSX", "Eastern Standard Time", "America/Toronto", "CA", 43.6532, -79.3832),
        new("Seoul", "Seoul", "KOSPI", "^KS11", "KRX", "Korea Standard Time", "Asia/Seoul", "KR", 37.5665, 126.9780),
        new("Sydney", "Sydney", "MSCI Australia ETF", "EWA.US", "ASX", "AUS Eastern Standard Time", "Australia/Sydney", "AU", -33.8688, 151.2093)
    ];

    public static IReadOnlyList<string> GetWorldIndexSymbols()
        => ExchangeEntries.Select(entry => entry.IndexSymbol).ToList();

    public FloatingClockViewModel BuildDefault(double startX = 48, double startY = 48)
    {
        (double localLatitude, double localLongitude, string localLabel) = ResolveLocalAnchor();

        FloatingClockViewModel clock = new()
        {
            X = startX,
            Y = startY,
            Width = 940,
            Height = 184,
            VelocityX = 3,
            VelocityY = 2,
            Title = "Global Markets",
            Subtitle = string.Empty
        };

        clock.Cities.Add(new ClockCityViewModel
        {
            Key = "Local",
            Label = localLabel,
            ZoneText = "Local",
            SupportsWeather = true,
            Latitude = localLatitude,
            Longitude = localLongitude,
            IsLocalSummary = true,
            ShowExchangeDetails = false
        });

        foreach (ExchangeEntry entry in ExchangeEntries)
        {
            clock.Cities.Add(new ClockCityViewModel
            {
                Key = entry.Key,
                Label = entry.City,
                ZoneText = entry.City,
                PrimaryTimeZoneId = entry.PrimaryTimeZoneId,
                SecondaryTimeZoneId = entry.SecondaryTimeZoneId,
                SupportsWeather = true,
                Latitude = entry.Latitude,
                Longitude = entry.Longitude,
                FlagCode = entry.FlagCode,
                IsLocalSummary = false,
                ShowExchangeDetails = true,
                ExchangeName = entry.ExchangeName,
                ExchangeSymbol = entry.IndexSymbol,
                CalendarExchangeCode = entry.CalendarExchangeCode
            });
        }

        return clock;
    }

    private static (double Latitude, double Longitude, string Label) ResolveLocalAnchor()
    {
        string localZoneId = TimeZoneInfo.Local.Id;
        return localZoneId switch
        {
            "Eastern Standard Time" => (40.7128, -74.0060, "Local Desk (New York)"),
            "Central Standard Time" => (41.8781, -87.6298, "Local Desk (Chicago)"),
            "Mountain Standard Time" => (39.7392, -104.9903, "Local Desk (Denver)"),
            "Pacific Standard Time" => (37.7749, -122.4194, "Local Desk (San Francisco)"),
            "Alaskan Standard Time" => (61.2181, -149.9003, "Local Desk (Anchorage)"),
            "Hawaiian Standard Time" => (21.3069, -157.8583, "Local Desk (Honolulu)"),
            "India Standard Time" => (19.0760, 72.8777, "Local Desk (Mumbai)"),
            "GMT Standard Time" => (51.5072, -0.1276, "Local Desk (London)"),
            "Tokyo Standard Time" => (35.6762, 139.6503, "Local Desk (Tokyo)"),
            _ => (40.7128, -74.0060, "Local Desk")
        };
    }

    private sealed record ExchangeEntry(
        string Key,
        string City,
        string ExchangeName,
        string IndexSymbol,
        string CalendarExchangeCode,
        string PrimaryTimeZoneId,
        string SecondaryTimeZoneId,
        string FlagCode,
        double Latitude,
        double Longitude);
}
