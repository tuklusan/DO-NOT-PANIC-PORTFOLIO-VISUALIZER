using System.Net;
using System.Net.Http;
using PortfolioSaver.Data.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

[Collection("YahooSessionSerial")]
public sealed class YahooFinanceSessionServiceTests
{
    [Fact]
    public async Task GetAsync_InvalidCrumbResponse_RefreshesSessionAndRetries()
    {
        SequenceHandler handler = new();
        using HttpClient httpClient = new(handler);
        YahooFinanceSessionService service = new(httpClient);
        service.Invalidate();

        using HttpResponseMessage response = await service.GetAsync("https://query1.finance.yahoo.com/v8/finance/spark?symbols=AAPL");
        string payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"ok\":true}", payload);

        Assert.Equal(2, handler.DataRequestUris.Count);
        Assert.Contains("crumb=crumb1", handler.DataRequestUris[0], StringComparison.Ordinal);
        Assert.Contains("crumb=crumb2", handler.DataRequestUris[1], StringComparison.Ordinal);
        Assert.Contains("A=9", handler.DataCookieHeaders[1], StringComparison.Ordinal);
        Assert.Contains("B=8", handler.DataCookieHeaders[1], StringComparison.Ordinal);
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private int _step;
        public List<string> DataRequestUris { get; } = [];
        public List<string> DataCookieHeaders { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri?.ToString() ?? string.Empty;

            if (url.StartsWith("https://finance.yahoo.com/", StringComparison.OrdinalIgnoreCase))
            {
                _step++;
                HttpResponseMessage bootstrap = new(HttpStatusCode.OK);
                bootstrap.Headers.TryAddWithoutValidation("Set-Cookie", _step < 4 ? "A=1; Path=/" : "A=9; Path=/");
                bootstrap.Content = new StringContent("home");
                return Task.FromResult(bootstrap);
            }

            if (url.StartsWith("https://query1.finance.yahoo.com/v1/test/getcrumb", StringComparison.OrdinalIgnoreCase))
            {
                _step++;
                bool refreshed = _step >= 5;
                HttpResponseMessage crumb = new(HttpStatusCode.OK);
                crumb.Headers.TryAddWithoutValidation("Set-Cookie", refreshed ? "B=8; Path=/" : "B=2; Path=/");
                crumb.Content = new StringContent(refreshed ? "crumb2" : "crumb1");
                return Task.FromResult(crumb);
            }

            if (url.StartsWith("https://query1.finance.yahoo.com/v8/finance/spark", StringComparison.OrdinalIgnoreCase))
            {
                _step++;
                DataRequestUris.Add(url);
                string cookie = request.Headers.TryGetValues("Cookie", out IEnumerable<string>? cookieValues)
                    ? string.Join("; ", cookieValues)
                    : string.Empty;
                DataCookieHeaders.Add(cookie);

                if (DataRequestUris.Count == 1)
                {
                    HttpResponseMessage invalid = new(HttpStatusCode.Forbidden)
                    {
                        Content = new StringContent("invalid crumb")
                    };
                    return Task.FromResult(invalid);
                }

                HttpResponseMessage ok = new(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ok\":true}")
                };
                return Task.FromResult(ok);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}

[CollectionDefinition("YahooSessionSerial", DisableParallelization = true)]
public sealed class YahooSessionSerialCollection;
