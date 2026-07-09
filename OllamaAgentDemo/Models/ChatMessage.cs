using System.Text.Json.Serialization;

namespace OllamaAgentDemo.Models;

public class ChatMessage
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }        // "assistant" / "user" ...

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    // 這個欄位只會在「思考」階段出現
    [JsonPropertyName("thinking")]
    public string? Thinking { get; set; }

    // 當模型需要呼叫工具時，會出現此陣列
    [JsonPropertyName("tool_calls")]
    public List<ToolCall>? ToolCalls { get; set; }
}