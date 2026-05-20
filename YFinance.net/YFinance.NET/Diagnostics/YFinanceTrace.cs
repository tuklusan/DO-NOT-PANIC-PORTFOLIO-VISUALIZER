namespace YFinance.NET.Diagnostics;

public sealed class YFinanceTrace
{
    private readonly IYFinanceTraceSink _sink;

    public YFinanceTrace(IYFinanceTraceSink? sink = null)
    {
        _sink = sink ?? NullYFinanceTraceSink.Instance;
    }

    public void InfoState(string source, string eventName, params (string Key, object? Value)[] fields)
        => _sink.InfoState(source, eventName, Map(fields));

    public void WarnState(string source, string eventName, params (string Key, object? Value)[] fields)
        => _sink.WarnState(source, eventName, Map(fields));

    public void ErrorState(string source, string eventName, Exception? exception = null, params (string Key, object? Value)[] fields)
        => _sink.ErrorState(source, eventName, Map(fields), exception);

    private static IEnumerable<KeyValuePair<string, object?>> Map(IEnumerable<(string Key, object? Value)> fields)
        => fields.Select(static field => new KeyValuePair<string, object?>(field.Key, field.Value));
}
