namespace PortfolioSaver.Data.Services;

public sealed class RateLimitGuard
{
    private DateTimeOffset _lastRunUtc = DateTimeOffset.MinValue;

    public async Task WaitIfNeededAsync(TimeSpan minimumInterval, CancellationToken cancellationToken = default)
    {
        TimeSpan elapsed = DateTimeOffset.UtcNow - _lastRunUtc;
        if (elapsed < minimumInterval)
            await Task.Delay(minimumInterval - elapsed, cancellationToken);

        _lastRunUtc = DateTimeOffset.UtcNow;
    }
}
