using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using OllamaAgentDemo.Tools;

namespace OllamaAgentDemo.Services;

/// <summary>
/// MCP (Model Context Protocol) Client - 遵循 JSON-RPC 2.0 規範與 MCP 規格。
/// 用於與 MCP Server 進行標準化通訊。
/// </summary>
public class McpClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUri;
    private bool _initialized;
    private bool _disposed;
    private int _requestIdCounter;

    /// <summary>
    /// MCP Server 提供的工具清單。
    /// </summary>
    public IReadOnlyDictionary<string, McpToolDescriptor> Tools { get; private set; } = new Dictionary<string, McpToolDescriptor>();

    public McpClient(HttpClient httpClient, string baseUri)
    {
        _httpClient = httpClient;
        _baseUri = baseUri.TrimEnd('/');
    }

    /// <summary>
    /// 初始化 MCP Session - 發送 initialize 請求並完成握手。
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        var initializeRequest = new
        {
            jsonrpc = "2.0",
            id = NextRequestId(),
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                clientInfo = new
                {
                    name = "OllamaAgentDemo",
                    version = "1.0.0"
                },
                capabilities = new { }
            }
        };

        var response = await _httpClient.PostAsJsonAsync(
            $"{_baseUri}/mcp", 
            initializeRequest,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Failed to initialize MCP session. Status: {response.StatusCode}");
        }

        var jsonResponse = await response.Content.ReadFromJsonAsync<McpResponse>();
        if (jsonResponse?.Result == null)
        {
            throw new InvalidOperationException("Invalid initialize response: missing result.");
        }

        _initialized = true;
    }

    /// <summary>
    /// 獲取工具清單 - 使用標準 tools/list 方法。
    /// </summary>
    public async Task<List<McpToolDescriptor>> ListToolsAsync()
    {
        if (!_initialized)
            await InitializeAsync();

        var request = new
        {
            jsonrpc = "2.0",
            id = NextRequestId(),
            method = "tools/list",
            @params = new { }
        };

        var response = await _httpClient.PostAsJsonAsync(
            $"{_baseUri}/mcp",
            request,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Failed to list tools. Status: {response.StatusCode}");
        }

        var responseText = await response.Content.ReadAsStringAsync();
        
        // 使用 JsonElement 手動解析 JSON（避免命名策略問題）
        using var doc = JsonDocument.Parse(responseText);
        var root = doc.RootElement;
        
        // 檢查是否有錯誤
        if (root.TryGetProperty("error", out var errorProp))
        {
            var errorCode = errorProp.GetProperty("code").GetInt32();
            var errorMessage = errorProp.GetProperty("message").GetString() ?? "Unknown error";
            throw new InvalidOperationException($"MCP error ({errorCode}): {errorMessage}");
        }

        // 取得 result 物件
        if (!root.TryGetProperty("result", out var resultProp))
        {
            throw new InvalidOperationException("Invalid tools/list response: missing result.");
        }

        // 從 result 中取得 tools 陣列
        if (!resultProp.TryGetProperty("tools", out var toolsProp) || toolsProp.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Invalid tools/list response: tools array not found.");
        }

        var descriptors = new List<McpToolDescriptor>();
        foreach (var toolElem in toolsProp.EnumerateArray())
        {
            var name = toolElem.GetProperty("name").GetString() ?? string.Empty;
            var description = toolElem.TryGetProperty("description", out var descProp) 
                ? descProp.GetString() ?? string.Empty 
                : string.Empty;
            JsonElement? schemaPropValue = null;
            if (toolElem.TryGetProperty("inputSchema", out var schemaProp))
            {
                schemaPropValue = schemaProp;
            }

            var descriptor = new McpToolDescriptor
            {
                Name = name,
                Description = description,
                InputSchema = schemaPropValue?.GetRawText()
            };
            descriptors.Add(descriptor);
        }

        // 更新內部快取
        var dict = new Dictionary<string, McpToolDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in descriptors)
        {
            dict[d.Name] = d;
        }
        Tools = dict;

        return descriptors;
    }

    /// <summary>
    /// 調用工具 - 使用標準 tools/call 方法。
    /// </summary>
    /// <param name="toolName">工具名稱</param>
    /// <param name="arguments">工具參數（符合 inputSchema）</param>
    public async Task<string> CallToolAsync(string toolName, Dictionary<string, object?> arguments)
    {
        if (!_initialized)
            await InitializeAsync();

        var request = new
        {
            jsonrpc = "2.0",
            id = NextRequestId(),
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments = arguments
            }
        };

        var response = await _httpClient.PostAsJsonAsync(
            $"{_baseUri}/mcp",
            request,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Tool call failed for '{toolName}': {response.StatusCode} - {errorContent}");
        }

        var responseText = await response.Content.ReadAsStringAsync();
        
        // 使用 JsonElement 手動解析 JSON
        using var doc = JsonDocument.Parse(responseText);
        var root = doc.RootElement;
        
        // 檢查是否有錯誤（MCP 標準：工具錯誤不返回 error 欄位，而是返回成功狀態+內容標記）
        if (root.TryGetProperty("error", out var errorProp))
        {
            var errorCodeVal = errorProp.GetProperty("code").GetInt32();
            var errorMessageVal = errorProp.GetProperty("message").GetString() ?? "Unknown error";
            return $"MCP Tool Error ({errorCodeVal}): {errorMessageVal}";
        }

        // 取得 result 物件
        if (!root.TryGetProperty("result", out var resultProp))
        {
            return $"Invalid tools/call response for '{toolName}': missing result.";
        }

        // 解析 MCP content 格式: { content: [{ type: "text", text: "..." }] }
        if (resultProp.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.Array)
        {
            var parts = new StringBuilder();
            foreach (var contentElem in contentProp.EnumerateArray())
            {
                var type = contentElem.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
                var text = contentElem.TryGetProperty("text", out var textProp) ? textProp.GetString() : null;
                if (type == "text" && !string.IsNullOrEmpty(text))
                {
                    // 解碼 base64|...|base64 格式的內容（MCP Server 使用的自訂編碼）
                    text = DecodeBase64Content(text);
                    
                    // 解碼 JSON escape sequences (\u00xx -> Unicode characters)
                    text = DecodeJsonEscapedText(text);
                    
                    if (parts.Length > 0)
                        parts.AppendLine();
                    parts.Append(text);
                }
            }
            return parts.ToString();
        }

        // fallback: 直接序列化整個 result
        var jsonStr = resultProp.GetRawText();
        return jsonStr;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        // HttpClient should not be disposed here as it's owned by the caller
    }

    private int NextRequestId()
    {
        return Interlocked.Increment(ref _requestIdCounter);
    }

    private static string? GetStringProperty(object obj, string propertyName)
    {
        var prop = obj.GetType().GetProperty(propertyName);
        if (prop?.GetValue(obj) is string value)
            return value;
        return null;
    }

    private static object? GetSchemaObject(object obj, string propertyName)
    {
        var prop = obj.GetType().GetProperty(propertyName);
        return prop?.GetValue(obj);
    }
    
    /// <summary>
    /// 解碼 base64|...|base64 格式的內容。
    /// MCP Server 使用這種格式來編碼非 ASCII 字符。
    /// </summary>
    private static string DecodeBase64Content(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        
        // 遞歸解碼所有 base64|...|base64 模式
        var result = text;
        var maxIterations = 10;
        var iteration = 0;
        
        while (result.Contains("|base64|") && iteration < maxIterations)
        {
            iteration++;
            var startIndex = result.IndexOf("|base64|", StringComparison.Ordinal);
            if (startIndex < 0)
                break;
            
            var beforeBase64 = result.Substring(0, startIndex + 9); // include "|base64|"
            var afterBase64Start = startIndex + 9;
            
            var base64Start = afterBase64Start;
            var base64End = result.IndexOf("|base64", afterBase64Start, StringComparison.Ordinal);
            if (base64End < 0)
                break;
            
            var base64Content = result.Substring(base64Start, base64End - base64Start);
            
            try
            {
                var decodedBytes = Convert.FromBase64String(base64Content);
                var decodedText = System.Text.Encoding.UTF8.GetString(decodedBytes);
                result = beforeBase64 + decodedText + result.Substring(base64End);
            }
            catch
            {
                // 如果解碼失敗，跳出迴圈
                break;
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// 解碼 JSON escape sequences (\u00xx -> actual Unicode characters)。
    /// </summary>
    private static string DecodeJsonEscapedText(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("\\u"))
            return text;
        
        // 使用 System.Text.Json 來反序列化 JSON 字串以正確處理 escape sequences
        try
        {
            // 將文本包裝成 JSON 字串
            var jsonStr = "\"" + text.Replace("\\", "\\\\")
                                     .Replace("\"", "\\\"")
                                     .Replace("\n", "\\n")
                                     .Replace("\r", "\\r")
                                     .Replace("\t", "\\t") + "\"";
            
            // 重新解析 \u00xx sequences
            using var doc = JsonDocument.Parse(jsonStr);
            return doc.RootElement.GetString() ?? text;
        }
        catch
        {
            return text;
        }
    }
}

/// <summary>
/// MCP 工具描述符（符合 MCP 規範的 inputSchema 格式）。
/// </summary>
public class McpToolDescriptor
{
    /// <summary>
    /// 工具名稱（唯一識別符）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 工具描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 工具輸入的 JSON Schema
    /// </summary>
    public object? InputSchema { get; set; }
}

/// <summary>
/// MCP JSON-RPC 2.0 響應模型。
/// </summary>
public class McpResponse
{
    [JsonPropertyName("jsonrpc")]
    public string? JsonRpc { get; set; }

    [JsonPropertyName("id")]
    public object? Id { get; set; }

    [JsonPropertyName("result")]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    public McpError? Error { get; set; }
}

/// <summary>
/// MCP JSON-RPC 2.0 錯誤模型。
/// </summary>
public class McpError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}