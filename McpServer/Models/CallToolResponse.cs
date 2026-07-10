namespace McpServer.Models;

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