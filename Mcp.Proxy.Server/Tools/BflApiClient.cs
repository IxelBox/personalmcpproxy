using System.Text;
using System.Text.Json.Nodes;

namespace Mcp.Proxy.Server.Tools;

/// <summary>
/// Real implementation of <see cref="IBflApiClient"/> backed by an <see cref="HttpClient"/>
/// that is pre-configured with the BFL base address and API key header.
/// </summary>
public sealed class BflApiClient(HttpClient http) : IBflApiClient
{
    public async Task<string> SubmitJobAsync(string endpoint, JsonObject body, CancellationToken ct = default)
    {
        var response = await http.PostAsync(endpoint, ToJsonContent(body), ct);
        var raw = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"BFL submit failed ({(int)response.StatusCode} {response.ReasonPhrase}): {raw}",
                inner: null,
                statusCode: response.StatusCode);

        var json = JsonNode.Parse(raw)
            ?? throw new InvalidOperationException($"BFL returned non-JSON: {raw}");

        return json["id"]?.GetValue<string>()
            ?? throw new InvalidOperationException($"BFL response missing 'id': {raw}");
    }

    public async Task<JsonNode> GetResultAsync(string jobId, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"/v1/get_result?id={jobId}", ct);
        var raw = await response.Content.ReadAsStringAsync(ct);

        // 404 means the task hasn't been registered yet — treat as Pending
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new JsonObject { ["status"] = "Pending" };

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"BFL get_result failed ({(int)response.StatusCode} {response.ReasonPhrase}): {raw}",
                inner: null,
                statusCode: response.StatusCode);

        return JsonNode.Parse(raw)
            ?? throw new InvalidOperationException($"BFL returned non-JSON: {raw}");
    }

    public async Task<string> GetCreditsAsync(CancellationToken ct = default)
    {
        var response = await http.GetAsync("/v1/credits", ct);
        var raw = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"BFL credits failed ({(int)response.StatusCode} {response.ReasonPhrase}): {raw}",
                inner: null,
                statusCode: response.StatusCode);

        return raw;
    }

    public async Task<byte[]> DownloadImageAsync(string url, CancellationToken ct = default)
    {
        var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private static StringContent ToJsonContent(JsonObject obj) =>
        new(obj.ToJsonString(), Encoding.UTF8, "application/json");
}
