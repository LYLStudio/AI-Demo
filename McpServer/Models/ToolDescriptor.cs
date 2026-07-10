namespace McpServer.Models;

/// <summary>
/// 描述一個可供 MCP Server 透過工具介面呼叫的工具。
/// </summary>
public class ToolDescriptor
{
    public string Id { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object?> Schema { get; set; } = new();
    public IList<string> RequiredRoles { get; set; } = new List<string> { "user", "admin" };
}