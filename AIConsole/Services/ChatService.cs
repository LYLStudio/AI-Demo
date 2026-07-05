namespace AIConsole.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIConsole.Models;

public class ChatService : IChatService, IChatProvider
{
    private readonly ChatConfig _config;
    private readonly IToolRegistry _toolRegistry;

    public ChatService(ChatConfig config, IToolRegistry toolRegistry)
    {
        _config = config;
        _toolRegistry = toolRegistry;
    }

    public void Dispose()
    {
        // No unmanaged resources to release.
    }

    public async Task StreamChatAsync(List<ChatMessage> history, CancellationToken cancellationToken, Action<StreamState, JsonElement> onChunkReceived, Action onStreamEnded)
    {
        using var http = new HttpClient { BaseAddress = new Uri(_config.BaseUrl) };
        http.Timeout = TimeSpan.FromMinutes(30);

        var historyList = new List<object>();
        foreach (var msg in history)
        {
            historyList.Add(new { role = msg.Role, content = msg.Content });
        }

        var payload = new
        {
            model = _config.ModelName,
            messages = historyList.ToArray(),
            think = "high",
            stream = true
        };

        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var buffer = new StringBuilder();
            var state = new StreamState();

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (line == null) break;
                line = line.TrimEnd('\r', '\n');

                if (line.Length == 0)
                {
                    if (buffer.Length > 0)
                    {
                        ProcessBuffer(buffer, state, onChunkReceived);
                        buffer.Clear();
                    }
                    continue;
                }

                if (line.StartsWith("data:"))
                {
                    var jsonPart = line.Substring(5).Trim();
                    if (jsonPart == "[DONE]") break;
                    ProcessJson(jsonPart, state, onChunkReceived);
                    continue;
                }

                buffer.Append(line);
                ProcessBuffer(buffer, state, onChunkReceived);
            }

            if (buffer.Length > 0) ProcessBuffer(buffer, state, onChunkReceived);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] {ex.Message}");
        }
        finally
        {
            onStreamEnded();
        }
    }

    private void ProcessBuffer(StringBuilder buffer, StreamState state, Action<StreamState, JsonElement> onChunkReceived)
    {
        var s = buffer.ToString().Trim();
        if (string.IsNullOrEmpty(s)) return;

        try
        {
            using var doc = JsonDocument.Parse(s);
            HandleRootElement(doc.RootElement, state, onChunkReceived);
            buffer.Clear();
        }
        catch (JsonException)
        {
            if (buffer.Length > 1_000_000) buffer.Clear();
        }
    }

    private void ProcessJson(string jsonPart, StreamState state, Action<StreamState, JsonElement> onChunkReceived)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonPart);
            HandleRootElement(doc.RootElement, state, onChunkReceived);
        }
        catch (JsonException)
        {
        }
    }

    private void HandleRootElement(JsonElement root, StreamState state, Action<StreamState, JsonElement> onChunkReceived)
    {
        if (root.TryGetProperty("tool_call", out var toolCallEl))
        {
            // We will let the caller handle-ing which specific property to look for in message/tool_call structure.
            // For this demo, we assume the element is what needs to be passed back.
            onChunkReceived(state, root); 
        }
        else
        {
            onChunkReceived(state, root);
        }
    }
}