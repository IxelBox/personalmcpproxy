using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Mcp.Proxy.Server.Authentication;

namespace Mcp.Proxy.Server.Tests;

public class OAuthFlowTests : IClassFixture<OAuthFlowTests.AppFactory>
{
    public class AppFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.Configure<SingleUserOAuthOptions>(
                    Constants.AuthenticationScheme, opts =>
                    {
                        opts.UserName = "alice";
                        opts.Password = "secret";
                    });
            });
        }
    }

    private readonly HttpClient _client;
    private readonly OAuthService _oauthService;

    public OAuthFlowTests(AppFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        _oauthService = factory.Services.GetRequiredService<OAuthService>();
    }

    // --- helpers ---

    private static (string Verifier, string Challenge) CreatePkce()
    {
        var verifier = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task<string> GetAuthCode(string codeChallenge, string redirectUri = "https://claude.ai/callback")
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "alice",
            ["password"] = "secret",
            ["redirect_uri"] = redirectUri,
            ["state"] = "test-state",
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        });

        var response = await _client.PostAsync("/authorize", form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var location = response.Headers.Location!.ToString();
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(
            new Uri(location).Query);
        return query["code"].ToString();
    }

    private async Task<string> ExchangeCodeForToken(string code, string verifier, string redirectUri = "https://claude.ai/callback")
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = redirectUri,
        });

        var response = await _client.PostAsync("/token", form);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("access_token").GetString()!;
    }

    // --- discovery ---

    [Fact]
    public async Task Discovery_ReturnsExpectedEndpoints()
    {
        var response = await _client.GetAsync("/.well-known/oauth-authorization-server");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("/authorize", json.GetProperty("authorization_endpoint").GetString());
        Assert.Contains("/token", json.GetProperty("token_endpoint").GetString());
    }

    // --- authorize ---

    [Fact]
    public async Task Authorize_WithValidCredentials_RedirectsWithCode()
    {
        var (_, challenge) = CreatePkce();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "alice",
            ["password"] = "secret",
            ["redirect_uri"] = "https://claude.ai/callback",
            ["state"] = "my-state",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        });

        var response = await _client.PostAsync("/authorize", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.Contains("code=", location);
        Assert.Contains("state=my-state", location);
    }

    [Fact]
    public async Task Authorize_WithWrongPassword_Returns200WithError()
    {
        var (_, challenge) = CreatePkce();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "alice",
            ["password"] = "wrong",
            ["redirect_uri"] = "https://claude.ai/callback",
            ["state"] = "s",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        });

        var response = await _client.PostAsync("/authorize", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid credentials", body);
    }

    // --- token ---

    [Fact]
    public async Task Token_WithValidCode_ReturnsAccessToken()
    {
        var (verifier, challenge) = CreatePkce();
        var code = await GetAuthCode(challenge);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = "https://claude.ai/callback",
        });

        var response = await _client.PostAsync("/token", form);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(json.GetProperty("access_token").GetString()));
        Assert.Equal("bearer", json.GetProperty("token_type").GetString());
        Assert.Equal(3600, json.GetProperty("expires_in").GetInt32());
    }

    [Fact]
    public async Task Token_WithWrongVerifier_Returns400()
    {
        var (_, challenge) = CreatePkce();
        var code = await GetAuthCode(challenge);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = "wrong-verifier",
            ["redirect_uri"] = "https://claude.ai/callback",
        });

        var response = await _client.PostAsync("/token", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Token_CodeCannotBeReused()
    {
        var (verifier, challenge) = CreatePkce();
        var code = await GetAuthCode(challenge);

        await ExchangeCodeForToken(code, verifier); // first use — ok

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = "https://claude.ai/callback",
        });
        var second = await _client.PostAsync("/token", form);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    // --- bearer token validation (via OAuthService) ---

    [Fact]
    public async Task BearerToken_ValidToken_IsAccepted()
    {
        var (verifier, challenge) = CreatePkce();
        var code = await GetAuthCode(challenge);
        var token = await ExchangeCodeForToken(code, verifier);

        Assert.True(_oauthService.ValidateToken(token));
    }

    [Fact]
    public void BearerToken_InvalidToken_IsRejected()
    {
        Assert.False(_oauthService.ValidateToken("not-a-real-token"));
    }
}
