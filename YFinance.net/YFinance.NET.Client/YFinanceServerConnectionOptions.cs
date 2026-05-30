namespace YFinance.NET.Client;

public sealed record YFinanceServerConnectionOptions(
    string Host,
    int Port,
    TimeSpan ConnectTimeout,
    IYFinanceServerClientTraceSink TraceSink)
{
    public static YFinanceServerConnectionOptions Default { get; } = new("127.0.0.1", Protocol.Constants.ProtocolConstants.DefaultPort, TimeSpan.FromSeconds(5), NullYFinanceServerClientTraceSink.Instance);
}
