using System.Text.Json.Serialization;

namespace OllamaAgentDemo.Models;

/* public class OllamaResponse
{
    public required string Model { get; set; }
    public required ChatMessage Message { get; set; }
    // 可以加入這個來觀察模型的思考過程
    public string? Thinking { get; set; }
    public bool Done { get; set; }
} */

public class OllamaResponse
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    // 這個值是 ISO‑8601 字串，建議轉成 DateTimeOffset / DateTime
    [JsonPropertyName("created_at")]
    public required DateTime CreatedAt { get; set; }

    [JsonPropertyName("message")]
    public required ChatMessage Message { get; set; } = new();

    [JsonPropertyName("done")]
    public bool Done { get; set; }

    // “stop”, “finish_reason” … 等值都會是字串
    [JsonPropertyName("done_reason")]
    public string? DoneReason { get; set; }

    /* ---------- 下面這些統計欄位都是可選的，若沒有就為 null ----------
     * 這裡以 long、int 來表示數值。 */
    
    [JsonPropertyName("total_duration")]
    public long TotalDuration { get; set; }          // nanoseconds

    [JsonPropertyName("load_duration")]
    public long LoadDuration { get; set; }

    [JsonPropertyName("prompt_eval_count")]
    public int PromptEvalCount { get; set; }

    [JsonPropertyName("prompt_eval_duration")]
    public long PromptEvalDuration { get; set; }

    [JsonPropertyName("eval_count")]
    public int EvalCount { get; set; }

    [JsonPropertyName("eval_duration")]
    public long EvalDuration { get; set; }
}
