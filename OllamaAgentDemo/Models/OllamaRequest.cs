namespace OllamaAgentDemo.Models;

public class OllamaRequest
{
    public required string Model { get; set; }
    public required List<ChatMessage> Messages { get; set; }
    public bool Stream { get; set; } = false; // 預設不串流，簡化處理
}