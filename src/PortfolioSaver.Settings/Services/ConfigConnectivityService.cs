using PortfolioSaver.Shared.Services;

namespace PortfolioSaver.Config.Services;

public sealed class ConfigConnectivityService
    : IConnectivityService
{
    private readonly InternetProbeService _probe = new();

    public bool IsInternetAvailable()
        => _probe.IsInternetAvailable();

    public Task<bool> IsInternetAvailableAsync(CancellationToken cancellationToken = default)
        => _probe.IsInternetAvailableAsync(cancellationToken);

    public void ForceProbe()
        => _probe.InvalidateCache();
}
