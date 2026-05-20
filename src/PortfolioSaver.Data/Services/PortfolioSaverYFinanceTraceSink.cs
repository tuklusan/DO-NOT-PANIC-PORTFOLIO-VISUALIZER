using PortfolioSaver.Shared.Diagnostics;
using YFinance.NET.Diagnostics;

namespace PortfolioSaver.Data.Services;

public sealed class PortfolioSaverYFinanceTraceSink : IYFinanceTraceSink
{
    public static PortfolioSaverYFinanceTraceSink Instance { get; } = new();

    private PortfolioSaverYFinanceTraceSink()
    {
    }

    public void InfoState(string source, string eventName, IEnumerable<KeyValuePair<string, object?>> fields)
        => TraceLog.InfoState(source, eventName, fields);

    public void WarnState(string source, string eventName, IEnumerable<KeyValuePair<string, object?>> fields)
        => TraceLog.WarnState(source, eventName, fields);

    public void ErrorState(string source, string eventName, IEnumerable<KeyValuePair<string, object?>> fields, Exception? exception = null)
        => TraceLog.ErrorState(source, eventName, fields, exception);
}
