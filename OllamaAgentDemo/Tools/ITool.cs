namespace OllamaAgentDemo.Tools;

public interface ITool
{
    string Name { get; }
    string Description { get; }
    Task<string> ExecuteAsync(string input);
}