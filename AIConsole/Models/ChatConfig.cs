namespace AIConsole.Models;

public class ChatConfig
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string ModelName { get; set; } = "gpt-oss:20b";//"gemma4:26b-mlx";
    public bool ShowThinking { get; set; } = true;
}