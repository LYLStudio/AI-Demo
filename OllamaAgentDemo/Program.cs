using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using OllamaAgentDemo.Models;
using OllamaAgentDemo.Services;
using OllamaAgentDemo.Tools;

namespace OllamaAgentDemo;

/// <summary>
/// 程式進入點 - 使用優化的 Agent Service 執行工具調用循環。
/// </summary>
public class Program
{
    private static IConfiguration _configuration = null!;
    private static string _modelName = null!;
    private static string _mcpServerUrl = null!;
    private static string _ollamaBaseUrl = null!;
    private static int _agentMaxIterations = 30;
    private static double _agentTemperature = 0.1;
    private static int _agentNumPredict = 4096;
    private static double _agentTimeoutMinutes = 3;

    public static async Task Main(string[] args)
    {
        // 1. 載入設定檔
        _configuration = BuildConfiguration();

        // 從設定檔讀取參數（禁止 hardcode）
        _modelName = GetRequiredSetting("Agent:Model");
        _mcpServerUrl = GetRequiredSetting("McpServer:BaseUrl");
        _ollamaBaseUrl = GetRequiredSetting("Ollama:BaseUrl");
        _agentMaxIterations = GetIntSetting("Agent:MaxIterations", 30);
        _agentTemperature = GetDoubleSetting("Agent:Temperature", 0.1);
        _agentNumPredict = GetIntSetting("Agent:NumPredict", 4096);
        _agentTimeoutMinutes = GetDoubleSetting("Agent:TimeoutMinutes", 3);

        string ollamaFullUrl = $"{_ollamaBaseUrl.TrimEnd('/')}/api/chat";

        Console.WriteLine("============================================================");
        Console.WriteLine("       Ollama Agent Demo - 優化工具鏈循環 v2.0");
        Console.WriteLine("============================================================");
        Console.WriteLine($"模型: {_modelName}");
        Console.WriteLine($"MCP Server: {_mcpServerUrl}");
        Console.WriteLine($"Ollama API: {ollamaFullUrl}");
        Console.WriteLine("輸入 'exit' 或 'quit' 來結束對話。");
        Console.WriteLine();

        // 2. 初始化依賴項
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(_agentTimeoutMinutes);

        // 3. 創建 MCP Client 並完成初始化握手
        var mcpClient = new McpClient(httpClient, _mcpServerUrl);

        try
        {
             // 執行 MCP 初始化流程
            await mcpClient.InitializeAsync();
            Console.WriteLine("[OK] MCP session initialized.");

             // 使用標準 tools/list 方法獲取工具清單
            var toolDescriptors = await mcpClient.ListToolsAsync();

            if (toolDescriptors.Count == 0)
             {
                Console.WriteLine("警告：從 MCP Server 發現 0 個工具。");
             }
            else
             {
                Console.WriteLine($"[OK] 發現 {toolDescriptors.Count} 個工具:");
                foreach (var desc in toolDescriptors)
                 {
                    Console.WriteLine($"     - {desc.Name}: {desc.Description}");
                 }
             }

             // 4. 根據工具清單建立 McpTool 實例
            var tools = new List<ITool>();
            foreach (var descriptor in toolDescriptors)
             {
                tools.Add(new McpTool(mcpClient, descriptor));
             }

             // 5. 初始化優化的 Agent Service
            var agentService = new OptimizedAgentService(
                httpClient: httpClient,
                ollamaFullUrl: ollamaFullUrl,
                tools: tools,
                maxIterations: _agentMaxIterations
             );

             // 6. 動態生成 System Prompt，包含工具使用指南
            var toolDescriptions = string.Join("\n", tools.Select(t =>
                 $"    - [{t.Name}]: {t.Description}"));

            var systemPrompt = $@"你是一個專業的 AI Agent，擅長使用工具來完成任務。

## 工作流程
當你收到用戶的請求時，請按照以下步驟進行：

1. **理解任務**: 分析用戶的需求，確定需要使用哪些工具。
2. **任務拆解**: 如果任務複雜，請將其拆分為多個子任務。
3. **工具調用**: 遵守以下格式呼叫工具：
    - 結構化格式（推薦）: 直接使用 tool_calls
    - 文字格式: `工具名稱: 參數`
4. **結果整合**: 根據工具返回的結果，給出完整的自然語言回答。

## 工具使用規範
- 在你收到工具的回傳結果之前，絕對不要在回覆中寫出任何答案或解釋。
- 每次只調用一個工具，等待結果後再繼續。
- 如果工具執行失敗，請分析錯誤並嘗試修正參數重新調用。

## 可用的工具
{toolDescriptions}

## 輸出格式要求
- 使用結構化方式呈現結果（列表、表格、公式等）
- 提供具體的計算過程或數據來源
- 給出清晰易懂的自然語言描述";

            var history = new List<ChatMessage>
             {
                new ChatMessage { Role = "system", Content = systemPrompt }
             };

             // 7. 主迴圈
            while (true)
             {
                Console.Write("\n[User]: ");
                string userInput = Console.ReadLine() ?? "";

                if (string.IsNullOrWhiteSpace(userInput)) continue;

                if (userInput.ToLower() == "exit" || userInput.ToLower() == "quit")
                 {
                    Console.WriteLine("\n=== 任務結束 ===");
                    Console.WriteLine("感謝使用 Ollama Agent Demo v2.0!");
                    break;
                 }

                 // 將使用者輸入加入紀錄
                history.Add(new ChatMessage { Role = "user", Content = userInput });

                Console.WriteLine();

                 // 使用優化的 Agent Service 執行推理與工具調用循環
                string finalAnswer = await agentService.RunConversationAsync(history, _modelName);

                 // 輸出最終回答
                Console.WriteLine();
                Console.WriteLine("[AI]: " + finalAnswer);
             }
         }
        finally
         {
            mcpClient.Dispose();
         }
     }

     /// <summary>
     /// 獲取必要設定，如果不存在則輸出錯誤並終止程式。
     /// </summary>
    private static string GetRequiredSetting(string key)
     {
        var value = _configuration[key];
        if (string.IsNullOrEmpty(value))
         {
            Console.Error.WriteLine($"錯誤：缺少 '{key}' 設定。請在 appsettings.json 或環境變數中提供。");
            Environment.Exit(1);
         }
        return value;
     }

     /// <summary>
     /// 獲取整數設定，如果不存在則使用預設值。
     /// </summary>
    private static int GetIntSetting(string key, int defaultValue)
     {
        var value = _configuration[key];
        if (string.IsNullOrEmpty(value)) return defaultValue;
        if (int.TryParse(value, out var result)) return result;
        return defaultValue;
     }

     /// <summary>
     /// 獲取浮點數設定，如果不存在則使用預設值。
     /// </summary>
    private static double GetDoubleSetting(string key, double defaultValue)
     {
        var value = _configuration[key];
        if (string.IsNullOrEmpty(value)) return defaultValue;
        if (double.TryParse(value, out var result)) return result;
        return defaultValue;
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