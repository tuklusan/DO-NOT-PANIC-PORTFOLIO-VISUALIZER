namespace YFinance.NET.Config;

public static class YFinanceUpstreamSyncMetadata
{
    // Keep these constants synchronized with YFinance.net/upstream-sync.json whenever an upstream review baseline changes.
    public const string UpstreamRepository = "https://github.com/ranaroussi/yfinance";
    public const string ForkRepository = "https://github.com/tuklusan/yfinance";
    public const string ReviewedCommit = "125b12e058fe37971390e32333d2cf9edb2a8a50";
    public const string ReviewedCommitDate = "2026-05-28T21:01:28+01:00";
    public const string ReviewedVersion = "1.4.1";
    public const string ReviewedByCr = "CR-062";
}
