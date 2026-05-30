using System.Net;
using System.Net.Http;
using PortfolioSaver.Data.Services;
using PortfolioSaver.Shared.Diagnostics;
using YFinance.NET.Protocol.Dtos;

namespace PortfolioSaver.Config.Services;

public sealed class YahooSymbolValidationService
{
    private const int MaxBatchSymbols = 25;
    private readonly Func<IReadOnlyCollection<string>, int, CancellationToken, Task<IReadOnlyDictionary<string, QuoteDto>>>? _quoteLookupAsync;

    public YahooSymbolValidationService(Func<int, object>? clientFactory = null)
    {
    }

    public YahooSymbolValidationService(
        Func<IReadOnlyCollection<string>, int, CancellationToken, Task<IReadOnlyDictionary<string, QuoteDto>>> quoteLookupAsync,
        Func<int, object>? clientFactory = null)
    {
        _quoteLookupAsync = quoteLookupAsync ?? throw new ArgumentNullException(nameof(quoteLookupAsync));
    }

    public async Task<YahooSymbolValidationResult> ValidateAsync(
        IEnumerable<string> symbols,
        int timeoutSeconds,
        IProgress<YahooSymbolValidationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        List<string> normalizedSymbols = symbols
            .Select(Normalize)
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        YahooSymbolValidationResult result = new(normalizedSymbols);
        if (normalizedSymbols.Count == 0)
            return result;
        IReadOnlyList<List<string>> batches = ChunkSymbols(normalizedSymbols, MaxBatchSymbols).ToList();
        for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
        {
            List<string> batch = batches[batchIndex];
            try
            {
                HashSet<string> resolvedBatchSymbols = new(StringComparer.OrdinalIgnoreCase);
                cancellationToken.ThrowIfCancellationRequested();
                Dictionary<string, string> requestByOriginal = batch.ToDictionary(
                    symbol => symbol,
                    YFinanceSymbolMapper.ToRequestSymbol,
                    StringComparer.OrdinalIgnoreCase);
                string operationId = YFinanceRuntimeClientFactory.CreateOperationId("config-validation");
                TraceLog.InfoState(
                    "YFinanceUiBridge",
                    "ValidationQuoteRequestStart",
                    [new("operation_id", operationId), new("batch_number", batchIndex + 1), new("batch_total", batches.Count), new("symbols", batch), new("request_symbols", requestByOriginal.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList())]);
                IReadOnlyDictionary<string, QuoteDto> quotes = await LookupQuotesAsync(
                        operationId,
                        requestByOriginal.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                        timeoutSeconds,
                        cancellationToken)
                    .ConfigureAwait(false);
                TraceLog.InfoState(
                    "YFinanceUiBridge",
                    "ValidationQuoteRequestComplete",
                    [new("operation_id", operationId), new("batch_number", batchIndex + 1), new("resolved_count", quotes.Count), new("response_keys", quotes.Keys.ToList())]);

                foreach ((string originalSymbol, string requestSymbol) in requestByOriginal)
                {
                    if (!quotes.TryGetValue(requestSymbol, out QuoteDto? quote) &&
                        !quotes.TryGetValue(YFinanceSymbolMapper.ToResponseMatchKey(requestSymbol), out quote))
                        continue;

                    bool hasLiveData = quote.RegularMarketPrice.HasValue ||
                                       quote.RegularMarketPreviousClose.HasValue ||
                                       quote.RegularMarketOpen.HasValue;
                    if (!hasLiveData)
                        continue;

                    string normalized = Normalize(originalSymbol);
                    resolvedBatchSymbols.Add(normalized);
                    string resolvedName = quote.DisplayName ?? quote.ShortName ?? quote.LongName ?? string.Empty;
                    result.RecordQuote(normalized, MapQuote(normalized, quote));
                    result.MarkValid(
                        normalized,
                        resolvedName,
                        quote.LongName);
                    progress?.Report(new YahooSymbolValidationProgress(normalized, true, resolvedName, "Validated via YFinance.NET"));
                }

                foreach (string requestedSymbol in batch)
                {
                    string normalized = Normalize(requestedSymbol);
                    if (resolvedBatchSymbols.Contains(normalized))
                        continue;

                    result.MarkInvalid(normalized, "YFinance.NET does not recognize this symbol.");
                    progress?.Report(new YahooSymbolValidationProgress(normalized, false, string.Empty, "Failed"));
                }
            }
            catch (Exception ex) when (IsTooManyRequests(ex))
            {
                result.MarkRateLimitedBatch(batch, ex.Message);
                TraceLog.WarnState(
                    "YFinanceUiBridge",
                    "ValidationQuoteRequestRateLimited",
                    [new("batch_number", batchIndex + 1), new("symbols", batch), new("message", ex.Message)]);
                foreach (string symbol in batch)
                    progress?.Report(new YahooSymbolValidationProgress(symbol, false, string.Empty, "Rate limited"));
            }
            catch (Exception ex)
            {
                result.MarkDeferredBatch(batch, ex.Message);
                TraceLog.WarnState(
                    "YFinanceUiBridge",
                    "ValidationQuoteRequestFailed",
                    [new("batch_number", batchIndex + 1), new("symbols", batch), new("message", ex.Message)]);
                foreach (string symbol in batch)
                    progress?.Report(new YahooSymbolValidationProgress(symbol, false, string.Empty, "Validation unavailable"));
            }
        }

        return result;
    }

