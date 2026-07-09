using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace OllamaAgentDemo.Tools;

/// <summary>
/// MCP Server 工具代理，將請求轉發至指定的 MCP 伺服器端點。
/// </summary>
public class McpTool : ITool
{
    private readonly HttpClient _httpClient;
    private readonly string _serverUrl;
    private readonly string _toolId;
    private readonly string _displayName;

    public McpTool(HttpClient httpClient, string serverUrl, string toolId, string? displayName = null)
    {
        _httpClient = httpClient;
        _serverUrl = serverUrl.TrimEnd('/');
        _toolId = toolId;
        _displayName = displayName ?? toolId;
    }

    public string Name => _displayName;

    public async Task<string> ExecuteAsync(string input)
    {
        try
        {
            // 根據 McpServer 的 StockInfoTool 實作，它預期 Input 為一個包含 "symbol" 屬性的 JSON 物件
            // 而 Agent 給予的 input 是單純的股票代碼字串 (例如 "2330")
            object requestInput = input;
            if (_toolId == "stock_info")
            {
                requestInput = new { symbol = input };
            }

            var requestBody = new
            {
                ToolId = _toolId,
                Input = requestInput,
                User = "OllamaAgentUser"
            };

            var response = await _httpClient.PostAsJsonAsync($"{_serverUrl}/mcp/call", requestBody);

            if (!response.IsSuccessStatusCode)
            {
                return $"Error calling MCP tool {_toolId}: {response.StatusCode}";
            }

            var resultResponse = await response.Content.ReadFromJsonAsync<McpCallResponse>();
            
            if (resultResponse == null)
            {
                return "Error: Received null response from MCP server.";
            }

            if (!resultResponse.Success)
            {
                return $"MCP Tool Error: {resultResponse.Error ?? "Unknown error occurred during tool execution."}";
            }

            if (resultResponse.Result == null)
            {
                return "MCP Tool executed successfully but returned no result.";
            }

            // 將 Result 物件序列化為 JSON 字串回傳給 Agent
            return JsonSerializer.Serialize(resultResponse.Result, new JsonSerializerOptions { WriteIndented = false });
        }
        catch (Exception ex)
        {
            return $"Error executing MCP tool {_toolId}: {ex.Message}";
        }
    }
}

public class McpCallResponse 
{
    public bool Success { get; set; }
    public string? ToolId { get; set; }
    public object? Result { get; set; }
    public string? Error { get; set; }
    public string? AuditId { get; set; }
    public string? Details { get; set; }
}