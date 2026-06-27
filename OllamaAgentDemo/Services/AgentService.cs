using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OllamaAgentDemo.Models;
using OllamaAgentDemo.Tools;

namespace OllamaAgentDemo.Services;

public class AgentService : IAgentService
{
    private readonly IOllamaService _ollamaService;
    private readonly Dictionary<string, ITool> _tools;

    public AgentService(IOllamaService ollamaService, IEnumerable<ITool> tools)
    {
        _ollamaService = ollamaService;
        _tools = tools.ToDictionary(t => t.Name);
    }

    public async Task RunConversationAsync(List<ChatMessage> history, string model)
    {
        bool toolCallFound = true;

        while (toolCallFound)
        {
            var response = await _ollamaService.ChatAsync(model, history);
            if (response == null || response.Message == null) break;

            history.Add(response.Message);

            // 檢查最後一則訊息是否包含 tool call
            // 預期格式: "ToolName: argument"
            var lastMessage = response.Message.Content;
            var match = Regex.Match(lastMessage, @"^(\w+):\s*(.*)$", RegexOptions.Multiline);

            if (match.Success)
            {
                string toolName = match.Groups[1].Value;
                string argument = match.Groups[2].Value;

                if (_tools.TryGetValue(toolName, out var tool))
                {
                    Console.WriteLine($"[Agent] 正在呼叫工具: {toolName} (參數: {argument})");
                    string result = await tool.ExecuteAsync(argument);
                    
                    // 將工具結果以使用者身份加入歷史紀錄，以便模型下次處理
                    history.Add(new ChatMessage { Role = "user", Content = result });
                }
                else
                {
                    Console.WriteLine($"[Agent] 找不到指定的工具: {toolName}");
                    history.Add(new ChatMessage { Role = "user", Content = $"Error: Tool '{toolName}' not found." });
                }
            }
            else
            {
                // 沒有發現 tool call，結束迴圈
                toolCallFound = false;
            }
        }
    }
}