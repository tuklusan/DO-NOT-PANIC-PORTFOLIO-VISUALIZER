namespace YFinance.NET.Exceptions;

public class YFinanceException : Exception
{
    public YFinanceException(string message) : base(message) { }
    public YFinanceException(string message, Exception innerException) : base(message, innerException) { }
}
