namespace YFinance.NET.Client;

public sealed class NullYFinanceServerClientTraceSink : IYFinanceServerClientTraceSink
{
    public static NullYFinanceServerClientTraceSink Instance { get; } = new();

    public void Info(string eventName, IReadOnlyList<KeyValuePair<string, object?>> fields)
    {
    }

    public void Warn(string eventName, IReadOnlyList<KeyValuePair<string, object?>> fields)
    {
    }

    public void Error(string eventName, IReadOnlyList<KeyValuePair<string, object?>> fields, Exception ex)
    {
    }
}
