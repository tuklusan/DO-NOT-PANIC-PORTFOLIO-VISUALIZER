namespace YFinance.NET.Config;

public sealed class YFinanceOptions
{
    public Uri FinanceHomeUri { get; init; } = new("https://finance.yahoo.com/");
    public Uri CookieBootstrapUri { get; init; } = new("https://fc.yahoo.com");
    public Uri CrumbUri { get; init; } = new("https://query1.finance.yahoo.com/v1/test/getcrumb");
    public Uri Query1BaseUri { get; init; } = new("https://query1.finance.yahoo.com");
    public Uri Query2BaseUri { get; init; } = new("https://query2.finance.yahoo.com");
    public TimeSpan SessionTtl { get; init; } = TimeSpan.FromMinutes(45);
    public TimeSpan MinimumRequestSpacing { get; init; } = TimeSpan.FromMilliseconds(500);
    public int MaxRetries { get; init; } = 3;
    public TimeSpan DefaultCacheTtl { get; init; } = TimeSpan.FromMinutes(30);
    public string UserAgent { get; init; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0 Safari/537.36";
}
