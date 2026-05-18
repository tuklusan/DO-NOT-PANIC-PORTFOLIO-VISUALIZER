using System.Net;
using System.Net.Http;
using System.Text;
using PortfolioSaver.Config.Services;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Models;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class ApiKeyValidationServiceTests
{
    [Fact]
    public async Task ValidateAsync_EmptyKeys_ReportRequiredErrors()
    {
        ApiKeyValidationService service = CreateService(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        ApiKeyValidationResult result = await service.ValidateAsync(Defaults.CreateSettings());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Finnhub API key is required.", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("Twelve Data API key is required.", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("Tiingo API key is required.", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("Financial Modeling Prep API key is required.", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("EODHD API key is required.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateAsync_PlaceholderKeys_ReportInstallerSampleFormatErrors()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.FinnhubApiKey = "abcdefghijklmnopqrstuvwxyz01234567890abc";
        settings.TwelveDataApiKey = "abcdefghijklmnopqrstuvwxyz012345";
        settings.TiingoApiKey = "abcdefghijklmnopqrstuvwxyz01234567890abc";
        settings.FinancialModelingPrepApiKey = "abcdefghijklmnopqrstuvwxyz012345";
        settings.EodhdApiKey = "abcdefghijklmn.01234567";

        ApiKeyValidationService service = CreateService(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        ApiKeyValidationResult result = await service.ValidateAsync(settings);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("installer sample format", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(5, result.Errors.Count);
    }

    [Fact]
    public async Task ValidateAsync_AcceptsTiingoWithoutLegacyResampleFreqProbe()
    {
        RecordingHandler handler = new(request =>
        {
            string uri = request.RequestUri!.ToString();
            if (uri.Contains("finnhub.io", StringComparison.OrdinalIgnoreCase))
                return Json("""{"c":123.45}""");
            if (uri.Contains("api.twelvedata.com", StringComparison.OrdinalIgnoreCase))
                return Json("""{"close":"123.45"}""");
            if (uri.Contains("api.tiingo.com", StringComparison.OrdinalIgnoreCase))
            {
                Assert.DoesNotContain("resampleFreq=1day", uri, StringComparison.OrdinalIgnoreCase);
                return Json("""[{"close":123.45}]""");
            }
            if (uri.Contains("financialmodelingprep.com", StringComparison.OrdinalIgnoreCase))
                return Json("""[{"exchange":"NYSE","isMarketOpen":true}]""");
            if (uri.Contains("eodhd.com", StringComparison.OrdinalIgnoreCase))
                return Json("""{"code":"AAPL.US","close":123.45}""");

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        ApiKeyValidationService service = CreateService(handler);
        ApiKeyValidationResult result = await service.ValidateAsync(CreateSettings());

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public async Task ValidateAsync_UsesStableFmpEndpoint()
    {
        RecordingHandler handler = new(request =>
        {
            string uri = request.RequestUri!.ToString();
            if (uri.Contains("api/v3/market-hours", StringComparison.OrdinalIgnoreCase))
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            if (uri.Contains("stable/all-exchange-market-hours", StringComparison.OrdinalIgnoreCase))
                return Json("""[{"exchange":"NYSE","isMarketOpen":false}]""");
            if (uri.Contains("finnhub.io", StringComparison.OrdinalIgnoreCase))
                return Json("""{"c":123.45}""");
            if (uri.Contains("api.twelvedata.com", StringComparison.OrdinalIgnoreCase))
                return Json("""{"close":"123.45"}""");
            if (uri.Contains("api.tiingo.com", StringComparison.OrdinalIgnoreCase))
                return Json("""[{"close":123.45}]""");
            if (uri.Contains("eodhd.com", StringComparison.OrdinalIgnoreCase))
                return Json("""{"code":"AAPL.US","close":123.45}""");

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        ApiKeyValidationService service = CreateService(handler);
        ApiKeyValidationResult result = await service.ValidateAsync(CreateSettings());

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        Assert.Contains(handler.Requests, uri => uri.Contains("stable/all-exchange-market-hours", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(handler.Requests, uri => uri.Contains("api/v3/market-hours", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_UsesEodhdRealtimeProbeInsteadOfExchangeDetails()
    {
        RecordingHandler handler = new(request =>
        {
            string uri = request.RequestUri!.ToString();
            if (uri.Contains("exchange-details", StringComparison.OrdinalIgnoreCase))
                return new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent("Only EOD data allowed for free users.", Encoding.UTF8, "text/plain")
                };
            if (uri.Contains("api/real-time/AAPL.US", StringComparison.OrdinalIgnoreCase))
                return Json("""{"code":"AAPL.US","close":123.45}""");
            if (uri.Contains("finnhub.io", StringComparison.OrdinalIgnoreCase))
                return Json("""{"c":123.45}""");
            if (uri.Contains("api.twelvedata.com", StringComparison.OrdinalIgnoreCase))
                return Json("""{"close":"123.45"}""");
            if (uri.Contains("api.tiingo.com", StringComparison.OrdinalIgnoreCase))
                return Json("""[{"close":123.45}]""");
            if (uri.Contains("financialmodelingprep.com", StringComparison.OrdinalIgnoreCase))
                return Json("""[{"exchange":"NYSE","isMarketOpen":true}]""");

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        ApiKeyValidationService service = CreateService(handler);
        ApiKeyValidationResult result = await service.ValidateAsync(CreateSettings());

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        Assert.Contains(handler.Requests, uri => uri.Contains("api/real-time/AAPL.US", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(handler.Requests, uri => uri.Contains("exchange-details", StringComparison.OrdinalIgnoreCase));
    }

    private static ApiKeyValidationService CreateService(HttpMessageHandler handler)
        => new(_ => new HttpClient(handler, disposeHandler: false));

    private static AppSettings CreateSettings()
    {
        AppSettings settings = Defaults.CreateSettings();
        settings.FinnhubApiKey = "finnhub-key";
        settings.TwelveDataApiKey = "twelve-key";
        settings.TiingoApiKey = "tiingo-key";
        settings.FinancialModelingPrepApiKey = "fmp-key";
        settings.EodhdApiKey = "eodhd-key";
        return settings;
    }

    private static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(_responder(request));
        }
    }
}
