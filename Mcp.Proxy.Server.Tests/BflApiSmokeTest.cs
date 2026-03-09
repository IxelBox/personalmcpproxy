using Mcp.Proxy.Server.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
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

        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new HostString("localhost");
        var accessor = new HttpContextAccessor { HttpContext = ctx };

        var config = new ConfigurationBuilder().Build();
        return new BlackforestLabWrapper(new BflApiClient(http), new ImageStore(), accessor, config);
    }

    [FactWhenEnv(EnvVar)]
    public async Task GenerateImage_FluxPro_ReturnsImageBytes()
    {
        var wrapper = MakeWrapper();

        output.WriteLine("Submitting job (flux-pro-1.1)...");
        var result = await wrapper.GenerateImage(
            prompt: "a red apple on a white background",
            model: "flux-pro-1.1");

        Assert.StartsWith("https://", result);

        output.WriteLine($"Image URL: {result}");
    }
}
