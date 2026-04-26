using System.Net;
using System.Net.Http;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Providers;
using Xunit;

namespace PortfolioSaver.Tests.Providers;

public sealed class TreasuryYieldCurveQuoteProviderTests
{
    [Fact]
    public async Task GetQuotesAsync_ReturnsLatestAndPreviousTreasuryYields()
    {
        TreasuryYieldCurveHandler handler = new();
        using HttpClient httpClient = new(handler);
        TreasuryYieldCurveQuoteProvider provider = new(httpClient);

        IReadOnlyList<QuoteSnapshot> quotes = await provider.GetQuotesAsync(["US2M", "US10Y"]);

        Assert.Equal(2, quotes.Count);
        Assert.Equal(1, handler.RequestCount);

        QuoteSnapshot twoMonth = Assert.Single(quotes.Where(quote => quote.Symbol == "US2M"));
        Assert.Equal(4.62m, twoMonth.Last);
        Assert.Equal(4.55m, twoMonth.PreviousClose);
        Assert.Equal(0.07m, twoMonth.Change);

        QuoteSnapshot tenYear = Assert.Single(quotes.Where(quote => quote.Symbol == "US10Y"));
        Assert.Equal(4.21m, tenYear.Last);
        Assert.Equal(4.17m, tenYear.PreviousClose);
        Assert.Equal(0.04m, tenYear.Change);
    }

    [Fact]
    public async Task GetQuotesAsync_MapsTnxAliasToTreasuryTenYear()
    {
        TreasuryYieldCurveHandler handler = new();
        using HttpClient httpClient = new(handler);
        TreasuryYieldCurveQuoteProvider provider = new(httpClient);

        IReadOnlyList<QuoteSnapshot> quotes = await provider.GetQuotesAsync(["^TNX"]);

        QuoteSnapshot quote = Assert.Single(quotes);
        Assert.Equal("^TNX", quote.Symbol);
        Assert.Equal(4.21m, quote.Last);
    }

    private sealed class TreasuryYieldCurveHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            string xml =
                """
                <?xml version="1.0" encoding="utf-8" standalone="yes" ?>
                <feed xml:base="https://home.treasury.gov/resource-center/data-chart-center/interest-rates/pages/xml"
                      xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices"
                      xmlns:m="http://schemas.microsoft.com/ado/2007/08/dataservices/metadata"
                      xmlns="http://www.w3.org/2005/Atom">
                  <entry>
                    <content type="application/xml">
                      <m:properties>
                        <d:NEW_DATE m:type="Edm.DateTime">2026-04-18T00:00:00</d:NEW_DATE>
                        <d:BC_2MONTH m:type="Edm.Double">4.62</d:BC_2MONTH>
                        <d:BC_10YEAR m:type="Edm.Double">4.21</d:BC_10YEAR>
                      </m:properties>
                    </content>
                  </entry>
                  <entry>
                    <content type="application/xml">
                      <m:properties>
                        <d:NEW_DATE m:type="Edm.DateTime">2026-04-17T00:00:00</d:NEW_DATE>
                        <d:BC_2MONTH m:type="Edm.Double">4.55</d:BC_2MONTH>
                        <d:BC_10YEAR m:type="Edm.Double">4.17</d:BC_10YEAR>
                      </m:properties>
                    </content>
                  </entry>
                </feed>
                """;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(xml)
            });
        }
    }
}
