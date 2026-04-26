namespace PortfolioSaver.Data.Options;

public sealed class DataProviderOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 10;
}
