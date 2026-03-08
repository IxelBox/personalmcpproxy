using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Mcp.Proxy.Server.Authentication;

public class SingleUserOAuthHandler(
    IOptionsMonitor<SingleUserOAuthOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    OAuthService oauthService)
    : AuthenticationHandler<SingleUserOAuthOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader) ||
            !authHeader.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.Fail("Missing or invalid Authorization header."));

        var token = authHeader.ToString()["Bearer ".Length..].Trim();
        if (!oauthService.ValidateToken(token))
            return Task.FromResult(AuthenticateResult.Fail("Invalid or expired token."));

        var claims = new[] { new Claim(ClaimTypes.Name, Options.UserName ?? "user") };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
