namespace YFinance.NET.Protocol.Errors;

public sealed record ProtocolError(string Code, string Message, bool Retryable = false);
