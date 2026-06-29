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
using System.Linq;
using PortfolioSaver.Render.ViewModels;

namespace PortfolioSaver.Render.Services;

public sealed class FloatingClockBuilder
{
    private static readonly IReadOnlyList<ExchangeEntry> ExchangeEntries =
    [
        new("NewYorkNasdaq", "New York", "Nasdaq Composite", "^IXIC", "NYSE", "Eastern Standard Time", "America/New_York", "US", 40.7128, -74.0060),
        new("Shanghai", "Shanghai", "SSE Composite", "000001.SS", "SSE", "China Standard Time", "Asia/Shanghai", "CN", 31.2304, 121.4737),
        new("Tokyo", "Tokyo", "Nikkei 225", "^N225", "TSE", "Tokyo Standard Time", "Asia/Tokyo", "JP", 35.6762, 139.6503),
        new("Euronext", "Paris", "Euro Stoxx 50", "^STOXX50E", "EPA", "Romance Standard Time", "Europe/Paris", "FR", 48.8566, 2.3522),
        new("Shenzhen", "Shenzhen", "SZSE Component", "399001.SZ", "SSE", "China Standard Time", "Asia/Shanghai", "CN", 22.5431, 114.0579),
        new("HongKong", "Hong Kong", "Hang Seng", "^HSI", "HKEX", "China Standard Time", "Asia/Hong_Kong", "HK", 22.3193, 114.1694),
        new("Mumbai", "Mumbai", "Nifty 50", "^NSEI", "NSE", "India Standard Time", "Asia/Kolkata", "IN", 19.0760, 72.8777),
        new("Toronto", "Toronto", "S&P/TSX", "^GSPTSE", "TSX", "Eastern Standard Time", "America/Toronto", "CA", 43.6532, -79.3832),
        new("Seoul", "Seoul", "KOSPI", "^KS11", "KRX", "Korea Standard Time", "Asia/Seoul", "KR", 37.5665, 126.9780),
        new("Taipei", "Taipei", "TSEC Weighted", "^TWII", "TSE", "Taipei Standard Time", "Asia/Taipei", "TW", 25.0330, 121.5654),
        new("London", "London", "FTSE 100", "^FTSE", "LSE", "GMT Standard Time", "Europe/London", "GB", 51.5072, -0.1276),
        new("Frankfurt", "Frankfurt", "DAX", "^GDAXI", "XETRA", "W. Europe Standard Time", "Europe/Berlin", "DE", 50.1109, 8.6821),
        new("Zurich", "Zurich", "Swiss Market", "^SSMI", "SIX", "W. Europe Standard Time", "Europe/Zurich", "CH", 47.3769, 8.5417),
        new("Sydney", "Sydney", "S&P/ASX 200", "^AXJO", "ASX", "AUS Eastern Standard Time", "Australia/Sydney", "AU", -33.8688, 151.2093),
        new("Riyadh", "Riyadh", "Tadawul All Share", "^TASI.SR", "TADAWUL", "Arab Standard Time", "Asia/Riyadh", "SA", 24.7136, 46.6753),
        new("SaoPaulo", "Sao Paulo", "Ibovespa", "^BVSP", "BVMF", "E. South America Standard Time", "America/Sao_Paulo", "BR", -23.5505, -46.6333),
        new("Johannesburg", "Johannesburg", "JSE Top 40", "^J200.JO", "JSE", "South Africa Standard Time", "Africa/Johannesburg", "ZA", -26.2041, 28.0473),
        new("Nordic", "Stockholm", "OMX Nordic 40", "^OMXN40", "OMX", "W. Europe Standard Time", "Europe/Stockholm", "SE", 59.3293, 18.0686)
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
