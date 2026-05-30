namespace YFinance.NET.Client;

public interface IYFinanceServerClientTraceSink
{
    void Info(string eventName, IReadOnlyList<KeyValuePair<string, object?>> fields);
    void Warn(string eventName, IReadOnlyList<KeyValuePair<string, object?>> fields);
    void Error(string eventName, IReadOnlyList<KeyValuePair<string, object?>> fields, Exception ex);
}
