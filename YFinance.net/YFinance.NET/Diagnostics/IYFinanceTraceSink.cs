namespace YFinance.NET.Diagnostics;

public interface IYFinanceTraceSink
{
    void InfoState(string source, string eventName, IEnumerable<KeyValuePair<string, object?>> fields);
    void WarnState(string source, string eventName, IEnumerable<KeyValuePair<string, object?>> fields);
    void ErrorState(string source, string eventName, IEnumerable<KeyValuePair<string, object?>> fields, Exception? exception = null);
}
