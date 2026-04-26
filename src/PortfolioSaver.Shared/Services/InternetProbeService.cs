using System.Net.NetworkInformation;
using System.Threading;

namespace PortfolioSaver.Shared.Services;

public sealed class InternetProbeService
{
    private readonly string _host;
    private readonly int _attempts;
    private readonly int _timeoutMilliseconds;
    private readonly TimeSpan _cacheDuration;
    private readonly object _sync = new();

    private DateTimeOffset _lastProbeUtc = DateTimeOffset.MinValue;
    private bool _lastProbeResult;

    public InternetProbeService(
        string host = "baidu.com",
        int attempts = 5,
        int timeoutMilliseconds = 1000,
        TimeSpan? cacheDuration = null)
    {
        _host = string.IsNullOrWhiteSpace(host) ? "baidu.com" : host.Trim();
        _attempts = Math.Max(1, attempts);
        _timeoutMilliseconds = Math.Clamp(timeoutMilliseconds, 250, 5000);
        _cacheDuration = cacheDuration ?? TimeSpan.FromSeconds(30);
    }

    public bool IsInternetAvailable()
    {
        lock (_sync)
        {
            if (DateTimeOffset.UtcNow - _lastProbeUtc <= _cacheDuration)
                return _lastProbeResult;
        }

        bool available = ProbeInternet();
        lock (_sync)
        {
            _lastProbeResult = available;
            _lastProbeUtc = DateTimeOffset.UtcNow;
            return _lastProbeResult;
        }
    }

    public void InvalidateCache()
    {
        lock (_sync)
            _lastProbeUtc = DateTimeOffset.MinValue;
    }

    private bool ProbeInternet()
    {
        using Ping ping = new();
        for (int attempt = 0; attempt < _attempts; attempt++)
        {
            try
            {
                PingReply reply = ping.Send(_host, _timeoutMilliseconds);
                if (reply.Status == IPStatus.Success)
                    return true;
            }
            catch
            {
            }

            if (attempt < _attempts - 1)
                Thread.Sleep(1000);
        }

        return false;
    }
}
