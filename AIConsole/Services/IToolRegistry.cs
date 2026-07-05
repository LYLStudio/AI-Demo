namespace AIConsole.Services;

using AIConsole.Models;

public interface IToolRegistry
{
    void RegisterTool(ITool tool);
    ITool GetTool(string name);
}