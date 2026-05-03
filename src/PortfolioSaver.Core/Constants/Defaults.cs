using System;
using System.IO;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;

namespace PortfolioSaver.Core.Constants;

public static class Defaults
{
    public const string DefaultNewsFeedUrl = "https://finance.yahoo.com/news/rss";
    public const int MinRefreshSeconds = 5;
    public const int MaxRefreshSeconds = 4 * 60 * 60;
    public const int DefaultDesktopRefreshSeconds = 300;
    public const int LegacySteadyStateRefreshSeconds = 1200;
    public const int MinimumSummarizedNewsRefreshMinutes = 15;
    public const int MinNewsRefreshMinutes = 5;
    public const int MaxNewsRefreshMinutes = 4 * 60;
    public const int MaxTapeCount = 4;
    public const int MaxTickersPerTape = 8;
    public const int MaxTapeNameLength = 16;
    public const double DefaultTapeBaseSpeed = 0.45d;
    public const double MinTapeSpeed = 0.20d;
    public const double MaxTapeSpeed = 0.65d;
    public const double DefaultNewsSpeed = 0.30d;

    public static string GetLegacyHistoricalCacheFolder()
        => Path.Combine(Environment.GetEnvironmentVariable("TEMP") ?? Path.GetTempPath(), "PortfolioSaver", "history-cache");

