using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Mcp.Proxy.Server.Tools;

internal class ServerInfoTools
{
    [McpServerTool]
    [Description("Returns the current server version.")]
    public string GetServerVersion() => BuildInfo.Version;
}
