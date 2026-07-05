namespace AIConsole.Services;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using AIConsole.Models;

public interface IChatProvider : IDisposable
{
    Task StreamChatAsync(List<ChatMessage> history, CancellationToken cancellationToken,
        Action<StreamState, JsonElement> onChunkReceived, Action onStreamEnded);
}
