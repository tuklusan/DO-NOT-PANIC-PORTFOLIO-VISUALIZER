using System.Text.Json;

namespace YFinance.NET.Models;

public sealed record QuoteSummaryResult(
    string Symbol,
    IReadOnlyDictionary<string, JsonElement> Modules,
    JsonDocument RawDocument);
