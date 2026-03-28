using Mcp.Proxy.Server.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit.Abstractions;

namespace Mcp.Proxy.Server.Tests;

/// <summary>
/// Live smoke tests -- call BflApiClient directly to verify the API key and connectivity.
/// Set MCPPROXYAPP_BFL_API_KEY to run; otherwise tests are shown as Skipped.
///   PowerShell:  $env:MCPPROXYAPP_BFL_API_KEY="your-key"; dotnet test --filter "BflApiSmokeTest"
///   Bash:        MCPPROXYAPP_BFL_API_KEY="your-key" dotnet test --filter "BflApiSmokeTest"
/// </summary>
public class BflApiSmokeTest(ITestOutputHelper output)
{
    private const string EnvVar = "MCPPROXYAPP_BFL_API_KEY";
    private static readonly string? ApiKey = Environment.GetEnvironmentVariable(EnvVar);

    // Parse width/height from a JPEG SOF marker
    private static (int Width, int Height) ReadJpegDimensions(byte[] jpeg)
    {
        int i = 2;
        while (i < jpeg.Length - 8)
        {
            if (jpeg[i] != 0xFF) break;
            byte marker = jpeg[i + 1];
            if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3)
                return ((jpeg[i + 7] << 8) | jpeg[i + 8], (jpeg[i + 5] << 8) | jpeg[i + 6]);
            i += 2 + ((jpeg[i + 2] << 8) | jpeg[i + 3]);
        }
        throw new InvalidOperationException("Cannot read JPEG dimensions.");
    }

    // All-white grayscale PNG matching the given dimensions — suitable as a BFL Fill mask
    private static string CreateWhitePngMask(int width, int height)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WritePngChunk(ms, "IHDR"u8, [
            (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width,
            (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height,
            8, 0, 0, 0, 0  // 8-bit grayscale
        ]);
        var raw = new byte[height * (1 + width)];
        for (int y = 0; y < height; y++)
            Array.Fill(raw, (byte)0xFF, y * (1 + width) + 1, width);
        using var idatBuf = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(idatBuf, System.IO.Compression.CompressionMode.Compress, leaveOpen: true))
            zlib.Write(raw);
        WritePngChunk(ms, "IDAT"u8, idatBuf.ToArray());
        WritePngChunk(ms, "IEND"u8, []);
        return Convert.ToBase64String(ms.ToArray());
    }

    private static void WritePngChunk(Stream s, ReadOnlySpan<byte> type, byte[] data)
    {
        s.Write([(byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length]);
        s.Write(type);
        s.Write(data);
        uint crc = 0xFFFFFFFF;
        foreach (byte b in type) crc = (crc >> 8) ^ _crc32Table[(crc ^ b) & 0xFF];
        foreach (byte b in data) crc = (crc >> 8) ^ _crc32Table[(crc ^ b) & 0xFF];
        crc = ~crc;
        s.Write([(byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc]);
    }

    private static readonly uint[] _crc32Table = Enumerable.Range(0, 256).Select(i =>
    {
        uint c = (uint)i;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static BlackforestLabWrapper MakeWrapper(ImageStore? store = null)
    {
        var http = new HttpClient { BaseAddress = new Uri("https://api.bfl.ai") };
        http.DefaultRequestHeaders.Add("x-key", ApiKey);

        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new HostString("localhost");
        var accessor = new HttpContextAccessor { HttpContext = ctx };

        var config = new ConfigurationBuilder().Build();
        return new BlackforestLabWrapper(new BflApiClient(http), store ?? new ImageStore(), accessor, config);
    }

    [FactWhenEnv(EnvVar)]
    public async Task GenerateImage_ReturnsHtmlImgTag()
    {
        var wrapper = MakeWrapper();
        output.WriteLine("Submitting GenerateImage job (flux-2-klein-4b)...");

        var result = await wrapper.GenerateImage(
            prompt: "a red apple on a white background",
            model: "flux-2-klein-4b",
            returnHtmlTag: true);

        Assert.StartsWith("<img src=\"https://", result);
        output.WriteLine(result);
    }

    [FactWhenEnv(EnvVar)]
    public async Task GenerateImage_ReturnsPlainUrl()
    {
        var wrapper = MakeWrapper();
        output.WriteLine("Submitting GenerateImage job (flux-2-klein-4b)...");

        var result = await wrapper.GenerateImage(
            prompt: "a red apple on a white background",
            model: "flux-2-klein-4b",
            returnHtmlTag: false);

        Assert.StartsWith("https://", result);
        output.WriteLine($"Image URL: {result}");
    }

    [FactWhenEnv(EnvVar)]
    public async Task EditImage_ReturnsHtmlImgTag()
    {
        var store = new ImageStore();
        var wrapper = MakeWrapper(store);

        // Step 1: generate a source image to edit
        output.WriteLine("Generating source image for editing (flux-2-klein-4b)...");
        var sourceUrl = await wrapper.GenerateImage(
            prompt: "a red apple on a white background",
            model: "flux-2-klein-4b",
            returnHtmlTag: false);
        output.WriteLine($"Source URL: {sourceUrl}");

        // Step 2: pull the bytes directly from the store — no HTTP server needed
        var imageId = sourceUrl.Split('/').Last();
        var (bytes, _) = store.Get(imageId) ?? throw new InvalidOperationException("Image not found in store.");
        var imageBase64 = Convert.ToBase64String(bytes);

        // Step 3: edit it
        output.WriteLine("Submitting EditImage job (flux-2-flex)...");
        var result = await wrapper.EditImage(
            prompt: "change the apple to a green apple",
            imageBase64: imageBase64,
            model: "flux-2-flex",
            returnHtmlTag: true);

        Assert.StartsWith("<img src=\"https://", result);
        output.WriteLine(result);
    }

    [FactWhenEnv(EnvVar)]
    public async Task GenerateImage_ReturnsBase64HtmlImgTag()
    {
        var wrapper = MakeWrapper();
        output.WriteLine("Submitting GenerateImage job (flux-2-klein-4b, base64+html)...");

        var result = await wrapper.GenerateImage(
            prompt: "a red apple on a white background",
            model: "flux-2-klein-4b",
            returnHtmlTag: true,
            returnBase64: true);

        Assert.StartsWith("<img src=\"data:image/jpeg;base64,", result);
        output.WriteLine($"Result length: {result.Length} chars");
    }

    [FactWhenEnv(EnvVar)]
    public async Task GenerateImage_ReturnsRawBase64()
    {
        var wrapper = MakeWrapper();
        output.WriteLine("Submitting GenerateImage job (flux-2-klein-4b, raw base64)...");

        var result = await wrapper.GenerateImage(
            prompt: "a red apple on a white background",
            model: "flux-2-klein-4b",
            returnHtmlTag: false,
            returnBase64: true);

        // Valid base64 string — decodable and non-empty
        var decoded = Convert.FromBase64String(result);
        Assert.NotEmpty(decoded);
        output.WriteLine($"Base64 length: {result.Length} chars, decoded: {decoded.Length} bytes");
    }

    [FactWhenEnv(EnvVar)]
    public async Task FillImage_ReturnsHtmlImgTag()
    {
        var store = new ImageStore();
        var wrapper = MakeWrapper(store);

        // Generate source image to fill
        output.WriteLine("Generating source image for fill (flux-2-klein-4b)...");
        var sourceUrl = await wrapper.GenerateImage(
            prompt: "a white coffee mug on a table",
            model: "flux-2-klein-4b",
            returnHtmlTag: false);
        output.WriteLine($"Source URL: {sourceUrl}");

        var imageId = sourceUrl.Split('/').Last();
        var (bytes, _) = store.Get(imageId) ?? throw new InvalidOperationException("Image not found in store.");
        var imageBase64 = Convert.ToBase64String(bytes);

        var (w, h) = ReadJpegDimensions(bytes);
        var maskBase64 = CreateWhitePngMask(w, h);
        output.WriteLine($"Image dimensions: {w}x{h}, mask PNG: {maskBase64.Length} chars base64");

        output.WriteLine("Submitting FillImage job...");
        var result = await wrapper.FillImage(
            prompt: "a red coffee mug on a table",
            imageBase64: imageBase64,
            maskBase64: maskBase64,
            returnHtmlTag: true);

        Assert.StartsWith("<img src=\"https://", result);
        output.WriteLine(result);
    }

    [FactWhenEnv(EnvVar)]
    public async Task GenerateImage_CacheKey_SecondCallIsInstant()
    {
        var wrapper = MakeWrapper();
        const string key = "smoke-test-apple";

        output.WriteLine("First call — generates image and caches it...");
        var first = await wrapper.GenerateImage(
            prompt: "a red apple on a white background",
            model: "flux-2-klein-4b",
            returnHtmlTag: false,
            cacheKey: key);
        output.WriteLine($"First result: {first}");

        output.WriteLine("Second call — should return from cache immediately...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var second = await wrapper.GenerateImage(
            prompt: "a red apple on a white background",
            model: "flux-2-klein-4b",
            returnHtmlTag: false,
            cacheKey: key);
        sw.Stop();

        Assert.Equal(first, second);
        Assert.True(sw.ElapsedMilliseconds < 500, $"Cache hit took {sw.ElapsedMilliseconds}ms — expected <500ms");
        output.WriteLine($"Cache hit returned in {sw.ElapsedMilliseconds}ms");
    }

    [FactWhenEnv(EnvVar)]
    public async Task GetBflCredits_ReturnsBalance()
    {
        var wrapper = MakeWrapper();
        var result = await wrapper.GetBflCredits();
        output.WriteLine($"Credits: {result}");
        Assert.NotEmpty(result);
    }
}
