using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using OllamaAgentDemo.Models;
using OllamaAgentDemo.Services;
using OllamaAgentDemo.Tools;

namespace OllamaAgentDemo;

class Program
{
    static async Task Main(string[] args)
    {
        // 1. 載入設定檔
        var configuration = BuildConfiguration();

        // 從設定檔讀取參數（禁止 hardcode，必須由設定檔或環境變數提供）
        string? modelName = configuration["Agent:Model"];
        string? mcpServerUrl = configuration["McpServer:BaseUrl"];
        string? ollamaFullUrl = configuration["Ollama:FullUrl"];

        // 驗證必要設定是否存在
        if (string.IsNullOrEmpty(modelName))
        {
            Console.Error.WriteLine("錯誤：缺少 'Agent:Model' 設定。請在 appsettings.json 或環境變數中提供。");
            Environment.Exit(1);
        }

        if (string.IsNullOrEmpty(mcpServerUrl))
        {
            Console.Error.WriteLine("錯誤：缺少 'McpServer:BaseUrl' 設定。請在 appsettings.json 或環境變數中提供。");
            Environment.Exit(1);
        }

        if (string.IsNullOrEmpty(ollamaFullUrl))
        {
            Console.Error.WriteLine("錯誤：缺少 'Ollama:FullUrl' 設定。請在 appsettings.json 或環境變數中提供。");
            Environment.Exit(1);
        }

        Console.WriteLine("=== C# AI Agent Demo (MCP Protocol Reconstructed) ===");
        Console.WriteLine($"模型: {modelName}");
        Console.WriteLine($"MCP Server: {mcpServerUrl}");
        Console.WriteLine($"Ollama API: {ollamaFullUrl}");
        Console.WriteLine("輸入 'exit' 或 'quit' 來結束對話。");

        // 2. 初始化依賴項 (Dependency Injection 風格)
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(2);

        // 3. 創建 MCP Client 並完成初始化握手
        var mcpClient = new McpClient(httpClient, mcpServerUrl);
        
        try
        {
            // 執行 MCP 初始化流程
            await mcpClient.InitializeAsync();
            Console.WriteLine("[OK] MCP session initialized successfully.");

            // 使用標準 tools/list 方法獲取工具清單
            var toolDescriptors = await mcpClient.ListToolsAsync();
            
            if (toolDescriptors.Count == 0)
            {
                Console.WriteLine("警告：從 MCP Server 發現 0 個工具。");
            }
            else
            {
                Console.WriteLine($"[OK] Discovered {toolDescriptors.Count} tools from MCP Server via 'tools/list' method:");
                foreach (var desc in toolDescriptors)
                {
                    Console.WriteLine($"   - {desc.Name}: {desc.Description}");
                }
            }

            // 4. 根據工具清單建立 McpTool 實例
            var tools = new List<ITool>();
            foreach (var descriptor in toolDescriptors)
            {
                tools.Add(new McpTool(mcpClient, descriptor));
            }

            var agentService = new AgentService(ollamaService: null, ollamaFullUrl: ollamaFullUrl, tools: tools);

            // 5. 初始化對話紀錄 (System Prompt，動態根據工具清單產生)
            var toolDescriptions = string.Join("\n", tools.Select(t => $"- {t.Name}: {GetToolDescription(t)}"));
            var history = new List<ChatMessage>
            {
                new ChatMessage 
                { 
                    Role = "system", 
                    Content = $@"你是一個專業的 AI Agent。
當你需要使用工具來獲取資訊或進行運算時，你必須嚴格遵守以下規則：

1. 輸出格式必須且僅限於：[ToolName]: [argument] (例如: Calculator: 10 + 10)
2. 在你收到工具的回傳結果之前，絕對不要在回覆中寫出任何答案或解釋。

目前你可以使用的工具：
{toolDescriptions}

當收到工具的回傳結果後，請根據該結果給出最終的自然語言回答。" 
                }
            };

            // 6. 主迴圈
            while (true)
            {
                Console.Write("\n[User]: ");
                string userInput = Console.ReadLine() ?? "";

                if (string.IsNullOrWhiteSpace(userInput)) continue;

                if (userInput.ToLower() == "exit" || userInput.ToLower() == "quit")
                {
                    Console.WriteLine("=== 任務結束 ===");
                    break;
                }

                // 將使用者輸入加入紀錄
                history.Add(new ChatMessage { Role = "user", Content = userInput });

                // 使用 Agent Service 執行推理與工具調用循環
                await agentService.RunConversationAsync(history, modelName);

                // 印出最後一則訊息 (AI 的最終回答)
                if (history.Count > 0)
                {
                    var lastMessage = history[^1];
                    if (lastMessage.Role == "assistant")
                    {
                        Console.WriteLine($"\n[AI]: {lastMessage.Content}");
                    }
                }
            }
        }
        finally
        {
            mcpClient.Dispose();
        }
    }

    /// <summary>
    /// 獲取工具描述的輔助方法。
    /// </summary>
    private static string GetToolDescription(ITool tool)
    {
        if (tool is McpTool mcpTool)
        {
            return mcpTool.Description;
        }
        return "";
    }

    /// <summary>
    /// 建構配置物件，支援 appsettings.json 和環境變數。
    /// </summary>
    private static IConfiguration BuildConfiguration()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{GetEnvironmentName()}.json", optional: true)
            .AddEnvironmentVariables();

        return builder.Build();
    }

    /// <summary>
    /// 獲取當前環境名稱。
    /// </summary>
    private static string GetEnvironmentName()
    {
        return Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") 
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") 
            ?? "Development";
    }
}