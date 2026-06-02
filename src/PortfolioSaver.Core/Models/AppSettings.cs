using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Enums;

namespace PortfolioSaver.Core.Models;

public sealed class AppSettings
{
    public int RefreshSecondsPortfolio { get; set; } = 300;
    public int RefreshSecondsOffHours { get; set; } = 300;
    public int HttpTimeoutSeconds { get; set; } = 10;
    public NewsScrollerMode NewsScrollerMode { get; set; } = NewsScrollerMode.SummarizedFinancialNews;
    public DeepSeekWritingStyle DeepSeekWritingStyle { get; set; } = DeepSeekWritingStyle.DouglasAdams;
    public string NewsFeedUrl { get; set; } = "https://finance.yahoo.com/news/rss";
    public int NewsRefreshMinutes { get; set; } = 15;

    public string BackgroundImageFolder { get; set; } = string.Empty;
    public bool UseCustomBackgroundImageFolder { get; set; }
    public string CustomBackgroundImageFolder { get; set; } = string.Empty;
    public int BackgroundChangeSeconds { get; set; } = 300;
    public bool ShuffleBackgrounds { get; set; } = true;
    public double DimOpacity { get; set; } = 0.55;
    public LayoutPreset LayoutPreset { get; set; } = LayoutPreset.UltrawideDefault;

    // DeepSeek remains user-configurable for summarized news and can still be overlaid from
    // protected local storage or environment variables.
    public string DeepSeekApiKey { get; set; } = string.Empty;
    public string DeepSeekEndpointUrl { get; set; } = Defaults.DefaultDeepSeekEndpointUrl;
    public string DeepSeekModelId { get; set; } = Defaults.DefaultDeepSeekModelId;
    public int MarketCalendarRefreshHours { get; set; } = 12;

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
    public List<TickerGroup> Groups { get; set; } = [];
}
