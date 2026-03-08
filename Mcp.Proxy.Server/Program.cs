using Mcp.Proxy.Server;
using Mcp.Proxy.Server.Authentication;
using Mcp.Proxy.Server.Tools;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

var apiKey = builder.Configuration["BlackForestLabs:ApiKey"]
    ?? throw new InvalidOperationException("BlackForestLabs:ApiKey is not configured.");
builder.Services.AddHttpClient<BlackforestLabWrapper>(client =>
{
    client.BaseAddress = new Uri("https://api.bfl.ai");
    client.DefaultRequestHeaders.Add("x-key", apiKey);
});

builder.Services.AddSingleton<OAuthService>();
builder.Services.AddAuthentication(Constants.AuthenticationScheme)
    .AddScheme<SingleUserOAuthOptions, SingleUserOAuthHandler>(Constants.AuthenticationScheme, null);

// bind credentials from appsettings.json "Authentication:Schemes:SingleUserOAuth"
var authSection = builder.Configuration.GetSection($"Authentication:Schemes:{Constants.AuthenticationScheme}");
builder.Services.Configure<SingleUserOAuthOptions>(Constants.AuthenticationScheme, opts =>
{
    opts.UserName = authSection["UserName"];
    opts.Password = authSection["Password"];
    opts.TokenSigningKey = authSection["TokenSigningKey"];
});

builder.Services.AddAuthorization();

// read our custom section from configuration
var mcpConfig = builder.Configuration.GetSection("McpServer");
// transport can be "Http" (default) or "Console"
var transport = mcpConfig.GetValue<string>("Transport")?.ToLowerInvariant() ?? "http";
var port = mcpConfig.GetValue<int?>("Port");

// Add the MCP services: the transport to use (http or console) and the tools to register.
var services = builder.Services.AddMcpServer().WithTools<RandomNumberTools>().WithTools<BlackforestLabWrapper>();

if (transport == "console")
{
    services.WithStdioServerTransport();
}
else
{
    // default to HTTP transport
    services.WithHttpTransport();

    // bind to 0.0.0.0 so the reverse proxy inside Docker can reach the container
    if (port.HasValue)
    {
        builder.WebHost.UseUrls($"http://0.0.0.0:{port.Value}");
    }
}

// trust X-Forwarded-For / X-Forwarded-Proto from the reverse proxy (nginx/traefik)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();  // trust all proxies on the container network
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();  // must be first — reads proxy headers before other middleware

app.UseAuthentication();
app.UseAuthorization();

app.MapOAuthEndpoints();
app.MapMcp().RequireAuthorization();

app.Run();
