using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Mcp.Proxy.Server.Authentication;

public static class OAuthEndpoints
{
    public static void MapOAuthEndpoints(this WebApplication app)
    {
        // OAuth2 discovery document — Claude web fetches this first
        app.MapGet("/.well-known/oauth-authorization-server", (HttpContext ctx) =>
        {
            var base_ = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            var doc = new OAuthDiscovery(
                Issuer: base_,
                AuthorizationEndpoint: $"{base_}/authorize",
                TokenEndpoint: $"{base_}/token",
                ResponseTypesSupported: ["code"],
                GrantTypesSupported: ["authorization_code"],
                CodeChallengeMethodsSupported: ["S256"]);

            return Results.Json(doc, OAuthJsonContext.Default.OAuthDiscovery);
        });

        // Show login form
        app.MapGet("/authorize", (
            string? redirect_uri, string? state,
            string? code_challenge, string? code_challenge_method) =>
        {
            var html = LoginForm(
                WebUtility.HtmlEncode(redirect_uri ?? ""),
                WebUtility.HtmlEncode(state ?? ""),
                WebUtility.HtmlEncode(code_challenge ?? ""),
                WebUtility.HtmlEncode(code_challenge_method ?? "S256"),
                error: null);

            return Results.Content(html, "text/html");
        });

        // Handle login form submission
        app.MapPost("/authorize", async (
            HttpContext ctx,
            OAuthService oauth,
            IOptionsMonitor<SingleUserOAuthOptions> optionsMonitor) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var username = form["username"].ToString();
            var password = form["password"].ToString();
            var redirectUri = form["redirect_uri"].ToString();
            var state = form["state"].ToString();
            var codeChallenge = form["code_challenge"].ToString();
            var codeChallengeMethod = form["code_challenge_method"].ToString();

            var opts = optionsMonitor.Get(Constants.AuthenticationScheme);
            if (username != opts.UserName || password != opts.Password)
            {
                var html = LoginForm(
                    WebUtility.HtmlEncode(redirectUri),
                    WebUtility.HtmlEncode(state),
                    WebUtility.HtmlEncode(codeChallenge),
                    WebUtility.HtmlEncode(codeChallengeMethod),
                    error: "Invalid credentials.");
                return Results.Content(html, "text/html");
            }

            var code = oauth.CreateAuthCode(codeChallenge, codeChallengeMethod, redirectUri);
            var location = $"{redirectUri}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}";
            return Results.Redirect(location);
        });

        // Exchange auth code for bearer token
        app.MapPost("/token", async (HttpContext ctx, OAuthService oauth) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            if (form["grant_type"].ToString() != "authorization_code")
                return Results.Json(new OAuthError("unsupported_grant_type"), OAuthJsonContext.Default.OAuthError, statusCode: 400);

            var token = oauth.ExchangeCode(
                form["code"].ToString(),
                form["code_verifier"].ToString(),
                form["redirect_uri"].ToString());

            if (token is null)
                return Results.Json(new OAuthError("invalid_grant"), OAuthJsonContext.Default.OAuthError, statusCode: 400);

            return Results.Json(new OAuthToken(token, "bearer", 3600), OAuthJsonContext.Default.OAuthToken);
        });
    }

    private static string LoginForm(string redirectUri, string state, string codeChallenge, string codeChallengeMethod, string? error) => $"""
        <!DOCTYPE html>
        <html>
        <head><title>Sign in</title></head>
        <body>
        {(error is not null ? $"<p style='color:red'>{error}</p>" : "")}
        <form method="post">
            <input type="hidden" name="redirect_uri" value="{redirectUri}" />
            <input type="hidden" name="state" value="{state}" />
            <input type="hidden" name="code_challenge" value="{codeChallenge}" />
            <input type="hidden" name="code_challenge_method" value="{codeChallengeMethod}" />
            <label>Username: <input type="text" name="username" /></label><br/>
            <label>Password: <input type="password" name="password" /></label><br/>
            <button type="submit">Sign in</button>
        </form>
        </body>
        </html>
        """;
}

record OAuthDiscovery(
    [property: JsonPropertyName("issuer")] string Issuer,
    [property: JsonPropertyName("authorization_endpoint")] string AuthorizationEndpoint,
    [property: JsonPropertyName("token_endpoint")] string TokenEndpoint,
    [property: JsonPropertyName("response_types_supported")] string[] ResponseTypesSupported,
    [property: JsonPropertyName("grant_types_supported")] string[] GrantTypesSupported,
    [property: JsonPropertyName("code_challenge_methods_supported")] string[] CodeChallengeMethodsSupported);

record OAuthError([property: JsonPropertyName("error")] string Error);

record OAuthToken(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);

[JsonSerializable(typeof(OAuthDiscovery))]
[JsonSerializable(typeof(OAuthError))]
[JsonSerializable(typeof(OAuthToken))]
internal partial class OAuthJsonContext : JsonSerializerContext;
