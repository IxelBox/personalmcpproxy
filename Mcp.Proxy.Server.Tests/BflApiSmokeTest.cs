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
    public async Task GetBflCredits_ReturnsBalance()
    {
        var wrapper = MakeWrapper();
        var result = await wrapper.GetBflCredits();
        output.WriteLine($"Credits: {result}");
        Assert.NotEmpty(result);
    }
}
