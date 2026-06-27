namespace OllamaAgentDemo.Models;

public class ChatMessage
{
    public required string Role { get; set; }
    public required string Content { get; set; }
}