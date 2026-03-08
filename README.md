# personalmcpproxy

A personal MCP proxy server with single-user OAuth2 authentication (Authorization Code flow + PKCE), built with ASP.NET Core.

## Authentication

The server uses OAuth2 Authorization Code flow with PKCE. When Claude web connects, it will:

1. Fetch `/.well-known/oauth-authorization-server` for discovery
2. Redirect you to `/authorize` — log in with your username and password
3. Exchange the auth code for a Bearer token at `/token`
4. Use the token on all subsequent MCP requests

## Configuration

Settings can be provided via `appsettings.json` or environment variables (use `__` instead of `:` as separator).

| Setting | Env variable | Default | Description |
| --- | --- | --- | --- |
| `McpServer:Transport` | `McpServer__Transport` | `Http` | `Http` or `Console` (stdio) |
| `McpServer:Port` | `McpServer__Port` | — | HTTP port override |
| `Authentication:Schemes:SingleUserOAuth:UserName` | `Authentication__Schemes__SingleUserOAuth__UserName` | — | Login username |
| `Authentication:Schemes:SingleUserOAuth:Password` | `Authentication__Schemes__SingleUserOAuth__Password` | — | Login password |

### appsettings.json example

```json
{
  "McpServer": {
    "Transport": "Http",
    "Port": 5000
  },
  "Authentication": {
    "Schemes": {
      "SingleUserOAuth": {
        "UserName": "alice",
        "Password": "secret"
      }
    }
  }
}
```

### Environment variable example

```bash
McpServer__Transport=Http
McpServer__Port=5000
Authentication__Schemes__SingleUserOAuth__UserName=alice
Authentication__Schemes__SingleUserOAuth__Password=secret
```

## Running locally

```bash
dotnet run --project Mcp.Proxy.Server
```

Connect from Claude web or an MCP client at `http://localhost:5000`.

## Docker

Build and run:

```bash
docker build -t personalmcpproxy:latest ./Mcp.Proxy.Server
docker run -p 5000:5000 \
  -e Authentication__Schemes__SingleUserOAuth__UserName=alice \
  -e Authentication__Schemes__SingleUserOAuth__Password=secret \
  personalmcpproxy:latest
```

### Behind a reverse proxy (Nginx)

```nginx
server {
    listen 80;
    server_name mcp.example.com;

    location / {
        proxy_pass         http://mcp-server:5000;
        proxy_http_version 1.1;
        proxy_set_header   Upgrade $http_upgrade;
        proxy_set_header   Connection keep-alive;
        proxy_set_header   Host $host;
        proxy_cache_bypass $http_upgrade;
    }
}
```

## Testing

```bash
dotnet test
```
