using System.Text.Json;

namespace McpServer.Models;

/// <summary>
/// 表示外部呼叫 MCP 工具的請求負載。
/// </summary>
public class CallToolRequest
{
    public string ToolId { get; set; } = string.Empty;
    public JsonElement? Input { get; set; }
    public string? User { get; set; }
    public string? ModelResponse { get; set; }
}