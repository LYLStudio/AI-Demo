using System.Text.Json;

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

/// <summary>
/// 表示工具執行後的回應結果。
/// </summary>
public class CallToolResponse
{
    public bool Success { get; set; }
    public string? ToolId { get; set; }
    public object? Result { get; set; }
    public string? Error { get; set; }
    public string? AuditId { get; set; }
    public string? Details { get; set; }
}

/// <summary>
/// 表示一個已分塊、可供 RAG 檢索的文件片段。
/// </summary>
public class DocChunk
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Source { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public Dictionary<string, object?> Metadata { get; set; } = new();
    public List<float> Embedding { get; set; } = new();
}

/// <summary>
/// 表示系統健康檢查結果。
/// </summary>
public class HealthStatus
{
    public string Status { get; set; } = "ok";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public Dictionary<string, object?> Components { get; set; } = new();
}
