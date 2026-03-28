# personalmcpproxy

A personal MCP server with single-user OAuth2 authentication (Authorization Code + PKCE), built with ASP.NET Core and published as a Native AOT binary.

## MCP Tools

### Black Forest Labs — image generation

| Tool | Description |
| --- | --- |
| `GenerateImage` | Text-to-image using any FLUX model. Supports `width`/`height` (FLUX.2, multiples of 16, up to 4MP) and `aspect_ratio` (Kontext, e.g. `"16:9"`). |
| `EditImage` | Image-to-image editing via `input_image`. Works with FLUX.2 and Kontext models. |
| `FillImage` | Masked inpainting using FLUX Fill (`flux-pro-1.0-fill`). White mask = fill, black = keep. |
| `GetBflCredits` | Returns the current credit balance for the configured API key. |

All image tools share these output parameters:

| Parameter | Default | Description |
| --- | --- | --- |
| `returnHtmlTag` | `true` | Wrap the result in an `<img>` tag |
| `returnBase64` | `false` | Return image data inline instead of a server URL |
| `cacheKey` | — | Optional string key. On first call the result is stored under this key; subsequent calls with the same key return instantly from cache without hitting the BFL API. |

Output mode matrix:

| `returnHtmlTag` | `returnBase64` | Result |
| --- | --- | --- |
| `true` | `false` | `<img src="https://host/images/{id}" />` |
| `false` | `false` | `https://host/images/{id}` |
| `true` | `true` | `<img src="data:image/jpeg;base64,..." />` |
| `false` | `true` | raw base64 string |

Images served via URL are cached server-side and expire after `ImageTtlMinutes`. When `returnBase64` is true and no `cacheKey` is set, the image is not stored on the server at all.

### Utility

| Tool | Description |
| --- | --- |
| `GetServerVersion` | Returns the running server version. |
| `GetRandomNumber` | Returns a random number in a given range. |

## Quick start

### Local development

**1. Set credentials via user secrets** (keeps secrets out of source control):

```bash
dotnet user-secrets --project Mcp.Proxy.Server set "Authentication:Schemes:SingleUserOAuth:UserName" "alice"
dotnet user-secrets --project Mcp.Proxy.Server set "Authentication:Schemes:SingleUserOAuth:Password" "secret"
dotnet user-secrets --project Mcp.Proxy.Server set "BlackForestLabs:ApiKey" "your-bfl-api-key"
```

**2. Run:**

```bash
dotnet run --project Mcp.Proxy.Server
```

**3. Connect** your MCP client to `http://localhost:5000`.

For token stability across restarts, also set `TokenSigningKey`:

```bash
dotnet user-secrets --project Mcp.Proxy.Server set "Authentication:Schemes:SingleUserOAuth:TokenSigningKey" "$(openssl rand -hex 32)"
```

### Docker

**Build:**

```bash
docker build -t personalmcpproxy:latest .
```

**Run:**

```bash
docker run -d \
  --name personalmcpproxy \
  --restart unless-stopped \
  -p 5000:5000 \
  -e Authentication__Schemes__SingleUserOAuth__UserName=alice \
  -e Authentication__Schemes__SingleUserOAuth__Password=secret \
  -e Authentication__Schemes__SingleUserOAuth__TokenSigningKey=your-signing-key \
  -e BlackForestLabs__ApiKey=your-bfl-api-key \
  personalmcpproxy:latest
```

**Behind a reverse proxy** — the server trusts `X-Forwarded-For` / `X-Forwarded-Proto` headers automatically. Nginx example:

```nginx
server {
    listen 443 ssl;
    server_name mcp.example.com;

    location / {
        proxy_pass         http://personalmcpproxy:5000;
        proxy_http_version 1.1;
        proxy_set_header   Host              $host;
        proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
        proxy_read_timeout 300s;  # generation can take up to ~5 minutes
    }
}
```

> Set `proxy_read_timeout` to at least 300 seconds — image generation polls the BFL API for up to 280 seconds.

## Authentication

The server uses OAuth2 Authorization Code flow with PKCE. When an MCP client connects, it will:

1. Fetch `/.well-known/oauth-authorization-server` for discovery
2. Redirect you to `/authorize` — log in with your username and password
3. Exchange the auth code for a Bearer token at `/token`
4. Use the token on all subsequent MCP requests

## Configuration reference

Settings can be provided via `appsettings.json`, environment variables (use `__` instead of `:` as separator), or user secrets.

| Setting | Env variable | Default | Description |
| --- | --- | --- | --- |
| `McpServer:Transport` | `McpServer__Transport` | `Http` | `Http` or `Console` (stdio) |
| `McpServer:Port` | `McpServer__Port` | — | HTTP port override |
| `McpServer:ImageTtlMinutes` | `McpServer__ImageTtlMinutes` | `60` | How long generated images are cached server-side (minutes) |
| `Authentication:Schemes:SingleUserOAuth:UserName` | `Authentication__Schemes__SingleUserOAuth__UserName` | — | Login username |
| `Authentication:Schemes:SingleUserOAuth:Password` | `Authentication__Schemes__SingleUserOAuth__Password` | — | Login password |
| `Authentication:Schemes:SingleUserOAuth:TokenSigningKey` | `Authentication__Schemes__SingleUserOAuth__TokenSigningKey` | random | Secret for signing bearer tokens. Set this to keep tokens valid across restarts. |
| `BlackForestLabs:ApiKey` | `BlackForestLabs__ApiKey` | — | BFL API key — get one at [api.bfl.ai](https://api.bfl.ai) |

### appsettings.json example

```json
{
  "McpServer": {
    "Port": 5000,
    "ImageTtlMinutes": 60
  },
  "Authentication": {
    "Schemes": {
      "SingleUserOAuth": {
        "UserName": "alice",
        "Password": "secret",
        "TokenSigningKey": "your-signing-key"
      }
    }
  },
  "BlackForestLabs": {
    "ApiKey": "your-bfl-api-key"
  }
}
```

## Testing

```bash
dotnet test
```

Live smoke tests hit the real BFL API and are skipped unless `MCPPROXYAPP_BFL_API_KEY` is set:

```powershell
# PowerShell
$env:MCPPROXYAPP_BFL_API_KEY="your-key"; dotnet test --filter "BflApiSmokeTest"
```

```bash
# Bash
MCPPROXYAPP_BFL_API_KEY="your-key" dotnet test --filter "BflApiSmokeTest"
```
