// ============================================================================
// Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
// Proprietary rights reserved except as expressly licensed herein.
//
// DO NOT PANIC PORTFOLIO VIEWER
// This software and its derivatives are licensed for STRICTLY NON-COMMERCIAL,
// personal, educational, or hobbyist use only. Commercial exploitation,
// corporate internal operations, or AI model training are strictly forbidden.
//
// ATTRIBUTION & DEPENDENCIES: This application incorporates the YFinance library,
// which is licensed under the Apache License, Version 2.0. A copy of the Apache
// License is provided within the distribution environment.
//
// FINANCIAL DISCLAIMER: This software is a passive visualization tool only.
// It does not provide financial, investment, legal, or tax advice. All data
// calculation and scraping outputs are provided 'AS IS' with zero guarantee
// of real-time accuracy or upstream availability.
//
// This file is subject to the terms and conditions defined in the LICENSE
// file located in the root directory of this source code repository.
// Removal or modification of this legal notice constitutes copyright infringement.
// ============================================================================
using System.Text.Json;
using YFinance.NET.Config;
using YFinance.NET.Diagnostics;
using YFinance.NET.Models;
using YFinance.NET.Transport;

namespace YFinance.NET.Features.Quotes;

public sealed class QuoteSummaryService
{
    private readonly YahooFinanceHttpClient _httpClient;
    private readonly YFinanceOptions _options;
    private readonly YFinanceTrace _trace;

    public QuoteSummaryService(YahooFinanceHttpClient httpClient, YFinanceOptions options, YFinanceTrace? trace = null)
    {
        _httpClient = httpClient;
        _options = options;
        _trace = trace ?? new YFinanceTrace(options.TraceSink);
    }

    public async Task<QuoteSummaryResult?> GetSummaryAsync(string symbol, IEnumerable<string> modules, CancellationToken cancellationToken = default)
    {
        string[] moduleList = modules.Select(static module => module.Trim())
                                     .Where(static module => !string.IsNullOrWhiteSpace(module))
                                     .Distinct(StringComparer.Ordinal)
                                     .ToArray();
        if (moduleList.Length == 0)
        {
            throw new ArgumentException("At least one quote summary module is required.", nameof(modules));
        }

        string normalizedSymbol = symbol.Trim().ToUpperInvariant();
        _trace.InfoState("YFinance.Summary", "SummaryRequestStart", ("symbol", normalizedSymbol), ("modules", moduleList), ("module_count", moduleList.Length));
        Dictionary<string, string?> query = new()
        {
            ["modules"] = string.Join(',', moduleList),
            ["corsDomain"] = "finance.yahoo.com",
            ["formatted"] = "false",
            ["symbol"] = normalizedSymbol
        };
        _options.AddLocaleQueryParameters(query);

        JsonDocument json = await _httpClient.GetCachedJsonAsync(
            $"/v10/finance/quoteSummary/{Uri.EscapeDataString(normalizedSymbol)}",
            query,
            _options.SummaryCacheTtl,
            cancellationToken).ConfigureAwait(false);

        JsonElement root = json.RootElement;
        if (!root.TryGetProperty("quoteSummary", out JsonElement quoteSummary) ||
            !quoteSummary.TryGetProperty("result", out JsonElement resultArray) ||
            resultArray.ValueKind != JsonValueKind.Array ||
            resultArray.GetArrayLength() == 0)
        {
            _trace.WarnState("YFinance.Summary", "SummaryRequestEmpty", ("symbol", normalizedSymbol), ("modules", moduleList));
            return null;
        }

        JsonElement first = resultArray[0];
        Dictionary<string, JsonElement> mapped = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in first.EnumerateObject())
        {
            mapped[property.Name] = property.Value.Clone();
        }

        _trace.InfoState("YFinance.Summary", "SummaryRequestComplete", ("symbol", normalizedSymbol), ("module_count", mapped.Count));
        return new QuoteSummaryResult(normalizedSymbol, mapped, JsonDocument.Parse(json.RootElement.GetRawText()));
    }
}
