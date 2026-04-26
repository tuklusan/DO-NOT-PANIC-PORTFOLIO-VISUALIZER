namespace PortfolioSaver.Data.Services;

public static class HttpClientFactory
{
    public static HttpClient Create(TimeSpan timeout)
    {
        HttpClient client = new();
        client.Timeout = timeout;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PortfolioScreensaver/1.0");
        return client;
    }
}
