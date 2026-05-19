using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace YFinance.NET.Caching;

public sealed class PersistentTtlCache<TValue>
{
    private readonly string _rootPath;
    private readonly JsonSerializerOptions _serializerOptions;

    public PersistentTtlCache(string rootPath, JsonSerializerOptions? serializerOptions = null)
    {
        _rootPath = rootPath;
        _serializerOptions = serializerOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<TValue?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        string path = GetPath(key);
        if (!File.Exists(path))
        {
            return default;
        }

        await using FileStream stream = File.OpenRead(path);
        CacheEnvelope<TValue>? envelope = await JsonSerializer.DeserializeAsync<CacheEnvelope<TValue>>(stream, _serializerOptions, cancellationToken).ConfigureAwait(false);
        if (envelope is null || envelope.ExpiresUtc <= DateTimeOffset.UtcNow)
        {
            TryDelete(path);
            return default;
        }

        return envelope.Value;
    }

    public async Task SetAsync(string key, TValue value, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        string path = GetPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        CacheEnvelope<TValue> envelope = new(value, DateTimeOffset.UtcNow.Add(ttl));
        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, envelope, _serializerOptions, cancellationToken).ConfigureAwait(false);
    }

    public static string BuildKey(params object?[] parts)
        => string.Join(':', parts.Select(static part => part?.ToString() ?? string.Empty));

    private string GetPath(string key)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        string fileName = Convert.ToHexString(bytes).ToLowerInvariant();
        return Path.Combine(_rootPath, fileName + ".json");
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private sealed record CacheEnvelope<T>(T Value, DateTimeOffset ExpiresUtc);
}