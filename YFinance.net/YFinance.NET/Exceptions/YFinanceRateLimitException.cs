namespace YFinance.NET.Exceptions;

public sealed class YFinanceRateLimitException : YFinanceException
{
    public int? StatusCode { get; }

    public YFinanceRateLimitException(string message, int? statusCode = null) : base(message)
    {
        StatusCode = statusCode;
    }
}
