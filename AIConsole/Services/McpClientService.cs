using System.Net.Http.Json;
using System.Text.Json;
using AIConsole.Models;

namespace AIConsole.Services;

public class McpClientService
{
    private readonly HttpClient _httpClient;

    public McpClientService(string baseUrl)
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/')) };
    }

    public async Task<McpServerInfo?> InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync("/mcp/initialize", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<McpServerInfo>(cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<ToolDefinition>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("/mcp/tools", cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<List<McpToolDescriptor>>(cancellationToken: cancellationToken);
        return payload?.Select(tool => new ToolDefinition
        {
            Name = tool.Id,
            Description = tool.Description,
            Parameters = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["schema"] = tool.Schema
            }
        }).ToList() ?? new List<ToolDefinition>();
    }

    public async Task<object?> CallToolAsync(string toolId, Dictionary<string, object> arguments, string? user = null, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            toolId,
            input = arguments.ToDictionary(entry => entry.Key, entry => entry.Value),
            user
        };

        using var response = await _httpClient.PostAsJsonAsync("/mcp/call", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
        return doc?.RootElement.Clone();
    }
}

public class McpServerInfo
{
    public string Server { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public Dictionary<string, object?> Metadata { get; set; } = new();
}

public class McpToolDescriptor
{
    public string Id { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object?> Schema { get; set; } = new();
    public List<string> RequiredRoles { get; set; } = new();
}
