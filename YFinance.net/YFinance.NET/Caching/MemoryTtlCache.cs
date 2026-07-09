// ============================================================================
// Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
// Proprietary rights reserved except as expressly licensed herein.
//
// DO NOT PANIC PORTFOLIO VISUALIZER
// This file is governed by the SANYALnet Labs Non-Commercial License in the
// root LICENSE file. Non-Commercial use is permitted; Commercial Use and use
// for AI/ML model training are prohibited unless separately authorized.
//
// Attribution is required: "Based on original work by Supratim Sanyal of
// SANYALnet Labs." See LICENSE for full terms, warranty disclaimer, termination,
// patent, trademark, and governing-law provisions.
// ============================================================================
namespace YFinance.NET.Caching;

public sealed class MemoryTtlCache<TValue>
{
    public const int DefaultMaxEntries = 1024;

    private readonly object _gate = new();
    private readonly int _maxEntries;
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<CacheEntry> _lru = [];

    public MemoryTtlCache(int maxEntries = DefaultMaxEntries)
    {
        _maxEntries = Math.Max(1, maxEntries);
    }

    public int Count
    {
        get
        {
            lock (_gate)
                return _entries.Count;
        }
    }

    public bool TryGet(string key, out TValue? value)
    {
        value = default;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out LinkedListNode<CacheEntry>? node))
            {
                return false;
            }

            if (DateTimeOffset.UtcNow >= node.Value.ExpiresUtc)
            {
                RemoveNode(node);
                return false;
            }

            _lru.Remove(node);
            _lru.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
    }

    public void Set(string key, TValue value, TimeSpan ttl)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        CacheEntry entry = new(key, value, now.Add(ttl));
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out LinkedListNode<CacheEntry>? existing))
                RemoveNode(existing);

            LinkedListNode<CacheEntry> node = new(entry);
            _lru.AddFirst(node);
            _entries[key] = node;
            RemoveExpired(now);
            TrimToCapacity();
        }
    }

    public static string BuildKey(params object?[] parts)
        => string.Join(':', parts.Select(static part => part?.ToString() ?? string.Empty));

    private void RemoveExpired(DateTimeOffset now)
    {
        for (LinkedListNode<CacheEntry>? node = _lru.Last; node is not null;)
        {
            LinkedListNode<CacheEntry>? previous = node.Previous;
            if (now >= node.Value.ExpiresUtc)
                RemoveNode(node);

            node = previous;
        }
    }

    private void TrimToCapacity()
    {
        while (_entries.Count > _maxEntries && _lru.Last is not null)
            RemoveNode(_lru.Last);
    }

    private void RemoveNode(LinkedListNode<CacheEntry> node)
    {
        _lru.Remove(node);
        _entries.Remove(node.Value.Key);
    }

    private sealed record CacheEntry(string Key, TValue Value, DateTimeOffset ExpiresUtc);
}
