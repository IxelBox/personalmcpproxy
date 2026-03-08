using System.Security.Cryptography;
using System.Text;

namespace Mcp.Proxy.Server.Authentication;

/// <summary>In-memory store for OAuth2 auth codes and bearer tokens (single-user).</summary>
public class OAuthService
{
    private readonly Dictionary<string, (string CodeChallenge, string Method, string RedirectUri)> _codes = new();
    private readonly Dictionary<string, DateTime> _tokens = new();

    public string CreateAuthCode(string codeChallenge, string method, string redirectUri)
    {
        var code = Generate();
        _codes[code] = (codeChallenge, method, redirectUri);
        return code;
    }

    public string? ExchangeCode(string code, string codeVerifier, string redirectUri)
    {
        if (!_codes.Remove(code, out var entry)) return null;
        if (entry.RedirectUri != redirectUri) return null;
        if (!VerifyPkce(codeVerifier, entry.CodeChallenge, entry.Method)) return null;

        var token = Generate();
        _tokens[token] = DateTime.UtcNow.AddHours(1);
        return token;
    }

    public bool ValidateToken(string token) =>
        _tokens.TryGetValue(token, out var expiry) && expiry > DateTime.UtcNow;

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

    private static string Generate() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
