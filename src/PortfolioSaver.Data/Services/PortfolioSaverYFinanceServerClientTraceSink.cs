using PortfolioSaver.Shared.Diagnostics;
using YFinance.NET.Client;

namespace PortfolioSaver.Data.Services;

internal sealed class PortfolioSaverYFinanceServerClientTraceSink : IYFinanceServerClientTraceSink
{
    public static PortfolioSaverYFinanceServerClientTraceSink Instance { get; } = new();

    public void Info(string eventName, IReadOnlyList<KeyValuePair<string, object?>> fields)
        => TraceLog.InfoState("YFinanceClientProtocol", eventName, fields);

    public void Warn(string eventName, IReadOnlyList<KeyValuePair<string, object?>> fields)
        => TraceLog.WarnState("YFinanceClientProtocol", eventName, fields);

    public void Error(string eventName, IReadOnlyList<KeyValuePair<string, object?>> fields, Exception ex)
        => TraceLog.ErrorState("YFinanceClientProtocol", eventName, fields, ex);
}
