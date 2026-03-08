using Mcp.Proxy.Server.Authentication;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Mcp.Proxy.Server.Tests;

public class OAuthServiceTests
{
    // Minimal IOptionsMonitor stub — no change notifications needed for tests.
    private sealed class StubMonitor(SingleUserOAuthOptions opts) : IOptionsMonitor<SingleUserOAuthOptions>
    {
        public SingleUserOAuthOptions CurrentValue => opts;
        public SingleUserOAuthOptions Get(string? name) => opts;
        public IDisposable? OnChange(Action<SingleUserOAuthOptions, string?> listener) => null;
    }

    private static OAuthService Make(string? signingKey = null)
    {
        var opts = new SingleUserOAuthOptions
        {
            UserName = "alice",
            Password = "secret",
            TokenSigningKey = signingKey,
        };
        return new OAuthService(new StubMonitor(opts), NullLogger<OAuthService>.Instance);
    }

    [Fact]
    public void Token_WithConfiguredKey_ValidatesSuccessfully()
    {
        var svc = Make("my-test-signing-key");
        var code = svc.CreateAuthCode("challenge", "plain", "https://example.com/cb");
        var token = svc.ExchangeCode(code, "challenge", "https://example.com/cb");

        Assert.NotNull(token);
        Assert.True(svc.ValidateToken(token!));
    }

    [Fact]
    public void Token_SurvivesRestart_WhenSameKeyConfigured()
    {
        const string key = "stable-key-across-restarts";

        // "before restart": generate a token
        var svc1 = Make(key);
        var code = svc1.CreateAuthCode("challenge", "plain", "https://example.com/cb");
        var token = svc1.ExchangeCode(code, "challenge", "https://example.com/cb")!;

        // "after restart": new instance, same key — token must still be valid
        var svc2 = Make(key);
        Assert.True(svc2.ValidateToken(token));
    }

    [Fact]
    public void Token_IsInvalidated_WhenKeyChanges()
    {
        var svc1 = Make("key-A");
        var code = svc1.CreateAuthCode("challenge", "plain", "https://example.com/cb");
        var token = svc1.ExchangeCode(code, "challenge", "https://example.com/cb")!;

        var svc2 = Make("key-B");
        Assert.False(svc2.ValidateToken(token));
    }

    [Fact]
    public void Token_WithRandomKey_ValidatesLocally()
    {
        // No key configured → random key; token must still be valid within the same instance
        var svc = Make(null);
        var code = svc.CreateAuthCode("challenge", "plain", "https://example.com/cb");
        var token = svc.ExchangeCode(code, "challenge", "https://example.com/cb");

        Assert.NotNull(token);
        Assert.True(svc.ValidateToken(token!));
    }

    [Fact]
    public void ValidateToken_GarbageInput_ReturnsFalse()
    {
        var svc = Make("my-key");
        Assert.False(svc.ValidateToken("not-a-token"));
        Assert.False(svc.ValidateToken(""));
        Assert.False(svc.ValidateToken("aaa.bbb"));
    }
}
