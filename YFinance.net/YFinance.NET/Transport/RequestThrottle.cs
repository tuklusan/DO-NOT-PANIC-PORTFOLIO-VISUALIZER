using System.Diagnostics;

namespace YFinance.NET.Transport;

public sealed class RequestThrottle
{
    private readonly TimeSpan _minimumSpacing;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRequestUtc = DateTimeOffset.MinValue;

    public RequestThrottle(TimeSpan minimumSpacing)
    {
        _minimumSpacing = minimumSpacing;
    }

    public async Task WaitAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TimeSpan elapsed = DateTimeOffset.UtcNow - _lastRequestUtc;
            if (elapsed < _minimumSpacing)
            {
                await Task.Delay(_minimumSpacing - elapsed, cancellationToken).ConfigureAwait(false);
            }
            _lastRequestUtc = DateTimeOffset.UtcNow;
        }
        finally
        {
            _gate.Release();
        }
    }
}
