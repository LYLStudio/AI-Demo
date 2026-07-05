namespace AIConsole.Models;

public class ChatConfig
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string McpServerBaseUrl { get; set; } = "http://127.0.0.1:5209";
    public string ModelName { get; set; } = "gpt-oss:20b";//"gemma4:26b-mlx";
    public bool ShowThinking { get; set; } = true;
}