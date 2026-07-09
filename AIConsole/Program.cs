using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIConsole.Models;
using AIConsole.Services;

// 1. Setup Configuration and Services
string configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
var configService = new ConfigService(configFilePath);
var chatConfig = configService.GetConfig();

// Determine if we should show thinking based on command line args
bool isThinkingEnabled = chatConfig.ShowThinking;
string[] cmdArgs = Environment.GetCommandLineArgs();
foreach (var arg in cmdArgs)
{
    if (arg == "--no-think") isThinkingEnabled = false;
}

// 2. Initialize Registry and MCP client
var toolRegistry = new ToolRegistry();
McpClientService? mcpClient = null;
McpServerInfo? serverInfo = null;

var candidateUrls = new[]
{
    chatConfig.McpServerBaseUrl,
    chatConfig.BaseUrl,
    "http://127.0.0.1:5000",
    "http://127.0.0.1:5080",
    "http://localhost:5000",
    "http://localhost:5080"
}
.Where(url => !string.IsNullOrWhiteSpace(url))
.Select(url => url!.TrimEnd('/'))
.Distinct(StringComparer.OrdinalIgnoreCase)
.ToList();

foreach (var candidateUrl in candidateUrls)
{
    try
    {
        var candidateClient = new McpClientService(candidateUrl);
        serverInfo = await candidateClient.InitializeAsync();
        mcpClient = candidateClient;
        Console.WriteLine($"[MCP] Connected to {serverInfo?.Server ?? "McpServer"} v{serverInfo?.Version ?? "unknown"} at {candidateUrl}");
        break;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[MCP] Unable to connect to {candidateUrl}: {ex.Message}");
    }
}

if (mcpClient is null || serverInfo is null)
{
    throw new InvalidOperationException("Unable to connect to McpServer. Please start the McpServer project and verify the URL.");
}

var tools = await mcpClient.ListToolsAsync();
foreach (var tool in tools)
{
    toolRegistry.RegisterTool(new McpToolProxy(tool, mcpClient));
}

// 3. Create Chat Service
using var chatService = new ChatService(chatConfig, toolRegistry);

Console.WriteLine("=== AIConsole Agent Mode ===");
Console.WriteLine("輸入 'exit' 或 'quit' 來結束對話。");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    var history = new List<ChatMessage>
    {
        new ChatMessage("system", @"你是一個專業的 AI Agent。請始終使用繁體中文回答。
當你需要使用工具來獲取資訊或進行運算時，你必須嚴格遵循以下邏輯：
1. **思考優先**：首先在 <thinking> 標記中分析使用者目標、目前已知資訊以及缺少的資訊，並規劃明確的步驟。
2. **工具驅動**：若需要外部資訊或執行特定功能，請呼叫對應的 MCP 工具。
3. **禁止搶答**：在收到工具回傳結果之前，絕對不要嘗試給出最終答案或結論。即使你認為知道答案，也必須先透過工具驗證（如果適用）。
4. **迭代推理**：收到工具結果後，將其視為新資訊，重新進入 <thinking> 階段分析是否已達成目標。若仍需更多資訊，請繼續調用工具；直到所有必要資訊均已齊全且邏輯自洽後，才輸出最終答案。")
    };

    while (true)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("\n[您的問題]: ");
        Console.ResetColor();
        
        var userInput = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(userInput)) continue;
        if (userInput.Equals("exit", StringComparison.OrdinalIgnoreCase) || 
            userInput.Equals("quit", StringComparison.OrdinalIgnoreCase)) break;

        history.Add(new ChatMessage("user", userInput));

        bool turnCompleted = false;

        while (!turnCompleted && !cts.IsCancellationRequested)
        {
            ToolCall? capturedToolCall = null;

            await chatService.StreamChatAsync(history, cts.Token, (state, element) => 
            {
                // Check for tool call in the current chunk - check both root and inside 'message'
                JsonElement? tcElement = null;
                if (element.TryGetProperty("tool_call", out var rootTc))
                {
                    tcElement = rootTc;
                }
                else if (element.TryGetProperty("message", out var msg) && msg.TryGetProperty("tool_call", out var msgTc))
                {
                    tcElement = msgTc;
                }

                if (tcElement != null)
                {
                    capturedToolCall = new ToolCall
                    {
                        Name = tcElement.Value.GetProperty("name").GetString() ?? "",
                        Arguments = new Dictionary<string, object>()
                    };
                    if (tcElement.Value.TryGetProperty("arguments", out var argsEl))
                    {
                        foreach(var prop in argsEl.EnumerateObject())
                        {
                            capturedToolCall.Arguments[prop.Name] = prop.Value.ToString(); 
                        }
                    }
                }

                ProcessChunkInternal(state, element, isThinkingEnabled);
            }, () => 
            {
                // Stream ended for this turn
            });

            if (capturedToolCall != null)
            {
                var tool = toolRegistry.GetTool(capturedToolCall.Name);
                if (tool != null)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\n[工具呼叫]: {capturedToolCall.Name}({string.Join(", ", capturedToolCall.Arguments.Select(kv => $"{kv.Key}={kv.Value}"))})");
                    Console.ResetColor();

                    var result = await tool.ExecuteAsync(capturedToolCall.Arguments);
                    
                    // 參考 OllamaAgentDemo，將工具結果以 'user' 身份加入紀錄，這能更有效觸發模型的後續推理與調用
                    history.Add(new ChatMessage("user", $"[工具結果]: {result.Content}"));
                    
                    // The loop continues: AI will now process the tool result and decide next step
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n[Error] Tool '{capturedToolCall.Name}' not found.");
                    Console.ResetColor();
                    // 同樣使用 'user' 角色回報錯誤，確保模型能接收到錯誤訊息並決定如何應對
                    history.Add(new ChatMessage("user", $"[工具錯誤]: 找不到名為 '{capturedToolCall.Name}' 的工具。"));
                }
            }
            else
            {
                // No tool call was captured in this stream, meaning the AI has finished its response
                turnCompleted = true; 
            }
        }
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("\nCancelled.");
}
catch (Exception ex)
{
    Console.WriteLine($"\nAn error occurred: {ex.Message}");
}

void ProcessChunkInternal(StreamState state, JsonElement root, bool isThinkingEnabled)
{
    if (root.TryGetProperty("message", out var message))
    {
        // Handle Thinking
        if (message.TryGetProperty("thinking", out var thinkingEl) && thinkingEl.ValueKind == JsonValueKind.String)
        {
            var thinking = thinkingEl.GetString();
            if (!string.IsNullOrEmpty(thinking))
            {
                if (!state.IsThinkingPrinted)
                {
                    state.IsThinkingPrinted = true;
                    if (isThinkingEnabled)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write("[思考] "); 
                        Console.Write(thinking);
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write("[思考] 思考中...");
                    }
                }
                else if (isThinkingEnabled)
                {
                    Console.Write(thinking);
                }
            }
        }

        // Handle Content
        if (message.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
        {
            var content = contentEl.GetString();
            if (!string.IsNullOrEmpty(content))
            {
                if (state.IsThinkingPrinted)
                {
                    Console.WriteLine(); 
                    state.IsThinkingPrinted = false; 
                }

                if (!state.IsContentPrinted)
                {
                    Console.ForegroundColor = ConsoleColor.Green; 
                    Console.Write("[答案] "); 
                    state.IsContentPrinted = true;
                }
                Console.Write(content);
            }
        }
        Console.ResetColor();
    }
}