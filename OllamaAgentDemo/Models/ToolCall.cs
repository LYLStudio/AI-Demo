using System.Text.Json.Serialization;
namespace OllamaAgentDemo.Models;

public class ToolCall
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("function")]
    public required Function CallFunction { get; set; } = new();
}

public class Function
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }  // e.g. "tool"

    [JsonPropertyName("arguments")]
    public required ToolArguments Arguments { get; set; } = new();
}

public class ToolArguments
{
    // 這裡示例只顯示一個參數；若工具有多個，直接在此類中再加屬性即可
    [JsonPropertyName("stock_id")]
    public string? StockId { get; set; }
}