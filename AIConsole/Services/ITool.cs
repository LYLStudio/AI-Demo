namespace AIConsole.Services;

using System.Threading.Tasks;
using AIConsole.Models;

public interface ITool
{
    string Name { get; }
    string Description { get; }
    Task<ToolResult> ExecuteAsync(Dictionary<string, object> arguments);
}