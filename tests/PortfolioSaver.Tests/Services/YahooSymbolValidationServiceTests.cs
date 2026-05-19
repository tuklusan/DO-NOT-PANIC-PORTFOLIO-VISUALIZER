using System.Net;
using System.Net.Http;
using System.Text;
using PortfolioSaver.Config.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class YahooSymbolValidationServiceTests
{
    [Fact]
    public async Task ValidateAsync_WhenYahooRateLimits_MarksSymbolsDeferredInsteadOfInvalid()
    {
        QueueHttpHandler handler = new(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html></html>", Encoding.UTF8, "text/html"),
                Headers = { { "Set-Cookie", "A=1; Path=/" } }
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("crumb-123")
            },
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("Too Many Requests")
            });

        YahooSymbolValidationService service = new(timeout => new HttpClient(handler));

        YahooSymbolValidationResult result = await service.ValidateAsync(["AAPL", "MSFT"], 5);

        Assert.True(result.WasRateLimited);
        Assert.Empty(result.InvalidSymbols);
        Assert.Equal(["AAPL", "MSFT"], result.DeferredSymbols);
    }

    private sealed class QueueHttpHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
                throw new InvalidOperationException($"No queued response left for {request.RequestUri}.");

            return Task.FromResult(_responses.Dequeue());
        }
    }
}
