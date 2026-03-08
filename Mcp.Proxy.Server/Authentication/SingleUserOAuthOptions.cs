using System;
using Microsoft.AspNetCore.Authentication;

namespace Mcp.Proxy.Server.Authentication;

/// <summary>
/// Configuration options for single-user OAuth authentication.
/// </summary>
public class SingleUserOAuthOptions : AuthenticationSchemeOptions
{
    /// <summary>The username credential.</summary>
    public string? UserName { get; set; }

    /// <summary>The password credential.</summary>
    public string? Password { get; set; }

    /// <summary>
    /// Secret used to sign bearer tokens (HMAC-SHA256). Tokens survive server restarts
    /// as long as this key stays the same. If omitted, a random key is generated on
    /// startup — tokens will be invalidated on every restart.
    /// </summary>
    public string? TokenSigningKey { get; set; }
}
