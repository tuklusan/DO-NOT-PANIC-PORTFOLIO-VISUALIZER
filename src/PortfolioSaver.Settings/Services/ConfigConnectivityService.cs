using PortfolioSaver.Shared.Services;

namespace PortfolioSaver.Config.Services;

public sealed class ConfigConnectivityService
{
    private readonly InternetProbeService _probe = new();

    public bool IsInternetAvailable()
        => _probe.IsInternetAvailable();

    public void ForceProbe()
        => _probe.InvalidateCache();
}
