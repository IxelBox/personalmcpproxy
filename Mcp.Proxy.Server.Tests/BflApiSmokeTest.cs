using Mcp.Proxy.Server.Tools;
using Xunit.Abstractions;

namespace Mcp.Proxy.Server.Tests;

/// <summary>
/// Live smoke test -- calls BflApiClient directly to verify the API key and connectivity.
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
    public async Task GenerateImage_FluxPro_ReturnsImageBytes()
    {
        var wrapper = MakeWrapper();

        output.WriteLine("Submitting job (flux-pro-1.1)...");
        var result = await wrapper.GenerateImage(
            prompt: "a red apple on a white background",
            model: "flux-pro-1.1");

        Assert.True(result.DecodedData.Length > 0, "Expected non-empty image data in result");
        Assert.Equal("image/jpeg", result.MimeType);

        output.WriteLine($"Received image: {result.MimeType}, {result.DecodedData.Length} bytes");
    }
}
