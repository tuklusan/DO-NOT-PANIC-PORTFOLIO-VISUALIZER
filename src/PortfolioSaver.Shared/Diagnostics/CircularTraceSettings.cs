using System.Globalization;

namespace PortfolioSaver.Shared.Diagnostics;

public static class CircularTraceSettings
{
    public const string MaxTraceMegabytesEnvironmentVariable = "DONOTPANICPORTFOLIOVISUALIZER_TRACE_MAX_MB";
    public const int DefaultMaxTraceMegabytes = 32;
    public const int MinimumMaxTraceMegabytes = 4;
    public const int MaximumMaxTraceMegabytes = 256;

    public static int ResolveMaxTraceBytes()
    {
        string? configured = Environment.GetEnvironmentVariable(MaxTraceMegabytesEnvironmentVariable)?.Trim();
        if (!int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out int megabytes))
            megabytes = DefaultMaxTraceMegabytes;

        megabytes = Math.Clamp(megabytes, MinimumMaxTraceMegabytes, MaximumMaxTraceMegabytes);
        return megabytes * 1024 * 1024;
    }

    public static int ResolveCachedMaxTraceBytes(ref int cachedBytes)
    {
        int resolved = Volatile.Read(ref cachedBytes);
        if (resolved > 0)
            return resolved;

        resolved = ResolveMaxTraceBytes();
        int previous = Interlocked.CompareExchange(ref cachedBytes, resolved, 0);
        return previous > 0 ? previous : resolved;
    }
}
