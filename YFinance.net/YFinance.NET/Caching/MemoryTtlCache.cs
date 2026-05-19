using System.Collections.Concurrent;

namespace YFinance.NET.Caching;

public sealed class MemoryTtlCache<TValue>
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);

    public bool TryGet(string key, out TValue? value)
    {
        value = default;
        if (!_entries.TryGetValue(key, out CacheEntry? entry))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow >= entry.ExpiresUtc)
        {
            _entries.TryRemove(key, out _);
            return false;
        }

        value = entry.Value;
        return true;
    }

    public void Set(string key, TValue value, TimeSpan ttl)
    {
        _entries[key] = new CacheEntry(value, DateTimeOffset.UtcNow.Add(ttl));
    }

    public static string BuildKey(params object?[] parts)
        => string.Join(':', parts.Select(static part => part?.ToString() ?? string.Empty));

    private sealed record CacheEntry(TValue Value, DateTimeOffset ExpiresUtc);
}
