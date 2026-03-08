namespace Mcp.Proxy.Server.Tests;

/// <summary>
/// Runs the test only when the specified environment variable is set.
/// Shows as Skipped (not Passed) when the variable is missing.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class FactWhenEnvAttribute : FactAttribute
{
    public FactWhenEnvAttribute(string envVar)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(envVar)))
            Skip = $"Env var '{envVar}' is not set.";
    }
}
