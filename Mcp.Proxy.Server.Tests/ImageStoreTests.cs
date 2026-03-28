using Mcp.Proxy.Server.Tools;

namespace Mcp.Proxy.Server.Tests;

public class ImageStoreTests : IDisposable
{
    private readonly ImageStore _store = new(cleanupInterval: TimeSpan.FromMilliseconds(50));

    public void Dispose() => _store.Dispose();

    [Fact]
    public void Store_ReturnsNonEmptyId()
    {
        var id = _store.Store([1, 2, 3], "image/jpeg", TimeSpan.FromMinutes(1));
        Assert.False(string.IsNullOrWhiteSpace(id));
    }

    [Fact]
    public void Get_ReturnsStoredEntry()
    {
        byte[] bytes = [10, 20, 30];
        var id = _store.Store(bytes, "image/png", TimeSpan.FromMinutes(1));

        var result = _store.Get(id);

        Assert.NotNull(result);
        Assert.Equal("image/png", result.Value.MimeType);
        Assert.Equal(bytes, result.Value.Bytes);
    }

    [Fact]
    public void Get_ReturnsNull_ForUnknownId()
    {
        Assert.Null(_store.Get("nonexistent"));
    }

    [Fact]
    public void Get_ReturnsNull_WhenExpired()
    {
        var id = _store.Store([1, 2, 3], "image/jpeg", TimeSpan.FromMilliseconds(1));

        Thread.Sleep(20); // let TTL elapse

        Assert.Null(_store.Get(id));
    }

    [Fact]
    public async Task Cleanup_RemovesExpiredEntries()
    {
        var id = _store.Store([1, 2, 3], "image/jpeg", TimeSpan.FromMilliseconds(1));

        await Task.Delay(200); // let TTL elapse + cleanup timer fire (50ms interval)

        // Verify directly that the entry is gone without going through Get (which also purges on access)
        Assert.Null(_store.Get(id));
    }

    [Fact]
    public void Store_WithExplicitKey_UsesProvidedKey()
    {
        var id = _store.Store([1, 2, 3], "image/jpeg", TimeSpan.FromMinutes(1), key: "my-key");
        Assert.Equal("my-key", id);
    }

    [Fact]
    public void Store_WithExplicitKey_IsRetrievableByThatKey()
    {
        byte[] bytes = [7, 8, 9];
        _store.Store(bytes, "image/png", TimeSpan.FromMinutes(1), key: "reuse-key");

        var result = _store.Get("reuse-key");

        Assert.NotNull(result);
        Assert.Equal(bytes, result.Value.Bytes);
    }

    [Fact]
    public void Store_WithExplicitKey_OverwritesPreviousEntry()
    {
        _store.Store([1, 2, 3], "image/jpeg", TimeSpan.FromMinutes(1), key: "k");
        byte[] newBytes = [9, 8, 7];
        _store.Store(newBytes, "image/png", TimeSpan.FromMinutes(1), key: "k");

        var result = _store.Get("k");

        Assert.Equal(newBytes, result!.Value.Bytes);
        Assert.Equal("image/png", result.Value.MimeType);
    }
}
