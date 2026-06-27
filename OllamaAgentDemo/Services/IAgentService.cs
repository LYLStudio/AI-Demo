using System.Collections.Generic;
using System.Threading.Tasks;
using OllamaAgentDemo.Models;

namespace OllamaAgentDemo.Services;

public interface IAgentService
{
    Task RunConversationAsync(List<ChatMessage> history, string model);
}