using PortfolioSaver.Core.Enums;

namespace PortfolioSaver.Core.Models;

public sealed class AppSettings
{
    public int RefreshSecondsPortfolio { get; set; } = 1200;
    public int RefreshSecondsOffHours { get; set; } = 1200;
    public int HttpTimeoutSeconds { get; set; } = 10;
    public string NewsFeedUrl { get; set; } = "https://finance.yahoo.com/news/rss";
    public int NewsRefreshMinutes { get; set; } = 15;

    public string BackgroundImageFolder { get; set; } = string.Empty;
    public bool UseCustomBackgroundImageFolder { get; set; }
    public string CustomBackgroundImageFolder { get; set; } = string.Empty;
    public int BackgroundChangeSeconds { get; set; } = 60;
    public bool ShuffleBackgrounds { get; set; } = true;
    public double DimOpacity { get; set; } = 0.55;
    public LayoutPreset LayoutPreset { get; set; } = LayoutPreset.UltrawideDefault;

    // Handoff-only plaintext fields requested by the user for Codex transfer.
    // Production code should move these into DPAPI or Windows Credential Manager.
    public string FinnhubApiKey { get; set; } = string.Empty;
    public string TwelveDataApiKey { get; set; } = string.Empty;
    public string TiingoApiKey { get; set; } = string.Empty;
    public string FinancialModelingPrepApiKey { get; set; } = string.Empty;
    public string EodhdApiKey { get; set; } = string.Empty;
    public int MarketCalendarRefreshHours { get; set; } = 12;

    public int MinFinnhubRequestSpacingSeconds { get; set; } = 2;
    public int MinTwelveDataRequestSpacingSeconds { get; set; } = 15;

    public bool EnableFloatingGraphs { get; set; } = true;
    public int HistoricalLookbackDays { get; set; } = 14;
    public int HistoricalRefreshHours { get; set; } = 12;
    public int MaxFloatingGraphsPerTape { get; set; } = 4;
    public string HistoricalCacheRootFolder { get; set; } = string.Empty;

    public bool EnableBouncingGraphCards { get; set; } = true;
    public double FloatingGraphVelocityMin { get; set; } = 22;
    public double FloatingGraphVelocityMax { get; set; } = 48;
    public bool EnableFloatingClock { get; set; } = true;
    public int ClockRefreshSeconds { get; set; } = 1;

    public bool BackgroundIncludeSubfolders { get; set; }
    public List<DataSourcePolicySettings> DataSources { get; set; } = [];
    public List<TickerGroup> Groups { get; set; } = [];
}
