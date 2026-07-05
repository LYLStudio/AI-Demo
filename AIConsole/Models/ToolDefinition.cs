namespace AIConsole.Models;

using System.Collections.Generic;

public class ToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
}

public class ToolCall
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object> Arguments { get; set; } = new();
}

public class ToolResult
{
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}