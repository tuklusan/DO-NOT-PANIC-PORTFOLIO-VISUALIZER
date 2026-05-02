using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Services;

namespace PortfolioSaver.Screensaver.Services;

internal static class QuoteRefreshPolicy
{
    internal const int RecoveryRefreshSeconds = 60;
    internal const int MinimumSteadyStateRefreshSeconds = 1200;
    private static readonly TimeSpan MinimumHardStaleThreshold = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan HardStaleGrace = TimeSpan.FromMinutes(2);

    public static TimeSpan GetConfiguredRefreshWindow(AppSettings settings, DateTimeOffset nowUtc)
    {
        MarketSession session = new MarketSessionResolver().Resolve(nowUtc);
        int seconds = session == MarketSession.Regular
            ? Math.Max(5, settings.RefreshSecondsPortfolio)
            : Math.Max(5, settings.RefreshSecondsOffHours);

        return TimeSpan.FromSeconds(seconds);
    }

    public static TimeSpan GetEffectiveRefreshWindow(AppSettings settings, DateTimeOffset nowUtc)
    {
        TimeSpan configured = GetConfiguredRefreshWindow(settings, nowUtc);
        return configured < TimeSpan.FromSeconds(MinimumSteadyStateRefreshSeconds)
            ? TimeSpan.FromSeconds(MinimumSteadyStateRefreshSeconds)
            : configured;
    }

    public static TimeSpan GetHardStaleThreshold(AppSettings settings, DateTimeOffset nowUtc)
    {
        TimeSpan effectiveRefreshWindow = GetEffectiveRefreshWindow(settings, nowUtc);
        TimeSpan withGrace = effectiveRefreshWindow + HardStaleGrace;
        return withGrace > MinimumHardStaleThreshold ? withGrace : MinimumHardStaleThreshold;
    }
}
