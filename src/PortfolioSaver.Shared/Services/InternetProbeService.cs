using System.Net.Http;
using System.Threading;

namespace PortfolioSaver.Shared.Services;

public sealed class InternetProbeService
{
    private static readonly string[] DefaultProbeUrls =
    [
        "https://www.msftconnecttest.com/connecttest.txt",
        "https://www.gstatic.com/generate_204"
    ];

    private readonly string[] _probeUrls;
    private readonly int _attempts;
    private readonly int _timeoutMilliseconds;
    private readonly TimeSpan _cacheDuration;
    private readonly object _sync = new();

    private DateTimeOffset _lastProbeUtc = DateTimeOffset.MinValue;
    private bool _lastProbeResult;

    public InternetProbeService(
        IEnumerable<string>? probeUrls = null,
        int attempts = 2,
        int timeoutMilliseconds = 1500,
        TimeSpan? cacheDuration = null)
    {
        _probeUrls = NormalizeProbeUrls(probeUrls);
        _attempts = Math.Max(1, attempts);
        _timeoutMilliseconds = Math.Clamp(timeoutMilliseconds, 250, 5000);
        _cacheDuration = cacheDuration ?? TimeSpan.FromSeconds(10);
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
        using HttpClient client = new()
        {
            Timeout = TimeSpan.FromMilliseconds(_timeoutMilliseconds)
        };

        for (int attempt = 0; attempt < _attempts; attempt++)
        {
            foreach (string probeUrl in _probeUrls)
            {
                if (TryProbeUrl(client, probeUrl))
                    return true;
            }

            if (attempt < _attempts - 1)
                Thread.Sleep(250);
        }

        return false;
    }

    private static bool TryProbeUrl(HttpClient client, string probeUrl)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, probeUrl);
            using HttpResponseMessage response = client.Send(request, HttpCompletionOption.ResponseHeadersRead);
            int statusCode = (int)response.StatusCode;
            return statusCode >= 200 && statusCode < 500;
        }
        catch
        {
            return false;
        }
    }

    private static string[] NormalizeProbeUrls(IEnumerable<string>? probeUrls)
    {
        string[] normalized = (probeUrls ?? DefaultProbeUrls)
            .Select(url => (url ?? string.Empty).Trim())
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Contains("://", StringComparison.Ordinal) ? url : $"https://{url}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized.Length > 0 ? normalized : DefaultProbeUrls;
    }
}
