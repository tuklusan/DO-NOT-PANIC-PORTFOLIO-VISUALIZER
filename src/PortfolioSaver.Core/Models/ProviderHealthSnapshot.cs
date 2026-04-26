namespace PortfolioSaver.Core.Models;

public sealed class ProviderHealthSnapshot
{
    public bool IsHealthy { get; set; } = true;
    public string StatusMessage { get; set; } = "OK";
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset? LastSuccessUtc { get; set; }
    public DateTimeOffset? LastFailureUtc { get; set; }
}
