using PortfolioSaver.Core.Models;

namespace PortfolioSaver.Data.Services;

public sealed class ProviderHealthService
{
    private readonly ProviderHealthSnapshot _snapshot = new();

    public ProviderHealthSnapshot Snapshot => _snapshot;

    public void MarkSuccess()
    {
        _snapshot.IsHealthy = true;
        _snapshot.StatusMessage = "OK";
        _snapshot.ConsecutiveFailures = 0;
        _snapshot.LastSuccessUtc = DateTimeOffset.UtcNow;
    }

    public void MarkFailure(string message)
    {
        _snapshot.IsHealthy = false;
        _snapshot.StatusMessage = message;
        _snapshot.ConsecutiveFailures++;
        _snapshot.LastFailureUtc = DateTimeOffset.UtcNow;
    }
}
