using System.Text.Json.Nodes;

namespace Mcp.Proxy.Server.Tools;

/// <summary>
/// Low-level HTTP abstraction for the Black Forest Labs API.
/// Inject this into production code and mock it in unit tests.
/// </summary>
public interface IBflApiClient
{
    /// <summary>Submits a generation job and returns the job ID.</summary>
    Task<string> SubmitJobAsync(string endpoint, JsonObject body, CancellationToken ct = default);

    /// <summary>Polls a single time for the result of a previously submitted job.</summary>
    Task<JsonNode> GetResultAsync(string jobId, CancellationToken ct = default);

    /// <summary>Returns the raw JSON credit-balance response.</summary>
    Task<string> GetCreditsAsync(CancellationToken ct = default);
}
