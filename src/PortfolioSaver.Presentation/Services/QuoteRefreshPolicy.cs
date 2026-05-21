using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Services;

namespace PortfolioSaver.Screensaver.Services;

internal static class QuoteRefreshPolicy
{
    internal const double RecoveryRefreshSeconds = 1.0d;
    private static readonly TimeSpan UiSequentialCadence = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan MinimumHardStaleThreshold = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan HardStaleGrace = TimeSpan.FromMinutes(2);

    public static TimeSpan GetConfiguredRefreshWindow(AppSettings settings, DateTimeOffset nowUtc)
        => UiSequentialCadence;

    public static TimeSpan GetEffectiveRefreshWindow(AppSettings settings, DateTimeOffset nowUtc)
        => UiSequentialCadence;

    public static TimeSpan GetRefreshPollingInterval(AppSettings settings, DateTimeOffset nowUtc)
        => UiSequentialCadence;

    public static TimeSpan GetHardStaleThreshold(AppSettings settings, DateTimeOffset nowUtc)
    {
        TimeSpan effectiveRefreshWindow = GetEffectiveRefreshWindow(settings, nowUtc);
        TimeSpan withGrace = effectiveRefreshWindow + HardStaleGrace;
        return withGrace > MinimumHardStaleThreshold ? withGrace : MinimumHardStaleThreshold;
    }
}
