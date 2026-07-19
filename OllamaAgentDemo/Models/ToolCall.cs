using System.Text.Json.Serialization;

namespace OllamaAgentDemo.Models;

/// <summary>
/// 工具調用模型 - 對應 Ollama API 的 tool_calls 格式。
/// </summary>
public class ToolCall
{
    /// <summary>
    /// 工具調用 ID (用於標識和回傳結果)。
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    /// <summary>
    /// 工具名稱。
    /// </summary>
    [JsonPropertyName("functionName")]
    public required string FunctionName { get; set; }

    /// <summary>
    /// 工具參數 (原始 JSON 字串)。
    /// </summary>
    [JsonPropertyName("functionArguments")]
    public required string FunctionArguments { get; set; }
}

/// <summary>
/// 用於向 AgentService 傳遞工具調用結果的回覆訊息。
/// </summary>
public class ToolResultMessage
{
    /// <summary>
    /// 工具調用 ID (對應原始 ToolCall.Id)。
    /// </summary>
    [JsonPropertyName("toolCallId")]
    public string? ToolCallId { get; set; }

    /// <summary>
    /// 工具執行結果內容。
    /// </summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}