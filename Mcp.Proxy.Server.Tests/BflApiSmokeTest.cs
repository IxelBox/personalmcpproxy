using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Mcp.Proxy.Server.Tests;

/// <summary>
/// Live smoke tests against the real BFL API.
/// Set MCPPROXYAPP_BFL_API_KEY to run the API tests; otherwise they are skipped automatically.
///   $env:MCPPROXYAPP_BFL_API_KEY = "your-key"
///   dotnet test --filter "BflApiSmokeTest"
/// </summary>
public class BflApiSmokeTest
{
    private const string EnvVar = "MCPPROXYAPP_BFL_API_KEY";
    private static readonly string? ApiKey = Environment.GetEnvironmentVariable(EnvVar);
    private static readonly HttpClient Http = new() { BaseAddress = new Uri("https://api.bfl.ai") };

    [FactWhenEnv(EnvVar)]
    public async Task Credits_ReturnsBalance()
    {
        Http.DefaultRequestHeaders.Remove("x-key");
        Http.DefaultRequestHeaders.Add("x-key", ApiKey);

        var response = await Http.GetAsync("/v1/credits");
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"HTTP {(int)response.StatusCode}: {body}");
        Console.WriteLine("BFL credits response: " + body);
    }

    [FactWhenEnv(EnvVar)]
    public async Task GenerateImage_ReturnsResultUrl()
    {
        Http.DefaultRequestHeaders.Remove("x-key");
        Http.DefaultRequestHeaders.Add("x-key", ApiKey);

        // 1. Submit
        var submitBody = new JsonObject
        {
            ["prompt"] = "a red apple on a wooden table, product photography",
            ["output_format"] = "jpeg",
            ["safety_tolerance"] = 2,
        };
        var submit = await Http.PostAsJsonAsync("/v1/flux-pro-1.1", submitBody);
        var submitRaw = await submit.Content.ReadAsStringAsync();
        Assert.True(submit.IsSuccessStatusCode, $"Submit HTTP {(int)submit.StatusCode}: {submitRaw}");

        var id = JsonNode.Parse(submitRaw)!["id"]!.GetValue<string>();
        Console.WriteLine("Job id: " + id);

        // 2. Poll
        var deadline = DateTime.UtcNow.AddSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(3000);
            var poll = await Http.GetAsync($"/v1/get_result?id={id}");
            var pollRaw = await poll.Content.ReadAsStringAsync();
            Console.WriteLine("Poll response: " + pollRaw);

            var status = JsonNode.Parse(pollRaw)!["status"]?.GetValue<string>();
            if (status == "Ready")
            {
                Assert.Contains("sample", pollRaw);
                return;
            }
            if (status is "Error" or "Content Moderated" or "Request Moderated")
                Assert.Fail($"BFL returned status '{status}': {pollRaw}");
        }
        Assert.Fail("Timed out waiting for BFL result");
    }

}
