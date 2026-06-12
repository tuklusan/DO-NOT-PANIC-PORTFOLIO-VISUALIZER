namespace PortfolioSaver.Data.Services;

public sealed class RateLimitGuard : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRunUtc = DateTimeOffset.MinValue;

    // Test hook for cancellation regression coverage; production code should not make decisions from semaphore internals.
    internal int CurrentCountForTests => _gate.CurrentCount;

    /// <summary>
    /// Serializes callers so a shared guard enforces one completed lookup interval at a time.
    /// </summary>
    /// <remarks>
    /// This guard is intentionally strict and is not reentrant; recursive calls on the same instance will deadlock.
    /// </remarks>
    public async Task WaitIfNeededAsync(TimeSpan minimumInterval, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TimeSpan elapsed = DateTimeOffset.UtcNow - _lastRunUtc;
            if (elapsed < minimumInterval)
                await Task.Delay(minimumInterval - elapsed, cancellationToken).ConfigureAwait(false);

            _lastRunUtc = DateTimeOffset.UtcNow;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }
}