    public static string GetHistoricalCacheFolder()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PortfolioSaver",
            "Caches",
            "History");

    public static string GetManagedBackgroundCacheFolder()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PortfolioSaver", "Backgrounds", "ExchangePhotoCache");

    public static AppSettings CreateSettings() => new()
    {
        FinnhubApiKey = string.Empty,
        TwelveDataApiKey = string.Empty,
        TiingoApiKey = string.Empty,
        FinancialModelingPrepApiKey = string.Empty,
        EodhdApiKey = string.Empty,
        MarketCalendarRefreshHours = 12,
        RefreshSecondsPortfolio = DefaultDesktopRefreshSeconds,
        RefreshSecondsOffHours = DefaultDesktopRefreshSeconds,
        MinFinnhubRequestSpacingSeconds = 2,
        MinTwelveDataRequestSpacingSeconds = 15,
        NewsScrollerMode = NewsScrollerMode.SummarizedFinancialNews,
        NewsFeedUrl = DefaultNewsFeedUrl,
        NewsRefreshMinutes = 15,
        EnableFloatingGraphs = true,
        HistoricalLookbackDays = 14,
        HistoricalRefreshHours = 12,
        MaxFloatingGraphsPerTape = 4,
        HistoricalCacheRootFolder = GetHistoricalCacheFolder(),
        HttpTimeoutSeconds = 10,
        BackgroundImageFolder = GetManagedBackgroundCacheFolder(),
        UseCustomBackgroundImageFolder = false,
        CustomBackgroundImageFolder = string.Empty,
        BackgroundChangeSeconds = 7200,
        DimOpacity = 0.55,
        LayoutPreset = LayoutPreset.UltrawideDefault,
        DataSources = [.. DataSourceCatalog.CreateDefaultPolicies()],
        Groups =
        [
            new TickerGroup
            {
                Name = "GBLCORE+SAT",
                Direction = ScrollDirection.Left,
                Speed = GetDefaultTapeSpeed(0),
                RowHeight = 56.0,
                Tickers =
                [
                    new TickerItem { Symbol = "VOO", DisplayName = "VOO" },
                    new TickerItem { Symbol = "IJH", DisplayName = "IJH" },
                    new TickerItem { Symbol = "IJR", DisplayName = "IJR" },
                    new TickerItem { Symbol = "VEA", DisplayName = "VEA" },
                    new TickerItem { Symbol = "VWO", DisplayName = "VWO" },
                    new TickerItem { Symbol = "VSS", DisplayName = "VSS" },
                    new TickerItem { Symbol = "EFV", DisplayName = "EFV" },
                    new TickerItem { Symbol = "EFG", DisplayName = "EFG" }
                ]
            },
            new TickerGroup
            {
                Name = "MLTFCT+SMRTBETA",
                Direction = ScrollDirection.Right,
                Speed = GetDefaultTapeSpeed(1),
                RowHeight = 56.0,
                Tickers =
                [
                    new TickerItem { Symbol = "QUAL", DisplayName = "QUAL" },
                    new TickerItem { Symbol = "MTUM", DisplayName = "MTUM" },
                    new TickerItem { Symbol = "VLUE", DisplayName = "VLUE" },
                    new TickerItem { Symbol = "SIZE", DisplayName = "SIZE" },
                    new TickerItem { Symbol = "USMV", DisplayName = "USMV" },
                    new TickerItem { Symbol = "INTF", DisplayName = "INTF" },
                    new TickerItem { Symbol = "EMGF", DisplayName = "EMGF" },
                    new TickerItem { Symbol = "SYLD", DisplayName = "SYLD" }
                ]
            },
            new TickerGroup
            {
                Name = "DIVGRTH+YLD",
                Direction = ScrollDirection.Left,
                Speed = GetDefaultTapeSpeed(2),
                RowHeight = 56.0,
                Tickers =
                [
                    new TickerItem { Symbol = "SCHD", DisplayName = "SCHD" },
                    new TickerItem { Symbol = "VIG", DisplayName = "VIG" },
                    new TickerItem { Symbol = "VYM", DisplayName = "VYM" },
                    new TickerItem { Symbol = "DGRO", DisplayName = "DGRO" },
                    new TickerItem { Symbol = "VYMI", DisplayName = "VYMI" },
                    new TickerItem { Symbol = "VIGI", DisplayName = "VIGI" },
                    new TickerItem { Symbol = "SPHD", DisplayName = "SPHD" },
                    new TickerItem { Symbol = "DGRW", DisplayName = "DGRW" }
                ]
            },
            new TickerGroup
            {
                Name = "STRTGC-ROTN",
                Direction = ScrollDirection.Right,
                Speed = GetDefaultTapeSpeed(3),
                RowHeight = 56.0,
                Tickers =
                [
                    new TickerItem { Symbol = "XLK", DisplayName = "XLK" },
                    new TickerItem { Symbol = "XLV", DisplayName = "XLV" },
                    new TickerItem { Symbol = "XLF", DisplayName = "XLF" },
                    new TickerItem { Symbol = "XLY", DisplayName = "XLY" },
                    new TickerItem { Symbol = "XLE", DisplayName = "XLE" },
                    new TickerItem { Symbol = "SMH", DisplayName = "SMH" },
                    new TickerItem { Symbol = "CIBR", DisplayName = "CIBR" },
                    new TickerItem { Symbol = "ARKK", DisplayName = "ARKK" }
                ]
            }
        ]
    };

    public static string GetDefaultTapeName(int index)
        => $"Tape {Math.Clamp(index, 1, MaxTapeCount)}";

    public static double GetDefaultTapeSpeed(int displayIndex)
        => displayIndex switch
        {
            0 => DefaultTapeBaseSpeed,
            1 => Math.Round(DefaultTapeBaseSpeed * 0.90d, 4),
            2 => Math.Round(DefaultTapeBaseSpeed * 1.10d, 4),
            3 => Math.Round(DefaultTapeBaseSpeed * 0.85d, 4),
            _ => DefaultTapeBaseSpeed
        };

    public static TickerGroup CreateEmptyTickerGroup(int displayIndex)
        => new()
        {
            Name = GetDefaultTapeName(displayIndex + 1),
            Direction = displayIndex % 2 == 0 ? ScrollDirection.Left : ScrollDirection.Right,
            Speed = GetDefaultTapeSpeed(displayIndex),
            RowHeight = 56.0,
            Enabled = true
        };
}


