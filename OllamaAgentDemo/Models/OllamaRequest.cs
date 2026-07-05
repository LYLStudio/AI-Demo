using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OllamaAgentDemo.Models;

/* public class OllamaRequest
{
    public required string Model { get; set; }
    public required List<ChatMessage> Messages { get; set; }
    public bool Stream { get; set; } = false; // 預設不串流，簡化處理
}
 */

public class OllamaRequest
{
    /// <summary>Model name, e.g. "gpt-oss:20b"</summary>
    [JsonPropertyName("model")]
    public string Model { get; init; } = default!;

    /// <summary>Conversation messages.</summary>
    [JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; init; } = new();

    /// <summary>Stream the output? (default: false)</summary>
    [JsonPropertyName("stream")]
    public bool Stream { get; set; } = false;

    /// <summary>Custom “think” flag – optional.</summary>
    [JsonPropertyName("think")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Think { get; set; }
}


