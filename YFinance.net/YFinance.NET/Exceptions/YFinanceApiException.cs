namespace YFinance.NET.Exceptions;

public sealed class YFinanceApiException : YFinanceException
{
    public int? StatusCode { get; }

    public YFinanceApiException(string message, int? statusCode = null) : base(message)
    {
        StatusCode = statusCode;
    }
}
