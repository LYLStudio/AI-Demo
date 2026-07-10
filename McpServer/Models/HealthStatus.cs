namespace McpServer.Models;

/// <summary>
/// 表示系統健康檢查結果。
/// </summary>
public class HealthStatus
{
    public string Status { get; set; } = "ok";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public Dictionary<string, object?> Components { get; set; } = new();
}
