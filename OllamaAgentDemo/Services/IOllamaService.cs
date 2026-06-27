using System.Collections.Generic;
using System.Threading.Tasks;
using OllamaAgentDemo.Models;

namespace OllamaAgentDemo.Services;

public interface IOllamaService
{
    Task<OllamaResponse?> ChatAsync(string model, List<ChatMessage> history);
}