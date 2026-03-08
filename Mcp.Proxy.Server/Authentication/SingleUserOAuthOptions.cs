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
}
