using PortfolioSaver.Shared.Services;

namespace PortfolioSaver.Screensaver.Services;

public sealed class NetworkAvailabilityService
{
    private readonly InternetProbeService _probe = new();

    public bool IsNetworkAvailable()
        => _probe.IsInternetAvailable();

    public Task<bool> IsNetworkAvailableAsync(CancellationToken cancellationToken = default)
        => _probe.IsInternetAvailableAsync(cancellationToken);

    public void ForceProbe()
        => _probe.InvalidateCache();
}
