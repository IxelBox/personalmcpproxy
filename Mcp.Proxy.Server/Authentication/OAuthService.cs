using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Mcp.Proxy.Server.Authentication;

/// <summary>
/// Handles OAuth2 auth codes (in-memory, short-lived) and stateless HMAC-signed bearer tokens.
/// Tokens survive server restarts when a stable TokenSigningKey is configured.
/// </summary>
public class OAuthService
{
    private readonly Dictionary<string, (string CodeChallenge, string Method, string RedirectUri)> _codes = new();
    private readonly byte[] _signingKey;

    public OAuthService(IOptionsMonitor<SingleUserOAuthOptions> options, ILogger<OAuthService> logger)
    {
        var configured = options.Get(Constants.AuthenticationScheme).TokenSigningKey;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            _signingKey = Encoding.UTF8.GetBytes(configured);
        }
        else
        {
            _signingKey = RandomNumberGenerator.GetBytes(32);
            logger.LogWarning(
                "No TokenSigningKey configured — generated a random key. " +
                "Tokens will be invalidated on every restart. " +
                "Set Authentication:Schemes:SingleUserOAuth:TokenSigningKey to persist tokens across restarts.");
        }
    }

    public string CreateAuthCode(string codeChallenge, string method, string redirectUri)
    {
        var code = GenerateRandom();
        _codes[code] = (codeChallenge, method, redirectUri);
        return code;
    }

    public string? ExchangeCode(string code, string codeVerifier, string redirectUri)
    {
        if (!_codes.Remove(code, out var entry)) return null;
        if (entry.RedirectUri != redirectUri) return null;
        if (!VerifyPkce(codeVerifier, entry.CodeChallenge, entry.Method)) return null;

        return CreateToken(TimeSpan.FromHours(1));
    }

    public bool ValidateToken(string token)
    {
        // token = base64url(expiry-ticks) + "." + base64url(hmac)
        var dot = token.IndexOf('.');
        if (dot < 0) return false;

        var payloadPart = token[..dot];
        var sigPart = token[(dot + 1)..];

        var expectedSig = Sign(payloadPart);
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(Base64UrlToBase64(sigPart)),
                Convert.FromBase64String(Base64UrlToBase64(expectedSig))))
            return false;

        var ticks = BitConverter.ToInt64(Convert.FromBase64String(Base64UrlToBase64(payloadPart)));
        return new DateTime(ticks, DateTimeKind.Utc) > DateTime.UtcNow;
    }

    private string CreateToken(TimeSpan lifetime)
    {
        var expiry = DateTime.UtcNow.Add(lifetime).Ticks;
        var payload = Base64UrlEncode(BitConverter.GetBytes(expiry));
        var sig = Sign(payload);
        return $"{payload}.{sig}";
    }

    private string Sign(string payload)
    {
        var mac = HMACSHA256.HashData(_signingKey, Encoding.ASCII.GetBytes(payload));
        return Base64UrlEncode(mac);
    }

    private static bool VerifyPkce(string verifier, string challenge, string method)
    {
        if (method.Equals("S256", StringComparison.OrdinalIgnoreCase))
        {
            var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
            return Base64UrlEncode(hash) == challenge;
        }
        return verifier == challenge; // plain
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Base64UrlToBase64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return (s.Length % 4) switch
        {
            2 => s + "==",
            3 => s + "=",
            _ => s
        };
    }

    private static string GenerateRandom() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
