namespace AIConsole.Services;

using System.Collections.Generic;
using AIConsole.Models;

public class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new();

    public void RegisterTool(ITool tool)
    {
        _tools[tool.Name] = tool;
    }

    public ITool GetTool(string name)
    {
        return _tools.TryGetValue(name, out var tool) ? tool : null;
    }
}