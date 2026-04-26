using System.Text.Json.Serialization;

namespace PortfolioSaver.Data.DTOs;

public sealed class TwelveDataQuoteResponse
{
    [JsonPropertyName("symbol")] public string Symbol { get; set; } = string.Empty;
    [JsonPropertyName("close")] public string? Close { get; set; }
    [JsonPropertyName("previous_close")] public string? PreviousClose { get; set; }
    [JsonPropertyName("percent_change")] public string? PercentChange { get; set; }
}
