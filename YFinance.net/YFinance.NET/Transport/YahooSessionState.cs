namespace YFinance.NET.Transport;

public sealed record YahooSessionState(
    string Crumb,
    string CookieHeader,
    DateTimeOffset ExpiresUtc,
    string Strategy = "basic")
{
    public bool IsValid(DateTimeOffset utcNow)
        => !string.IsNullOrWhiteSpace(Crumb)
           && !string.IsNullOrWhiteSpace(CookieHeader)
           && utcNow < ExpiresUtc.Subtract(TimeSpan.FromMinutes(2));
}
