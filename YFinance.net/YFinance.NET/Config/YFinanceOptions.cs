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
using YFinance.NET.Caching;
using YFinance.NET.Diagnostics;
using YFinance.NET.Storage;

namespace YFinance.NET.Config;

public sealed class YFinanceOptions
{
    public Uri FinanceHomeUri { get; init; } = new("https://finance.yahoo.com/");
    public Uri CookieBootstrapUri { get; init; } = new("https://fc.yahoo.com");
    public Uri CrumbUri { get; init; } = new("https://query1.finance.yahoo.com/v1/test/getcrumb");
    public Uri Query1BaseUri { get; init; } = new("https://query1.finance.yahoo.com");
    public Uri Query2BaseUri { get; init; } = new("https://query2.finance.yahoo.com");
    public TimeSpan SessionTtl { get; init; } = TimeSpan.FromMinutes(45);
    public TimeSpan MinimumRequestSpacing { get; init; } = TimeSpan.FromSeconds(1);
    public int MaxRetries { get; init; } = 3;
    public TimeSpan DefaultCacheTtl { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan SummaryCacheTtl { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan PersistentMetadataCacheTtl { get; init; } = TimeSpan.FromMinutes(10);
    public int MaxSymbolsPerQuoteRequest { get; init; } = 25;
    public string Language { get; init; } = "en-US";
    public string Region { get; init; } = "US";
    /// <summary>
    /// Enables the trace-only GitHub freshness check that helps support diagnose whether YFinance.NET is behind upstream yfinance.
    /// The standalone server enables this by default for support traces; library consumers must opt in explicitly.
    /// </summary>
    public bool EnableUpstreamSyncCheck { get; init; } = false;
    public Uri UpstreamSyncCommitsApiUri { get; init; } = new("https://api.github.com/repos/ranaroussi/yfinance/commits?per_page=1");
    public TimeSpan UpstreamSyncCheckInterval { get; init; } = TimeSpan.FromHours(24);
    public TimeSpan UpstreamSyncCheckTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public string UserAgent { get; init; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0 Safari/537.36";
    public IYFinanceTraceSink TraceSink { get; init; } = YFinanceCircularTraceSink.Instance;
    public string PersistentCacheRootPath { get; init; } = ResolvePersistentCacheRootPath();

    public string MetadataCacheDirectoryPath => Path.Combine(PersistentCacheRootPath, CacheBuckets.Metadata);
    public string MarketTimingCacheDirectoryPath => Path.Combine(MetadataCacheDirectoryPath, "market-timing");

    /// <remarks>Whitespace language or region values intentionally omit the corresponding Yahoo query parameter.</remarks>
    internal void AddLocaleQueryParameters(IDictionary<string, string?> query)
    {
        if (!string.IsNullOrWhiteSpace(Language))
            query["lang"] = Language.Trim();

        if (!string.IsNullOrWhiteSpace(Region))
            query["region"] = Region.Trim();
    }

    private static string ResolvePersistentCacheRootPath()
    {
        string productRoot = AppDataRootResolver.ResolveInstalledLocalDataRoot();
        return Path.Combine(productRoot, "Caches", "YFinance");
    }
}
