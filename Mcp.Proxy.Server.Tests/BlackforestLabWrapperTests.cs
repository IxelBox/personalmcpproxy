using System.Text.Json.Nodes;
using Mcp.Proxy.Server.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Mcp.Proxy.Server.Tests;

public class BlackforestLabWrapperTests
{
    private static readonly byte[] FakeImageBytes = [0xFF, 0xD8, 0xFF, 0xE0]; // minimal JPEG header

    private static (BlackforestLabWrapper Wrapper, ImageStore Store, FakeBflApiClient Api) MakeWrapper()
    {
        var api = new FakeBflApiClient();
        var store = new ImageStore();

        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new HostString("localhost");
        var accessor = new HttpContextAccessor { HttpContext = ctx };

        var config = new ConfigurationBuilder().Build();
        return (new BlackforestLabWrapper(api, store, accessor, config), store, api);
    }

    [Fact]
    public async Task GenerateImage_Default_StoresAndReturnsHtmlTag()
    {
        var (wrapper, store, api) = MakeWrapper();

        var result = await wrapper.GenerateImage("a cat");

        Assert.StartsWith("<img src=\"https://localhost/images/", result);
        Assert.Equal(1, api.SubmitCallCount);
    }

    [Fact]
    public async Task GenerateImage_ReturnBase64_WithNoKey_DoesNotStore()
    {
        var (wrapper, store, api) = MakeWrapper();

        var result = await wrapper.GenerateImage("a cat", returnHtmlTag: true, returnBase64: true);

        Assert.StartsWith("<img src=\"data:image/jpeg;base64,", result);
        Assert.Equal(1, api.SubmitCallCount);
        // ImageStore should be empty — nothing stored
        Assert.Null(store.Get(result)); // result is the tag, not an id — store should have no entries
    }

    [Fact]
    public async Task GenerateImage_ReturnBase64_WithKey_StoresForCaching()
    {
        var (wrapper, store, _) = MakeWrapper();

        await wrapper.GenerateImage("a cat", returnBase64: true, cacheKey: "cat-key");

        Assert.NotNull(store.Get("cat-key"));
    }

    [Fact]
    public async Task GenerateImage_CacheKey_SecondCallSkipsApi()
    {
        var (wrapper, _, api) = MakeWrapper();

        await wrapper.GenerateImage("a cat", cacheKey: "my-cat");
        await wrapper.GenerateImage("a cat", cacheKey: "my-cat");

        Assert.Equal(1, api.SubmitCallCount); // BFL called only once
    }

    [Fact]
    public async Task GenerateImage_CacheKey_ReturnsConsistentResult()
    {
        var (wrapper, _, _) = MakeWrapper();

        var first  = await wrapper.GenerateImage("a cat", cacheKey: "cat");
        var second = await wrapper.GenerateImage("a cat", cacheKey: "cat");

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task GenerateImage_NoCacheKey_AlwaysCallsApi()
    {
        var (wrapper, _, api) = MakeWrapper();

        await wrapper.GenerateImage("a cat");
        await wrapper.GenerateImage("a cat");

        Assert.Equal(2, api.SubmitCallCount);
    }

    [Fact]
    public async Task GenerateImage_PlainUrl_ReturnsUrl()
    {
        var (wrapper, _, _) = MakeWrapper();

        var result = await wrapper.GenerateImage("a cat", returnHtmlTag: false);

        Assert.StartsWith("https://localhost/images/", result);
        Assert.DoesNotContain("<img", result);
    }

    [Fact]
    public async Task GenerateImage_RawBase64_ReturnsDecodableString()
    {
        var (wrapper, _, _) = MakeWrapper();

        var result = await wrapper.GenerateImage("a cat", returnHtmlTag: false, returnBase64: true);

        var decoded = Convert.FromBase64String(result);
        Assert.Equal(FakeImageBytes, decoded);
    }

    // ---

    private sealed class FakeBflApiClient : IBflApiClient
    {
        public int SubmitCallCount { get; private set; }

        public Task<string> SubmitJobAsync(string endpoint, JsonObject body, CancellationToken ct)
        {
            SubmitCallCount++;
            return Task.FromResult("fake-job-id");
        }

        public Task<JsonNode> GetResultAsync(string id, CancellationToken ct) =>
            Task.FromResult<JsonNode>(new JsonObject
            {
                ["status"] = "Ready",
                ["result"] = new JsonObject { ["sample"] = "https://cdn.bfl.ai/fake-image.jpg" }
            });

        public Task<byte[]> DownloadImageAsync(string url, CancellationToken ct) =>
            Task.FromResult(FakeImageBytes);

        public Task<string> GetCreditsAsync(CancellationToken ct) =>
            Task.FromResult("100.00");
    }
}
