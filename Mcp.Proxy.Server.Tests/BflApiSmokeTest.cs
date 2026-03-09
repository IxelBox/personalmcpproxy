using Mcp.Proxy.Server.Tools;

namespace Mcp.Proxy.Server.Tests;

/// <summary>
/// Live smoke tests — calls BlackforestLabWrapper directly, same code path as the MCP tool.
/// Set MCPPROXYAPP_BFL_API_KEY to run; otherwise tests are shown as Skipped.
///   $env:MCPPROXYAPP_BFL_API_KEY = "your-key"
///   dotnet test --filter "BflApiSmokeTest"
/// </summary>
public class BflApiSmokeTest
{
    private const string EnvVar = "MCPPROXYAPP_BFL_API_KEY";
    private static readonly string? ApiKey = Environment.GetEnvironmentVariable(EnvVar);

    private static BlackforestLabWrapper MakeWrapper()
    {
        var http = new HttpClient { BaseAddress = new Uri("https://api.bfl.ai") };
        http.DefaultRequestHeaders.Add("x-key", ApiKey);
        return new BlackforestLabWrapper(new SingleClientFactory(http));
    }

    // Minimal IHttpClientFactory stub that returns one pre-configured client.
    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    [FactWhenEnv(EnvVar)]
    public async Task Credits_ReturnsBalance()
    {
        var wrapper = MakeWrapper();
        var result = await wrapper.GetBflCredits();

        Assert.False(string.IsNullOrWhiteSpace(result));
        Console.WriteLine("BFL credits: " + result);
    }

    [FactWhenEnv(EnvVar)]
    public async Task GenerateImage_ReturnsResultUrl()
    {
        var wrapper = MakeWrapper();
        var result = await wrapper.GenerateImage(
            prompt: "a red apple on a wooden table, product photography");

        Console.WriteLine("GenerateImage result: " + result);
        Assert.False(result.StartsWith("Generation failed"), result);
        Assert.False(result.StartsWith("Request moderated"), result);
        Assert.False(result.StartsWith("Timed out"), result);
        Assert.Contains("sample", result);
    }
}