    private static IEnumerable<List<string>> ChunkSymbols(IReadOnlyList<string> symbols, int size)
    {
        if (size <= 0)
            yield break;

        for (int index = 0; index < symbols.Count; index += size)
            yield return symbols.Skip(index).Take(Math.Min(size, symbols.Count - index)).ToList();
    }

    private static string Normalize(string? symbol)
        => (symbol ?? string.Empty).Trim().ToUpperInvariant();

    private static bool IsTooManyRequests(Exception ex)
        => ex is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } ||
           ex.Message.Contains("429", StringComparison.OrdinalIgnoreCase) ||
           ex.Message.Contains("too many requests", StringComparison.OrdinalIgnoreCase) ||
           ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase);

    private async Task<IReadOnlyDictionary<string, QuoteDto>> LookupQuotesAsync(
        string operationId,
        IReadOnlyCollection<string> requestSymbols,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (_quoteLookupAsync is not null)
            return await _quoteLookupAsync(requestSymbols, timeoutSeconds, cancellationToken).ConfigureAwait(false);

        QuotesResponseDto response = await YFinanceRuntimeClientFactory
            .RunSerializedAsync(
                "config-validation",
                operationId,
                (client, token) => client.GetQuotesAsync(requestSymbols.ToList(), token),
                cancellationToken)
            .ConfigureAwait(false);

        Dictionary<string, QuoteDto> results = new(StringComparer.OrdinalIgnoreCase);
        foreach (QuoteDto quote in response.Quotes)
        {
            if (!string.IsNullOrWhiteSpace(quote.Symbol))
                results[quote.Symbol] = quote;

            string responseKey = YFinanceSymbolMapper.ToResponseMatchKey(quote.Symbol);
            if (!string.IsNullOrWhiteSpace(responseKey))
                results[responseKey] = quote;
        }

        return results;
    }

    private static PortfolioSaver.Core.Models.QuoteSnapshot MapQuote(string originalSymbol, QuoteDto quote)
    {
        decimal? last = YFinanceSymbolMapper.NormalizeNumericValue(originalSymbol, quote.RegularMarketPrice);
        decimal? previousClose = YFinanceSymbolMapper.NormalizeNumericValue(originalSymbol, quote.RegularMarketPreviousClose);
        decimal? change = YFinanceSymbolMapper.NormalizeNumericValue(originalSymbol, quote.RegularMarketChange);
        decimal? changePercent = quote.RegularMarketChangePercent;
        if (changePercent is null && last is decimal current && previousClose is decimal prior && prior != 0m)
            changePercent = ((current - prior) / prior) * 100m;

        return new PortfolioSaver.Core.Models.QuoteSnapshot
        {
            Symbol = originalSymbol,
            Last = last,
            Change = change,
            ChangePercent = changePercent,
            PreviousClose = previousClose,
            Currency = quote.Currency ?? "USD",
            MarketSession = YFinanceSymbolMapper.MapMarketSession(quote.MarketState),
            ProviderTimestampUtc = null,
            FetchTimestampUtc = DateTimeOffset.UtcNow,
            IsStale = quote.Cache.Stale
        };
    }
}

public sealed record YahooSymbolValidationProgress(
    string Symbol,
    bool IsValid,
    string ResolvedName,
    string Message);

public sealed class YahooSymbolValidationResult
{
    private readonly Dictionary<string, YahooSymbolValidationEntry> _entries;
    private readonly HashSet<string> _rateLimitedSymbols = new(StringComparer.OrdinalIgnoreCase);

