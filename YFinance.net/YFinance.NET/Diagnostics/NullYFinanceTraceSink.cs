namespace YFinance.NET.Diagnostics;

public sealed class NullYFinanceTraceSink : IYFinanceTraceSink
{
    public static NullYFinanceTraceSink Instance { get; } = new();

    private NullYFinanceTraceSink()
    {
    }

    public void InfoState(string source, string eventName, IEnumerable<KeyValuePair<string, object?>> fields)
    {
    }

    public void WarnState(string source, string eventName, IEnumerable<KeyValuePair<string, object?>> fields)
    {
    }

    public void ErrorState(string source, string eventName, IEnumerable<KeyValuePair<string, object?>> fields, Exception? exception = null)
    {
    }
}
