namespace PortfolioSaver.Shared.Infrastructure;

public sealed class Result
{
    public bool IsSuccess { get; }
    public string ErrorMessage { get; }

    private Result(bool isSuccess, string errorMessage)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
    }

    public static Result Success() => new(true, string.Empty);
    public static Result Failure(string message) => new(false, message);
}
