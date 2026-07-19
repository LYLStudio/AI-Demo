using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using OllamaAgentDemo.Services;

namespace OllamaAgentDemo.Tools;

/// <summary>
/// 基於 MCP Client 的工具包裝器，遵循 MCP 規範進行工具調用。
/// </summary>
public class McpTool : ITool
{
    private readonly McpClient _mcpClient;
    private readonly string _toolName;
    private readonly string _displayName;
    private readonly string _description;

    public McpTool(McpClient mcpClient, McpToolDescriptor descriptor)
    {
        _mcpClient = mcpClient;
        _toolName = descriptor.Name;
        _displayName = descriptor.Name;
        _description = descriptor.Description;
    }

    public string Name => _displayName;

    /// <summary>
    /// 獲取工具的 MCP 名稱（用於工具調用）。
    /// </summary>
    public string ToolName => _toolName;

    /// <summary>
    /// 獲取工具的描述。
    /// </summary>
    public string Description => _description;

    /// <summary>
    /// 執行工具調用 - 使用標準 MCP tools/call 協議。
    /// </summary>
    /// <param name="arguments">工具參數字典</param>
    public async Task<string> ExecuteWithArgsAsync(Dictionary<string, object?> arguments)
    {
        try
        {
            return await _mcpClient.CallToolAsync(_toolName, arguments);
        }
        catch (Exception ex)
        {
            return $"Error executing tool '{_toolName}': {ex.Message}";
        }
    }

    /// <summary>
    /// ITool 介面方法 - 接受字串參數並自動根據 schema 轉換。
    /// </summary>
    public async Task<string> ExecuteAsync(string input)
    {
        // 解析輸入參數：支援 JSON 格式或直接字串
        if (input.TrimStart().StartsWith("{"))
        {
            try
            {
                var args = JsonSerializer.Deserialize<Dictionary<string, object?>>(input);
                if (args != null && args.Count > 0)
                {
                    return await ExecuteWithArgsAsync(args);
                }
            }
            catch
            {
                // 如果不是有效 JSON，繼續使用預設邏輯
            }
        }

        // 預設行為：將輸入作為 "input" 參數傳遞
        var defaultArgs = new Dictionary<string, object?> { ["input"] = input };
        return await ExecuteWithArgsAsync(defaultArgs);
    }
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