    public YahooSymbolValidationResult(IEnumerable<string> requestedSymbols)
    {
        _entries = requestedSymbols
            .Select(symbol => Normalize(symbol))
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                symbol => symbol,
                symbol => new YahooSymbolValidationEntry(symbol),
                StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, YahooSymbolValidationEntry> Entries => _entries;

    public IReadOnlyList<string> InvalidSymbols => _entries.Values
        .Where(entry => entry.WasChecked && !entry.IsValid)
        .Select(entry => entry.Symbol)
        .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public IReadOnlyList<string> DeferredSymbols => _entries.Values
        .Where(entry => !entry.WasChecked && !entry.IsValid)
        .Select(entry => entry.Symbol)
        .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public IReadOnlyList<string> RateLimitedSymbols => _rateLimitedSymbols
        .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
        .ToList();
    public IReadOnlyDictionary<string, PortfolioSaver.Core.Models.QuoteSnapshot> ValidatedQuotes => _validatedQuotes;

    public bool WasRateLimited => _rateLimitedSymbols.Count > 0;

    private readonly Dictionary<string, PortfolioSaver.Core.Models.QuoteSnapshot> _validatedQuotes = new(StringComparer.OrdinalIgnoreCase);

    public void MergeFrom(YahooSymbolValidationResult other)
    {
        foreach (YahooSymbolValidationEntry entry in other.Entries.Values)
        {
            if (entry.IsValid)
            {
                MarkValid(entry.Symbol, entry.DisplayName, entry.DisplayName);
                continue;
            }

            if (entry.WasChecked)
            {
                MarkInvalid(entry.Symbol, entry.FailureReason);
                continue;
            }

            MarkDeferred(entry.Symbol, entry.FailureReason);
        }

        foreach ((string symbol, PortfolioSaver.Core.Models.QuoteSnapshot quote) in other._validatedQuotes)
            _validatedQuotes[symbol] = quote;

        foreach (string symbol in other._rateLimitedSymbols)
            _rateLimitedSymbols.Add(symbol);
    }

    public void MarkValid(string symbol, string? shortName, string? longName)
    {
        string normalized = Normalize(symbol);
        if (!_entries.TryGetValue(normalized, out YahooSymbolValidationEntry? entry))
            return;

        entry.IsValid = true;
        entry.WasChecked = true;
        entry.FailureReason = string.Empty;
        entry.DisplayName = !string.IsNullOrWhiteSpace(shortName)
            ? shortName!.Trim()
            : (!string.IsNullOrWhiteSpace(longName) ? longName!.Trim() : entry.DisplayName);
    }

    public void RecordQuote(string symbol, PortfolioSaver.Core.Models.QuoteSnapshot quote)
    {
        string normalized = Normalize(symbol);
        _validatedQuotes[normalized] = quote;
    }

    public void MarkInvalid(string symbol, string reason)
    {
        string normalized = Normalize(symbol);
        if (!_entries.TryGetValue(normalized, out YahooSymbolValidationEntry? entry))
            return;

        entry.IsValid = false;
        entry.WasChecked = true;
        entry.FailureReason = reason;
    }

    public void MarkDeferredBatch(IEnumerable<string> symbols, string reason)
    {
        foreach (string symbol in symbols)
            MarkDeferred(symbol, reason);
    }

    public void MarkRateLimitedBatch(IEnumerable<string> symbols, string reason)
    {
        foreach (string symbol in symbols)
        {
            string normalized = Normalize(symbol);
            _rateLimitedSymbols.Add(normalized);
            MarkDeferred(normalized, string.IsNullOrWhiteSpace(reason)
                ? "YFinance.NET rate limited this validation request."
                : $"YFinance.NET rate limited this validation request: {reason}");
        }
    }

    private void MarkDeferred(string symbol, string reason)
    {
        string normalized = Normalize(symbol);
        if (!_entries.TryGetValue(normalized, out YahooSymbolValidationEntry? entry))
            return;

        entry.IsValid = false;
        entry.WasChecked = false;
        entry.FailureReason = reason;
    }

    private static string Normalize(string? symbol)
        => (symbol ?? string.Empty).Trim().ToUpperInvariant();
}

public sealed class YahooSymbolValidationEntry
{
    public YahooSymbolValidationEntry(string symbol)
    {
        Symbol = symbol;
        IsValid = false;
        WasChecked = false;
    }

    public string Symbol { get; }
    public bool IsValid { get; set; }
    public bool WasChecked { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;
}
