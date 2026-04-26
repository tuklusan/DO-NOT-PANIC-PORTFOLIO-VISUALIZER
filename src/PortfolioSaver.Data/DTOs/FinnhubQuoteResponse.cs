using System.Text.Json.Serialization;

namespace PortfolioSaver.Data.DTOs;

public sealed class FinnhubQuoteResponse
{
    [JsonPropertyName("c")] public decimal? Current { get; set; }
    [JsonPropertyName("d")] public decimal? Change { get; set; }
    [JsonPropertyName("dp")] public decimal? ChangePercent { get; set; }
    [JsonPropertyName("pc")] public decimal? PreviousClose { get; set; }
    [JsonPropertyName("t")] public long? UnixTime { get; set; }
}
