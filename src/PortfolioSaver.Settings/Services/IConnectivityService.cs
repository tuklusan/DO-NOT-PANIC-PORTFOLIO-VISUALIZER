namespace PortfolioSaver.Config.Services;

public interface IConnectivityService
{
    bool IsInternetAvailable();
    void ForceProbe();
}
