using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OllamaAgentDemo.Models;

namespace OllamaAgentDemo.Services;

public class OllamaService : IOllamaService
{
    private readonly HttpClient _httpClient;
    private readonly string _ollamaUrl;

    public OllamaService(HttpClient httpClient, string ollamaUrl)
    {
        _httpClient = httpClient;
        _ollamaUrl = ollamaUrl;
    }

    public async Task<OllamaResponse?> ChatAsync(string model, List<ChatMessage> history)
    {
        //var requestBody = new OllamaRequest { Model = model, Messages = history };

        var requestBody = new OllamaRequest
        {
            Model = model,
            Messages = history,
            Stream = false,
            Think = "low"                // 可選，若不設定會被忽略
        };

        var jsonPayload = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(_ollamaUrl, content);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"API 回應失敗，狀態碼: {response.StatusCode}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<OllamaResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
}