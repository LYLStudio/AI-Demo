using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OllamaAgentDemo.Models;
using OllamaAgentDemo.Tools;

namespace OllamaAgentDemo.Services;

/// <summary>
/// Agent 服務 - 管理 AI Agent 的推理與工具調用循環。
/// </summary>
public class AgentService : IAgentService
{
    private readonly string _ollamaFullUrl;
    private readonly Dictionary<string, ITool> _tools;

    public AgentService(
        object? ollamaService, 
        string ollamaFullUrl, 
        IEnumerable<ITool> tools)
    {
        _ollamaFullUrl = ollamaFullUrl;
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
    /// 執行工具調用循環。
    /// </summary>
    public async Task RunConversationAsync(List<ChatMessage> history, string model)
    {
        // 使用內建的 Ollama API 呼叫
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(2);

        bool needMoreProcessing = true;
        int maxIterations = 20; // 防止無限循環
        int iterationCount = 0;

        while (needMoreProcessing && iterationCount < maxIterations)
        {
            iterationCount++;

            // 建構 Ollama API 請求格式（包含 tools 定義以啟用工具調用）
            var toolDefinitions = _tools.Values.Select(t => new
            {
                type = "function",
                function = new
                {
                    name = t.Name,
                    description = t is McpTool mcpTool ? mcpTool.Description : $"工具 {t.Name}",
                    parameters = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>(),
                        required = new string[0]
                    }
                }
            }).ToArray();

            var request = new
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
                            arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(tc.FunctionArguments)
                        }
                    }).ToArray() : null,
                    tool_call_id = msg.ToolCallId
                }).ToArray(),
                stream = false,
                tools = toolDefinitions,
                options = new
                {
                    temperature = 0.1,  // 降低溫度以提高工具調用的準確性
                    num_predict = 2048
                }
            };

            var jsonContent = JsonSerializer.Serialize(request);
            
            Console.WriteLine($"[Agent] 迭代 #{iterationCount}，發送請求到 Ollama...");
            Console.WriteLine($"[Agent] 訊息數量: {history.Count}");
            Console.WriteLine($"[Agent] 請求 JSON (前100字): {jsonContent.Substring(0, Math.Min(100, jsonContent.Length))}...");

            var httpResponse = await httpClient.PostAsync(
                _ollamaFullUrl,
                new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json"));

            if (!httpResponse.IsSuccessStatusCode)
            {
                var errorMsg = $"Error from Ollama API: {httpResponse.StatusCode}";
                Console.WriteLine($"[Agent] {errorMsg}");
                history.Add(new ChatMessage { Role = "system", Content = errorMsg });
                needMoreProcessing = false;
                break;
            }

            var responseJson = await httpResponse.Content.ReadAsStringAsync();
            Console.WriteLine($"[Agent] 回應 JSON (前200字): {responseJson.Substring(0, Math.Min(200, responseJson.Length))}");
            
            // 解析回應
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;
            
            string assistantContent = root.GetProperty("message").GetProperty("content").GetString() ?? "";
            Console.WriteLine($"[Agent] 助手內容: {assistantContent}");
            
            // 檢查是否有 tool_calls
            var messageObj = new ChatMessage { Content = assistantContent };
            
            if (root.GetProperty("message").TryGetProperty("tool_calls", out var toolCallsProp) && 
                toolCallsProp.ValueKind == JsonValueKind.Array && toolCallsProp.GetArrayLength() > 0)
            {
                Console.WriteLine($"[Agent] 發現 {toolCallsProp.GetArrayLength()} 個 tool calls");
                
                // 模型返回了結構化的 tool_calls
                var toolCalls = new List<ToolCall>();
                foreach (var tc in toolCallsProp.EnumerateArray())
                {
                    var fn = tc.GetProperty("function");
                    var functionName = fn.GetProperty("name").GetString() ?? "";
                    var functionArgsRaw = fn.GetProperty("arguments").GetRawText();
                    
                    Console.WriteLine($"[Agent] 工具調用: {functionName}，參數: {functionArgsRaw}");
                    
                    toolCalls.Add(new ToolCall
                    {
                        Id = tc.GetProperty("id").GetString() ?? "",
                        FunctionName = functionName,
                        FunctionArguments = functionArgsRaw
                    });
                }
                messageObj.ToolCalls = toolCalls;
                
                // 將帶有 tool_calls 的助手訊息加入歷史紀錄
                history.Add(messageObj);
                
                // 處理工具調用
                needMoreProcessing = await ProcessToolCallsAsync(toolCalls, history);
            }
            else
            {
                Console.WriteLine($"[Agent] 沒有 tool calls，視為最終回答");
                
                // 沒有工具調用，檢查是否是工具調用格式
                var toolCallMatch = Regex.Match(assistantContent.Trim(), @"^([a-zA-Z_]+):\s*(.+)$");
                if (toolCallMatch != null && _tools.ContainsKey(toolCallMatch.Groups[1].Value))
                {
                    // 這是文字格式的工具調用（當 Ollama 沒有使用結構化工具調用時）
                    var toolName = toolCallMatch.Groups[1].Value;
                    var toolArgs = toolCallMatch.Groups[2].Value;
                    
                    Console.WriteLine($"[Agent] 檢測到文字格式工具調用: {toolName} ({toolArgs})");
                    
                    var toolCalls = new List<ToolCall>
                    {
                        new ToolCall
                        {
                            Id = $"wc_{iterationCount}",
                            FunctionName = toolName,
                            FunctionArguments = $"{{\"input\": \"{toolArgs}\"}}"
                        }
                    };
                    
                    history.Add(new ChatMessage 
                    { 
                        Role = "assistant", 
                        Content = assistantContent 
                    });
                    
                    needMoreProcessing = await ProcessToolCallsAsync(toolCalls, history);
                }
                else
                {
                    // 沒有工具調用，這是最終回答
                    Console.WriteLine($"[Agent] 最終回答: {assistantContent}");
                    history.Add(new ChatMessage 
                    { 
                        Role = "assistant", 
                        Content = assistantContent 
                    });
                    needMoreProcessing = false;
                }
            }
        }

        if (iterationCount >= maxIterations)
        {
            Console.WriteLine($"[Agent] 達到最大迭代次數 ({maxIterations})，停止工具調用循環。");
        }
    }

    /// <summary>
    /// 處理工具調用並返回是否還有更多工具調用。
    /// </summary>
    private async Task<bool> ProcessToolCallsAsync(List<ToolCall> toolCalls, List<ChatMessage> history)
    {
        foreach (var tc in toolCalls)
        {
            Console.WriteLine($"[Agent] 正在執行工具: {tc.FunctionName} (參數: {tc.FunctionArguments})");

            if (_tools.TryGetValue(tc.FunctionName, out var tool))
            {
                try
                {
                    string result = await tool.ExecuteAsync(tc.FunctionArguments);

                    // 將工具結果以 tool_call_id 送回給模型
                    history.Add(new ChatMessage 
                    { 
                        Role = "tool", 
                        Content = result,
                        ToolCallId = tc.Id
                    });
                    
                    Console.WriteLine($"[Agent] 工具 '{tc.FunctionName}' 返回結果 (長度: {result.Length} 字元)");
                }
                catch (Exception ex)
                {
                    var errorMsg = $"Error executing tool '{tc.FunctionName}': {ex.Message}";
                    Console.WriteLine($"[Agent] {errorMsg}");
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
                var errorMsg = $"Error: Tool '{tc.FunctionName}' not found. Available tools: {string.Join(", ", _tools.Keys)}";
                Console.WriteLine($"[Agent] {errorMsg}");
                history.Add(new ChatMessage 
                { 
                    Role = "tool", 
                    Content = errorMsg,
                    ToolCallId = tc.Id
                });
            }
        }

        // 工具調用後需要再次呼叫模型來生成最終回答
        return true;
    }
}

/// <summary>
/// Agent 服務介面。
/// </summary>
public interface IAgentService
{
    Task RunConversationAsync(List<ChatMessage> history, string model);
}