namespace PortfolioSaver.Shared.Helpers;

public static class TimeFormatHelper
{
    public static string ToAgeString(DateTimeOffset utcTime)
    {
        TimeSpan age = DateTimeOffset.UtcNow - utcTime;
        if (age.TotalSeconds < 60)
            return $"{Math.Max(0, (int)age.TotalSeconds)}s ago";
        if (age.TotalMinutes < 60)
            return $"{(int)age.TotalMinutes}m ago";
        return $"{(int)age.TotalHours}h ago";
    }
}
