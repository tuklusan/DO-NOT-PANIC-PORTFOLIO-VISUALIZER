namespace YFinance.NET.Protocol.Constants;

public static class ProtocolConstants
{
    public const int Version = 1;
    public const int DefaultPort = 14870;
    public const int MaxConcurrentClients = 1024;
    public const int LengthPrefixBytes = 4;
    public const int MaxMessageBytes = 4 * 1024 * 1024;

    public static string GetMutexName(int port)
        => $"Global\\PortfolioSaver.YFinance.NET.Server.{port}";
}
