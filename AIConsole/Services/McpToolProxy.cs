using System.Text.Json;
using AIConsole.Models;

namespace AIConsole.Services;

public class McpToolProxy : ITool
{
    private readonly ToolDefinition _definition;
    private readonly McpClientService _mcpClient;

    public McpToolProxy(ToolDefinition definition, McpClientService mcpClient)
    {
        _definition = definition;
        _mcpClient = mcpClient;
    }

    public string Name => _definition.Name;
    public string Description => _definition.Description;

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> arguments)
    {
        var response = await _mcpClient.CallToolAsync(Name, arguments);
        var text = response switch
        {
            JsonElement element => element.GetRawText(),
            _ => response?.ToString() ?? string.Empty
        };

        return new ToolResult
        {
            Name = Name,
            Content = text
        };
    }
}
