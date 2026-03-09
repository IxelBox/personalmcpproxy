using Mcp.Proxy.Server.Tools;

namespace Mcp.Proxy.Server.Tests;

/// <summary>
/// Live smoke test — calls BflApiClient directly to verify the API key and connectivity.
/// Set MCPPROXYAPP_BFL_API_KEY to run; otherwise the test is shown as Skipped.
///   PowerShell:  $env:MCPPROXYAPP_BFL_API_KEY="your-key"; dotnet test --filter "BflApiSmokeTest"
///   Bash:        MCPPROXYAPP_BFL_API_KEY="your-key" dotnet test --filter "BflApiSmokeTest"
/// </summary>
public class BflApiSmokeTest
{
    private const string EnvVar = "MCPPROXYAPP_BFL_API_KEY";
    private static readonly string? ApiKey = Environment.GetEnvironmentVariable(EnvVar);

    private static BflApiClient MakeClient()
    {
        var http = new HttpClient { BaseAddress = new Uri("https://api.bfl.ai") };
        http.DefaultRequestHeaders.Add("x-key", ApiKey);
        return new BflApiClient(http);
    }

    [FactWhenEnv(EnvVar)]
    public async Task GetCredits_ReturnsBalance()
    {
        var client = MakeClient();

        var result = await client.GetCreditsAsync();

        Assert.False(string.IsNullOrWhiteSpace(result), "Expected non-empty JSON from /v1/credits");
        Console.WriteLine("BFL credits: " + result);
    }
}
