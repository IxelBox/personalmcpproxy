using System.Text.Json.Nodes;
using Mcp.Proxy.Server.Tools;
using Xunit.Abstractions;

namespace Mcp.Proxy.Server.Tests;

/// <summary>
/// Live smoke test — calls BflApiClient directly to verify the API key and connectivity.
/// Set MCPPROXYAPP_BFL_API_KEY to run; otherwise the test is shown as Skipped.
///   PowerShell:  $env:MCPPROXYAPP_BFL_API_KEY="your-key"; dotnet test --filter "BflApiSmokeTest"
///   Bash:        MCPPROXYAPP_BFL_API_KEY="your-key" dotnet test --filter "BflApiSmokeTest"
/// </summary>
public class BflApiSmokeTest(ITestOutputHelper output)
{
    private const string EnvVar = "MCPPROXYAPP_BFL_API_KEY";
    private static readonly string? ApiKey = Environment.GetEnvironmentVariable(EnvVar);

    private static BlackforestLabWrapper MakeWrapper()
    {
        var http = new HttpClient { BaseAddress = new Uri("https://api.bfl.ai") };
        http.DefaultRequestHeaders.Add("x-key", ApiKey);
        return new BlackforestLabWrapper(new BflApiClient(http));
    }

    [FactWhenEnv(EnvVar)]
    public async Task GenerateImage_FluxDev_ReturnsImageUrl()
    {
        var wrapper = MakeWrapper();

        output.WriteLine("Submitting job (flux-dev)...");
        var result = await wrapper.GenerateImage(
            prompt: "a red apple on a white background",
            model: "flux-pro-1.1");

        Assert.False(result.StartsWith("Generation failed"), result);
        Assert.False(result.StartsWith("Request moderated"), result);
        Assert.False(result.StartsWith("Timed out"), result);

        var json = JsonNode.Parse(result);
        var imageUrl = json?["sample"]?.GetValue<string>();

        Assert.False(string.IsNullOrWhiteSpace(imageUrl), $"Expected 'sample' URL in result: {result}");
        Assert.StartsWith("https://", imageUrl);

        output.WriteLine("Image URL: " + imageUrl);
    }
}
