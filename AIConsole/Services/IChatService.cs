namespace AIConsole.Services;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Text.Json;
using System.Threading.Tasks;
using AIConsole.Models;

public interface IChatService : IDisposable
{
    Task StreamChatAsync(List<ChatMessage> history, CancellationToken cancellationToken, Action<StreamState, JsonElement> onChunkReceived, Action onStreamEnded);
}