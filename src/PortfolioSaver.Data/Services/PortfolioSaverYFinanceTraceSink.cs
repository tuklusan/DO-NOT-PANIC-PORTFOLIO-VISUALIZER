using YFinance.NET.Diagnostics;

namespace PortfolioSaver.Data.Services;

public sealed class PortfolioSaverYFinanceTraceSink : IYFinanceTraceSink
{
    public static PortfolioSaverYFinanceTraceSink Instance { get; } = new();
    private static readonly IYFinanceTraceSink Sink = YFinanceCircularTraceSink.Instance;

    private PortfolioSaverYFinanceTraceSink()
    {
    }

    public void InfoState(string source, string eventName, IEnumerable<KeyValuePair<string, object?>> fields)
        => Sink.InfoState(source, eventName, fields);

    public void WarnState(string source, string eventName, IEnumerable<KeyValuePair<string, object?>> fields)
        => Sink.WarnState(source, eventName, fields);

    public void ErrorState(string source, string eventName, IEnumerable<KeyValuePair<string, object?>> fields, Exception? exception = null)
        => Sink.ErrorState(source, eventName, fields, exception);
}
