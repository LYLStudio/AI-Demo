using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using OllamaAgentDemo.Models;
using OllamaAgentDemo.Services;
using OllamaAgentDemo.Tools;

namespace OllamaAgentDemo;

class Program
{
    private const string ModelName = "gemma4:26b-mlx"; // 根據環境設定可能需要調整

    static async Task Main(string[] args)
    {
        Console.WriteLine("=== C# AI Agent Demo (Refactored) ===");
        Console.WriteLine("輸入 'exit' 或 'quit' 來結束對話。");

        // 1. 初始化依賴項 (Dependency Injection 風格)
        using var httpClient = new HttpClient();
        string ollamaUrl = "http://localhost:11434/api/chat";

        var ollamaService = new OllamaService(httpClient, ollamaUrl);
        
        // 註冊所有工具
        var tools = new List<ITool>
        {
            new CalculatorTool(),
            new StockInfoTool(httpClient)
        };

        var agentService = new AgentService(ollamaService, tools);

        // 2. 初始化對話紀錄 (System Prompt)
        var history = new List<ChatMessage>
        {
            new ChatMessage { Role = "system", Content = @"你是一個專業的 AI Agent。
當你需要使用工具來獲取資訊或進行運算時，你必須嚴格遵守以下規則：

1. 輸出格式必須且僅限於：[ToolName]: [argument] (例如: Calculator: 10 + 10)
2. 在你收到工具的回傳結果之前，絕對不要在回覆中寫出任何答案或解釋。

目前你可以使用的工具：
- Calculator (用於數學運算)
- StockInfo (用於查詢個股資訊，輸入格式為股票代號組合，例如 tse_2330.tw)

當收到工具的回傳結果後，請根據該結果給出最終的自然語言回答。" }
        };

        // 3. 主迴圈
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
            await agentService.RunConversationAsync(history, ModelName);

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
}