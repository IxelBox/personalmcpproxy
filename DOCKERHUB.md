# mcp-proxy-server

MCP proxy server with OAuth2 authentication (Authorization Code + PKCE), exposing [Black Forest Labs FLUX](https://api.bfl.ai) image generation as MCP tools.

## Quick start

```bash
docker run -p 5000:5000 \
  -e Authentication__Schemes__SingleUserOAuth__UserName=alice \
  -e Authentication__Schemes__SingleUserOAuth__Password=secret \
  -e BlackForestLabs__ApiKey=your-bfl-api-key \
  ixelbox/mcp-proxy-server:latest
```

Connect your MCP client to `http://localhost:5000`.

## Environment variables

| Variable | Required | Description |
| --- | --- | --- |
| `Authentication__Schemes__SingleUserOAuth__UserName` | Yes | Login username |
| `Authentication__Schemes__SingleUserOAuth__Password` | Yes | Login password |
| `BlackForestLabs__ApiKey` | Yes | BFL API key — get one at [api.bfl.ai](https://api.bfl.ai) |
| `McpServer__Port` | No | HTTP port (default: `5000`) |

## Docker Compose

```yaml
services:
  mcp-proxy:
    image: ixelbox/mcp-proxy-server:latest
    ports:
      - "5000:5000"
    environment:
      Authentication__Schemes__SingleUserOAuth__UserName: alice
      Authentication__Schemes__SingleUserOAuth__Password: secret
      BlackForestLabs__ApiKey: your-bfl-api-key
    restart: unless-stopped
```

## Source

[github.com/ixelbox/personalmcpproxy](https://github.com/ixelbox/personalmcpproxy)
