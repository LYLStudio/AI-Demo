namespace OllamaAgentDemo.Models;

public class OllamaResponse
{
    public required string Model { get; set; }
    public required ChatMessage Message { get; set; }
    // 可以加入這個來觀察模型的思考過程
    public string? Thinking { get; set; }
    public bool Done { get; set; }
}