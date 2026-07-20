using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OllamaAgentDemo.Models;
using OllamaAgentDemo.Tools;

namespace OllamaAgentDemo.Services;

/// <summary>
/// 優化的 Agent 服務 - 改進工具調用循環和任務處理效率。
/// 任務流程: 用戶輸入 → 任務拆解 → 工具呼叫 → 自然語言結果
/// </summary>
public class OptimizedAgentService
{
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, ITool> _tools;
    private readonly string _ollamaFullUrl;
    private readonly int _maxIterations;

    /// <summary>
    /// 初始化 OptimizedAgentService。
    /// </summary>
    /// <param name="httpClient">HTTP 客戶端</param>
    /// <param name="ollamaFullUrl">Ollama API 完整 URL</param>
    /// <param name="tools">工具列表</param>
    /// <param name="maxIterations">最大迭代次數</param>
    public OptimizedAgentService(
        HttpClient httpClient,
        string ollamaFullUrl,
        IEnumerable<ITool> tools,
        int maxIterations = 30)
    {
        _httpClient = httpClient;
        _ollamaFullUrl = ollamaFullUrl;
        _maxIterations = maxIterations;

        var dict = new Dictionary<string, ITool>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in tools)
        {
            if (!dict.ContainsKey(tool.Name))
            {
                dict.Add(tool.Name, tool);
            }
        }
        _tools = dict;
    }

    /// <summary>
    /// 執行優化的工具調用循環。
    /// </summary>
    /// <param name="history">對話歷史</param>
    /// <param name="model">模型名稱</param>
    /// <returns>最終回答</returns>
    public async Task<string> RunConversationAsync(List<ChatMessage> history, string model)
    {
        bool needMoreProcessing = true;
        int iterationCount = 0;

        while (needMoreProcessing && iterationCount < _maxIterations)
        {
            iterationCount++;

            // 1. 建構包含完整工具 schema 的請求
            var request = BuildRequest(model, history);

            // 2. 發送請求並獲取回應
            (needMoreProcessing, string? finalAnswer) = await ProcessResponseAsync(request, history);

            // 3. 如果獲得最終回答，返回結果
            if (!needMoreProcessing && !string.IsNullOrEmpty(finalAnswer))
            {
                return finalAnswer;
            }
        }

        // 達到最大迭代次數警告
        return $"\n[警告] 達到最大迭代次數 ({_maxIterations})，任務可能未完成。";
    }

    /// <summary>
    /// 建構 Ollama API 請求，包含完整的工具定義和 schema。
    /// </summary>
    private object BuildRequest(string model, List<ChatMessage> history)
    {
        var toolDefinitions = _tools.Values.Select(t => BuildToolDefinition(t)).ToArray();

        return new
        {
            model = model,
            messages = history.Select(msg => new
            {
                role = msg.Role,
                content = msg.Content,
                tool_calls = msg.ToolCalls?.Count > 0 ? msg.ToolCalls.Select(tc => new
                {
                    id = tc.Id,
                    type = "function",
                    function = new
                    {
                        name = tc.FunctionName,
                        arguments = ParseToolCallArguments(tc.FunctionArguments)
                    }
                }).ToArray() : null,
                tool_call_id = msg.ToolCallId
            }).ToArray(),
            stream = false,
            tools = toolDefinitions,
            options = new
            {
                temperature = 0.1,
                num_predict = 4096,
                repeat_penalty = 1.1
            }
        };
    }

    /// <summary>
    /// 建構工具定義，包含正確的 JSON Schema。
    /// </summary>
    private static object BuildToolDefinition(ITool tool)
    {
        var schema = GetToolSchema(tool.Name);

        return new
        {
            type = "function",
            function = new
            {
                name = tool.Name,
                description = tool.Description,
                parameters = schema
            }
        };
    }

    /// <summary>
    /// 獲取工具的 JSON Schema，使 LLM 能正確理解參數格式。
    /// </summary>
    private static Dictionary<string, object> GetToolSchema(string toolName)
    {
        return toolName.ToLower() switch
        {
            "calculator" => new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["expression"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["description"] = "The mathematical expression to evaluate (e.g., '2+3*4' or '100/5')"
                    }
                },
                ["required"] = new[] { "expression" }
            },
            "stock_info" => new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["symbol"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["description"] = "股票代號 (e.g., '2330' for TSMC)"
                    },
                    ["market"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["description"] = "市場類型: tse (上市), otc (上櫃)"
                    }
                },
                ["required"] = new[] { "symbol" }
            },
            _ => new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>(),
                ["required"] = new string[0]
            }
        };
    }

    /// <summary>
    /// 處理 Ollama API 回應並執行工具調用循環。
    /// </summary>
    private async Task<(bool needMoreProcessing, string? finalAnswer)> ProcessResponseAsync(
        object request,
        List<ChatMessage> history)
    {
        var jsonContent = JsonSerializer.Serialize(request);

        // 發送 HTTP 請求
        var httpResponse = await _httpClient.PostAsync(
            _ollamaFullUrl,
            new StringContent(jsonContent, Encoding.UTF8, "application/json"));

        if (!httpResponse.IsSuccessStatusCode)
        {
            var errorMsg = $"Error from Ollama API: {httpResponse.StatusCode}";
            Console.WriteLine($"[錯誤] {errorMsg}");
            history.Add(new ChatMessage { Role = "system", Content = errorMsg });
            return (false, errorMsg);
        }

        var responseJson = await httpResponse.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        string assistantContent = root.GetProperty("message").GetProperty("content").GetString() ?? "";

        // 檢查是否有結構化的 tool_calls
        if (root.GetProperty("message").TryGetProperty("tool_calls", out var toolCallsProp) &&
            toolCallsProp.ValueKind == JsonValueKind.Array && toolCallsProp.GetArrayLength() > 0)
        {
            int toolCallCount = toolCallsProp.GetArrayLength();
            Console.WriteLine($"[Agent] 使用工具: {toolCallCount} 個");

            var toolCalls = new List<ToolCall>();
            foreach (var tc in toolCallsProp.EnumerateArray())
            {
                var fn = tc.GetProperty("function");
                var functionName = fn.GetProperty("name").GetString() ?? "";
                var functionArgsRaw = fn.GetProperty("arguments").GetRawText();

                Console.WriteLine($"[Agent] 呼叫工具: {functionName}({ExtractToolParams(functionName, functionArgsRaw)})");

                toolCalls.Add(new ToolCall
                {
                    Id = tc.GetProperty("id").GetString() ?? "",
                    FunctionName = functionName,
                    FunctionArguments = functionArgsRaw
                });
            }

            // 將助手訊息加入歷史
            history.Add(new ChatMessage
            {
                Role = "assistant",
                Content = assistantContent,
                ToolCalls = toolCalls
            });

            // 執行工具調用
            await ExecuteToolCallsAsync(toolCalls, history);

            return (true, null);
        }
        else
        {
            // 檢查是否是文字格式的工具調用 (backwards compatibility)
            var toolCallMatch = Regex.Match(assistantContent.Trim(), @"^([a-zA-Z_][a-zA-Z0-9_]*):\s*(.+)$");
            if (toolCallMatch != null && _tools.ContainsKey(toolCallMatch.Groups[1].Value))
            {
                var toolName = toolCallMatch.Groups[1].Value;
                var toolArgs = toolCallMatch.Groups[2].Value;

                Console.WriteLine($"[Agent] 使用工具: {toolName}({toolArgs})");

                var toolCalls = new List<ToolCall>
                {
                    new ToolCall
                    {
                        Id = $"wc_{Guid.NewGuid().ToString("N")[..8]}",
                        FunctionName = toolName,
                        FunctionArguments = BuildToolArguments(toolName, toolArgs)
                    }
                };

                history.Add(new ChatMessage
                {
                    Role = "assistant",
                    Content = assistantContent
                });

                await ExecuteToolCallsAsync(toolCalls, history);
                return (true, null);
            }
            else
            {
                // 這是最終回答
                history.Add(new ChatMessage { Role = "assistant", Content = assistantContent });
                return (false, assistantContent);
            }
        }
    }

    /// <summary>
    /// 從工具參數中提取關鍵參數值。
    /// </summary>
    private static string ExtractToolParams(string toolName, string argumentsRaw)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsRaw);
            var root = doc.RootElement;

            if (toolName.ToLower() == "stock_info")
            {
                if (root.TryGetProperty("symbol", out var symbolProp))
                {
                    return $"\"{symbolProp.GetString()}\"";
                }
            }
            else if (toolName.ToLower() == "calculator")
            {
                if (root.TryGetProperty("expression", out var exprProp))
                {
                    return $"\"{exprProp.GetString()}\"";
                }
            }
        }
        catch
        {
            // 忽略解析錯誤
        }
        return argumentsRaw.Length > 50 ? argumentsRaw.Substring(0, 50) + "..." : argumentsRaw;
    }

    /// <summary>
    /// 執行工具調用並處理結果。
    /// </summary>
    private async Task ExecuteToolCallsAsync(
        List<ToolCall> toolCalls,
        List<ChatMessage> history)
    {
        foreach (var tc in toolCalls)
        {
            Console.WriteLine($"[Agent] 正在執行: {tc.FunctionName}...");

            if (_tools.TryGetValue(tc.FunctionName, out var tool))
            {
                try
                {
                    string result;

                    // 增強參數解析邏輯
                    if (string.IsNullOrWhiteSpace(tc.FunctionArguments) ||
                        tc.FunctionArguments.Trim() == "{}")
                    {
                        var fallbackArgs = BuildToolArguments(tc.FunctionName, "");
                        result = await tool.ExecuteAsync(fallbackArgs);
                    }
                    else if (tc.FunctionArguments.TrimStart().StartsWith("{"))
                    {
                        result = await tool.ExecuteAsync(tc.FunctionArguments);
                    }
                    else
                    {
                        var wrappedArgs = BuildToolArguments(tc.FunctionName, tc.FunctionArguments);
                        result = await tool.ExecuteAsync(wrappedArgs);
                    }

                    // 將工具結果以 tool role 送回給模型
                    history.Add(new ChatMessage
                    {
                        Role = "tool",
                        Content = result,
                        ToolCallId = tc.Id
                    });

                    Console.WriteLine($"[Agent] 完成: {tc.FunctionName}");
                }
                catch (Exception ex)
                {
                    var errorMsg = $"Error executing tool '{tc.FunctionName}': {ex.Message}";
                    Console.WriteLine($"[錯誤] {errorMsg}");
                    history.Add(new ChatMessage
                    {
                        Role = "tool",
                        Content = errorMsg,
                        ToolCallId = tc.Id
                    });
                }
            }
            else
            {
                var errorMsg = $"Error: Tool '{tc.FunctionName}' not found.";
                Console.WriteLine($"[錯誤] {errorMsg}");
                history.Add(new ChatMessage
                {
                    Role = "tool",
                    Content = errorMsg,
                    ToolCallId = tc.Id
                });
            }
        }
    }

    /// <summary>
    /// 根據工具名稱建構正確的參數 JSON。
    /// </summary>
    private static string BuildToolArguments(string toolName, string argumentValue)
    {
        if (string.IsNullOrWhiteSpace(argumentValue))
            argumentValue = "";

        var paramName = GetToolParameterMap(toolName);
        var escapedValue = argumentValue
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");

        return $"{{\"{paramName}\": \"{escapedValue}\"}}";
    }

    /// <summary>
    /// 獲取工具參數映射。
    /// </summary>
    private static string GetToolParameterMap(string toolName)
    {
        return toolName.ToLower() switch
        {
            "calculator" => "expression",
            "stock_info" => "symbol",
            _ => "input"
        };
    }

    /// <summary>
    /// 解析工具調用參數。
    /// </summary>
    private static object? ParseToolCallArguments(string argumentsRaw)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentsRaw);
        }
        catch
        {
            return null;
        }
    }
}