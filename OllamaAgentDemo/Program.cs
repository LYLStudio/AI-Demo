using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Data; // 用於 DataTable.Compute

namespace OllamaAgentDemo
{
    // 1. 定義對話內容的結構 (Message 物件)
    public class ChatMessage
    {
        public required string Role { get; set; }
        public required string Content { get; set; }
    }

    // 2. 定義 Ollama API 回應的完整結構
    public class OllamaResponse
    {
        public required string Model { get; set; }
        public required ChatMessage Message { get; set; }
        // 可以加入這個來觀察模型的思考過程
        public string? Thinking { get; set; }
        public bool Done { get; set; }
    }

    // 3. 定義發送給 Ollama 的請求結構
    public class OllamaRequest
    {
        public required string Model { get; set; }
        public required List<ChatMessage> Messages { get; set; }
        public bool Stream { get; set; } = false; // 預設不串流，簡化處理
    }

    class Program
    {
        private static readonly HttpClient client = new HttpClient();
        private const string OllamaUrl = "http://localhost:11434/api/chat";
        private const string ModelName = "gemma4:26b-mlx";

        static async Task Main(string[] args)
        {
            Console.WriteLine("=== C# AI Agent Demo (Calculator Tool) ===");
            Console.WriteLine("輸入 'exit' 或 'quit' 來結束對話。");

            // 初始化包含 System Prompt 的歷史紀錄
            var history = new List<ChatMessage>
            {
                new ChatMessage { Role = "system", Content = @"你是一個專業的 AI Agent。
當你需要使用工具來獲取資訊或進行運算時，你必須嚴格遵守以下規則：

1. 輸出格式必須且僅限於：[ACTION: 工具名稱 | INPUT: 參數]
2. 在你收到工具的回傳結果之前，絕對不要在回覆中寫出任何答案或解釋。
3. 不要重複輸出你預期會得到的數字。

目前你可以使用的工具：
- Calculator (用於數學運算，輸入格式為簡單的算式，如 10 * 10)
- StockInfo (用於查詢個股資訊，輸入格式為股票代號組合，例如 tse_2330.tw)

當收到工具的回傳結果後，請根據該結果給出最終的自然語言回答。" }
            };

            while (true)
            {
                Console.Write("\n[User]: ");
                string userInput = $"{Console.ReadLine()}";

                if (string.IsNullOrWhiteSpace(userInput)) continue;

                if (userInput.ToLower() == "exit" || userInput.ToLower() == "quit")
                {
                    Console.WriteLine("=== 任務結束 ===");
                    break;
                }

                // 將使用者的輸入加入歷史紀錄
                history.Add(new ChatMessage { Role = "user", Content = userInput });

                // 開始 Agent 的推理與工具調用循環
                await RunAgentLoop(history);
            }
        }

        static async Task RunAgentLoop(List<ChatMessage> history)
        {
            bool toolUsed = true;

            while (toolUsed)
            {
                var requestBody = new OllamaRequest { Model = ModelName, Messages = history };
                var jsonPayload = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // 1. 發送請求
                var response = await client.PostAsync(OllamaUrl, content);

                // --- 除錯檢查 1: HTTP 狀態碼 ---
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[Error] API 回應失敗，狀態碼: {response.StatusCode}");
                    return;
                }

                var responseString = await response.Content.ReadAsStringAsync();

                // --- 除錯檢查 2: 印出原始 JSON (非常重要！) ---
                // Console.WriteLine($"\n[Debug] Ollama 回傳的原始內容: {responseString}");

                // 2. 反序列化
                var ollamaResult = JsonSerializer.Deserialize<OllamaResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                // --- 除錯檢查 3: 檢查解序列化後的物件 ---
                if (ollamaResult == null)
                {
                    Console.WriteLine("[Error] 解序列化失敗：ollamaResult 為空。");
                    return;
                }
                if (ollamaResult.Message == null)
                {
                    Console.WriteLine("[Error] 解序列化失敗：ollamaResult.message 為空。請檢查 JSON 結構是否包含 'message' 物件。");
                    return;
                }

                string aiMessage = ollamaResult.Message.Content;
                // 我們不直接印出完整的 AI 回應，因為如果有工具調用，它可能只是 [ACTION: ...]
                // 如果沒有工具調用，它就是最終答案。
                // 為了保持對話流暢，我們可以在確定不是工具調用時才印出。

                // 更新對話紀錄
                history.Add(new ChatMessage { Role = "assistant", Content = aiMessage });

                // 檢查是否包含工具調用
                var match = Regex.Match(aiMessage, @"\[ACTION:\s*(?<name>\w+)\s*\|\s*INPUT:\s*(?<input>.*?)\]");

                if (match.Success)
                {
                    string toolName = match.Groups["name"].Value;
                    string toolInput = match.Groups["input"].Value;

                    Console.WriteLine($"\n[System]: 偵測到工具 $\rightarrow$ {toolName}({toolInput})");

                    string result = "";
                    if (toolName == "Calculator")
                    {
                        result = ExecuteCalculator(toolInput);
                    }
                    else if (toolName == "StockInfo")
                    {
                        result = await ExecuteStockInfo(toolInput);
                    }

                    Console.WriteLine($"[System]: 工具回傳結果 $\rightarrow$ {result}");
                    history.Add(new ChatMessage { Role = "user", Content = $"Tool Result: {result}" });
                }
                else
                {
                    // 如果沒有匹配到工具調用，表示這是 AI 的最終回答
                    Console.WriteLine($"\n[AI]: {aiMessage}");
                    toolUsed = false;
                }
            }
        }


        static string ExecuteCalculator(string expression)
        {
            try
            {
                // 使用 DataTable 來計算字串表達式 (簡單的 Demo 用法)
                var dt = new DataTable();
                var result = dt.Compute(expression, "");
                return $"{result}";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        static async Task<string> ExecuteStockInfo(string stockIdentifier)
        {
            try
            {
                string url = $"https://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch={stockIdentifier}";
                return await client.GetStringAsync(url);
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }
    }
}
