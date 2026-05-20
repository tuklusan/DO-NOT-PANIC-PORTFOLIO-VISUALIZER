namespace YFinance.NET.Models;

public sealed record TradingPeriodWindow(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string? Timezone,
    long? GmtOffsetSeconds);
