// ============================================================================
// Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
// Proprietary rights reserved except as expressly licensed herein.
//
// DO NOT PANIC PORTFOLIO VISUALIZER
// This file is governed by the SANYALnet Labs Non-Commercial License in the
// root LICENSE file. Non-Commercial use is permitted; Commercial Use and use
// for AI/ML model training are prohibited unless separately authorized.
//
// Attribution is required: "Based on original work by Supratim Sanyal of
// SANYALnet Labs." See LICENSE for full terms, warranty disclaimer, termination,
// patent, trademark, and governing-law provisions.
// ============================================================================
using System.Globalization;
using System.Xml.Linq;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Interfaces;

namespace PortfolioSaver.Data.Providers;

public sealed class TreasuryYieldCurveQuoteProvider : IQuoteProvider
{
    private const string TreasuryFeedBaseUrl = "https://home.treasury.gov/resource-center/data-chart-center/interest-rates/pages/xml?data=daily_treasury_yield_curve&field_tdr_date_value=";
    private static readonly XNamespace AtomNamespace = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace DataNamespace = "http://schemas.microsoft.com/ado/2007/08/dataservices";
    private static readonly XNamespace MetadataNamespace = "http://schemas.microsoft.com/ado/2007/08/dataservices/metadata";
    private static readonly IReadOnlyDictionary<string, string> FieldMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["US2M"] = "BC_2MONTH",
        ["US10Y"] = "BC_10YEAR",
        ["^TNX"] = "BC_10YEAR"
    };
    private static readonly SocketsHttpHandler SharedHttpHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30)
    };

    internal static SocketsHttpHandler SharedHttpHandlerForTests => SharedHttpHandler;

    private readonly HttpClient _httpClient;

    public TreasuryYieldCurveQuoteProvider(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient(SharedHttpHandler, disposeHandler: false);
    }

    public async Task<IReadOnlyList<QuoteSnapshot>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
    {
        List<string> requested = symbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (requested.Count == 0)
            return [];

        int currentYear = DateTime.UtcNow.Year;
        List<TreasuryYieldRow> rows = await LoadRowsAsync(currentYear, cancellationToken);
        if (rows.Count < 2)
        {
            List<TreasuryYieldRow> priorRows = await LoadRowsAsync(currentYear - 1, cancellationToken);
            rows.AddRange(priorRows);
        }

        List<TreasuryYieldRow> ordered = rows
            .OrderByDescending(row => row.DateUtc)
            .ToList();

        List<QuoteSnapshot> results = [];
        foreach (string symbol in requested)
        {
            if (!FieldMap.TryGetValue(symbol, out string? fieldName))
                continue;

            TreasuryYieldRow? latest = ordered.FirstOrDefault(row => row.Values.ContainsKey(fieldName));
            if (latest is null)
                continue;

            TreasuryYieldRow? previous = ordered
                .SkipWhile(row => row.DateUtc == latest.DateUtc)
                .FirstOrDefault(row => row.Values.ContainsKey(fieldName));

            decimal last = latest.Values[fieldName];
            decimal? previousClose = previous is not null ? previous.Values[fieldName] : null;
            decimal? change = previousClose is decimal prior ? Math.Round(last - prior, 4) : null;
            decimal? changePercent = previousClose is decimal prev && prev != 0m
                ? Math.Round(((last - prev) / prev) * 100m, 4)
                : null;

            results.Add(new QuoteSnapshot
            {
                Symbol = symbol,
                Last = last,
                PreviousClose = previousClose,
                Change = change,
                ChangePercent = changePercent,
                Currency = "USD",
                ProviderTimestampUtc = latest.DateUtc,
                FetchTimestampUtc = DateTimeOffset.UtcNow,
                IsStale = false
            });
        }

        return results;
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<QuoteSnapshot> quotes = await GetQuotesAsync(["US10Y"], cancellationToken);
        return quotes.Count > 0;
    }

    private async Task<List<TreasuryYieldRow>> LoadRowsAsync(int year, CancellationToken cancellationToken)
    {
        string url = TreasuryFeedBaseUrl + year.ToString(CultureInfo.InvariantCulture);
        using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        XDocument document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);

        List<TreasuryYieldRow> rows = [];
        foreach (XElement entry in document.Root?.Elements(AtomNamespace + "entry") ?? [])
        {
            XElement? properties = entry
                .Element(AtomNamespace + "content")
                ?.Element(MetadataNamespace + "properties");
            if (properties is null)
                continue;

            XElement? dateElement = properties.Element(DataNamespace + "NEW_DATE");
            if (dateElement is null ||
                !DateTimeOffset.TryParse(dateElement.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset dateUtc))
            {
                continue;
            }

            Dictionary<string, decimal> values = new(StringComparer.OrdinalIgnoreCase);
            foreach (string field in FieldMap.Values.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                XElement? fieldElement = properties.Element(DataNamespace + field);
                if (fieldElement is null)
                    continue;

                if (decimal.TryParse(fieldElement.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsed))
                    values[field] = parsed;
            }

            if (values.Count > 0)
                rows.Add(new TreasuryYieldRow(dateUtc, values));
        }

        return rows;
    }

    private sealed record TreasuryYieldRow(DateTimeOffset DateUtc, IReadOnlyDictionary<string, decimal> Values);
}
