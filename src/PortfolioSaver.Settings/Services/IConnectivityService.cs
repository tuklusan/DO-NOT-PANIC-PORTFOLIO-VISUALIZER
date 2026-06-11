namespace PortfolioSaver.Config.Services;

public interface IConnectivityService
{
    bool IsInternetAvailable();
    Task<bool> IsInternetAvailableAsync(CancellationToken cancellationToken = default);
    void ForceProbe();
}
