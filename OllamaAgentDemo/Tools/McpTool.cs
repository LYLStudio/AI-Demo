using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace OllamaAgentDemo.Tools;

/// <summary>
/// MCP Server 工具代理，從 MCP Server API 動態取得工具定義並將請求轉發至指定的 MCP 伺服器端點。
/// </summary>
public class McpTool : ITool
{
  private readonly HttpClient _httpClient;
  private readonly string _serverUrl;
  private readonly string _toolId;
  private readonly string _displayName;
  private readonly ToolSchema? _schema;

  public McpTool(HttpClient httpClient, string serverUrl, string toolId, string displayName, ToolSchema? schema = null)
  {
    _httpClient = httpClient;
    _serverUrl = serverUrl.TrimEnd('/');
    _toolId = toolId;
    _displayName = displayName;
    _schema = schema;
  }

  public string Name => _displayName;

  public async Task<string> ExecuteAsync(string input)
  {
    try
    {
      // 根據 schema 動態建構輸入物件
      object requestInput = BuildRequestInputFromSchema(input);

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
      var resultSerialized = JsonSerializer.Serialize(resultResponse.Result, new JsonSerializerOptions { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
      var resultSerializedDecoded = Base64Restorer.Restore(resultSerialized); // 將 Base64 編碼的內容還原為 UTF-8 字元  
      return resultSerializedDecoded;
    }
    catch (Exception ex)
    {
      return $"Error executing MCP tool {_toolId}: {ex.Message}";
    }
  }

  /// <summary>
  /// 根據 schema 動態建構請求輸入物件。
  /// 使用 schema 中的第一個 required 屬性作為輸入欄位名稱。
  /// </summary>
  private object BuildRequestInputFromSchema(string input)
  {
    if (_schema != null && _schema.Properties != null && _schema.Required != null && _schema.Required.Count > 0)
    {
      // 取得第一個 required 的屬性名稱作為欄位
      var fieldName = _schema.Required[0];

      // 使用 Dictionary 來動態建構輸入
      return new Dictionary<string, string> { [fieldName] = input };
    }

    // 如果沒有 schema，預設使用 "input" 作為欄位名稱
    return new { input };
  }
}

/// <summary>
/// MCP Server 工具列表 API 的回應模型。
/// </summary>
public class ToolDescriptor
{
  [JsonPropertyName("id")]
  public string Id { get; set; } = string.Empty;

  [JsonPropertyName("description")]
  public string Description { get; set; } = string.Empty;

  [JsonPropertyName("schema")]
  public ToolSchema? Schema { get; set; }

  [JsonPropertyName("requiredRoles")]
  public List<string>? RequiredRoles { get; set; }
}

/// <summary>
/// 工具 schema 定義，用於描述工具輸入的結構。
/// </summary>
public class ToolSchema
{
  [JsonPropertyName("type")]
  public string? Type { get; set; }

  [JsonPropertyName("properties")]
  public Dictionary<string, PropertySchema>? Properties { get; set; }

  [JsonPropertyName("required")]
  public List<string>? Required { get; set; }
}

/// <summary>
/// 屬性 schema 定義。
/// </summary>
public class PropertySchema
{
  [JsonPropertyName("type")]
  public string? Type { get; set; }

  [JsonPropertyName("description")]
  public string? Description { get; set; }

  [JsonPropertyName("required")]
  public bool? Required { get; set; }
}

public class McpCallResponse
{
  [JsonPropertyName("success")]
  public bool Success { get; set; }

  [JsonPropertyName("toolId")]
  public string? ToolId { get; set; }

  [JsonPropertyName("result")]
  public object? Result { get; set; }

  [JsonPropertyName("error")]
  public string? Error { get; set; }

  [JsonPropertyName("auditId")]
  public string? AuditId { get; set; }

  [JsonPropertyName("details")]
  public string? Details { get; set; }
}

/// <summary>
/// 工具名稱轉換輔助類別，將工具 ID 自動轉換為友好的顯示名稱。
/// </summary>
public static class ToolNameHelper
{
  /// <summary>
  /// 將工具 ID（如 "stock_info"、"read_file"）轉換為友好的顯示名稱（如 "StockInfo"、"ReadFile"）。
  /// </summary>
  public static string ToDisplayName(string toolId)
  {
    if (string.IsNullOrWhiteSpace(toolId))
    {
      return string.Empty;
    }

    // 將 snake_case 轉換為 PascalCase
    var parts = toolId.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
    var result = new StringBuilder();
    foreach (var part in parts)
    {
      if (part.Length > 0)
      {
        result.Append(char.ToUpper(part[0]));
        if (part.Length > 1)
        {
          result.Append(part.Substring(1));
        }
      }
    }
    return result.ToString();
  }
}