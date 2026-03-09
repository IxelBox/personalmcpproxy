using System.Collections.Concurrent;

namespace Mcp.Proxy.Server.Tools;

/// <summary>
/// In-memory store for generated images, keyed by an unguessable GUID.
/// Entries expire after the configured TTL. A background timer removes expired entries.
/// </summary>
public sealed class ImageStore : IDisposable
{
    private readonly ConcurrentDictionary<string, Entry> _store = new();
    private readonly Timer _cleanup;

    public ImageStore(TimeSpan cleanupInterval = default)
    {
        var interval = cleanupInterval == default ? TimeSpan.FromMinutes(1) : cleanupInterval;
        _cleanup = new Timer(_ => RemoveExpired(), null, interval, interval);
    }

    public string Store(byte[] bytes, string mimeType, TimeSpan ttl)
    {
        var id = Guid.NewGuid().ToString("N");
        _store[id] = new Entry(bytes, mimeType, DateTime.UtcNow.Add(ttl));
        return id;
    }

    public (byte[] Bytes, string MimeType)? Get(string id)
    {
        if (!_store.TryGetValue(id, out var entry))
            return null;

        if (entry.Expires <= DateTime.UtcNow)
        {
            _store.TryRemove(id, out _);
            return null;
        }

        return (entry.Bytes, entry.MimeType);
    }

    public void Dispose() => _cleanup.Dispose();

    private void RemoveExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var (key, entry) in _store)
        {
            if (entry.Expires <= now)
                _store.TryRemove(key, out _);
        }
    }

    private sealed record Entry(byte[] Bytes, string MimeType, DateTime Expires);
}
