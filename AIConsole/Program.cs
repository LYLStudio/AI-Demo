using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
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

// 2. Initialize Registry and Tools
var toolRegistry = new ToolRegistry();
var operationHandler = new OperationHandler();
toolRegistry.RegisterTool(new CalculatorTool(operationHandler));

// 3. Create Chat Service
using var chatService = new ChatService(chatConfig, toolRegistry);

Console.WriteLine("AIConsole Raw HttpClient streaming demo (think=high)");
Console.WriteLine("Type 'exit' or 'quit' to end the session.");

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
        new ChatMessage("system", "You are a helpful assistant. Please always respond in Traditional Chinese (繁體中文).")
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
                // Check for tool call in the current chunk
                if (element.TryGetProperty("tool_call", out var tc))
                {
                    capturedToolCall = new ToolCall
                    {
                        Name = tc.GetProperty("name").GetString() ?? "",
                        Arguments = new Dictionary<string, object>()
                    };
                    if (tc.TryGetProperty("arguments", out var argsEl))
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
                Console.WriteLine("\n[Stream Ended]");
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
                    history.Add(new ChatMessage("tool", result.Content));
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n[Error] Tool '{capturedToolCall.Name}' not found.");
                    Console.ResetColor();
                    turnCompleted = true; 
                }
            }
            else
            {
                turnCompleted = true; // No tool call, end the turn
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