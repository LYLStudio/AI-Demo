using System.Text.Json;
using McpServer.Infrastructure;
using McpServer.Interfaces;
using McpServer.Models;

namespace McpServer.Services;

/// <summary>
/// 依據 DI 註冊的工具集合，提供工具描述、驗證與執行流程。
/// </summary>
public class ToolRegistry : IToolRegistry
{
    private readonly IReadOnlyList<ITool> _tools;
    private readonly AuditLogger _auditLogger;

    public ToolRegistry(IEnumerable<ITool> tools, AuditLogger auditLogger)
    {
        _tools = tools.ToList();
        _auditLogger = auditLogger;
    }

    public IReadOnlyList<ToolDescriptor> ListTools()
    {
        return _tools.Select(tool => new ToolDescriptor
        {
            Id = tool.Id,
            Description = tool.Description,
            Schema = tool.Schema,
            RequiredRoles = tool.RequiredRoles.ToList()
        }).ToList();
    }

    public bool TryGetTool(string toolId, out ITool? tool)
    {
        tool = _tools.FirstOrDefault(candidate => string.Equals(candidate.Id, toolId, StringComparison.OrdinalIgnoreCase));
        return tool is not null;
    }

    public async Task<CallToolResponse> ExecuteAsync(string toolId, JsonElement? input, string? user = null, CancellationToken cancellationToken = default)
    {
        if (!TryGetTool(toolId, out var tool) || tool is null)
        {
            return new CallToolResponse { Success = false, Error = $"Tool '{toolId}' was not found." };
        }

        if (!IsAuthorized(tool, user))
        {
            return new CallToolResponse { Success = false, Error = $"The caller is not authorized for tool '{toolId}'." };
        }

        if (!SchemaValidator.Validate(input, tool.Schema))
        {
            _auditLogger.Write(tool.Id, user, input, "schema_error", null);
            return new CallToolResponse { Success = false, Error = $"Input does not match the schema for tool '{toolId}'." };
        }

        try
        {
            var result = await tool.ExecuteAsync(input, cancellationToken);
            _auditLogger.Write(tool.Id, user, input, "ok", result);
            return new CallToolResponse { Success = true, ToolId = tool.Id, Result = result, AuditId = Guid.NewGuid().ToString("N") };
        }
        catch (Exception ex)
        {
            _auditLogger.Write(tool.Id, user, input, "error", ex.Message);
            return new CallToolResponse { Success = false, ToolId = tool.Id, Error = ex.Message };
        }
    }

    private static bool IsAuthorized(ITool tool, string? user)
    {
        if (tool.RequiredRoles.Count == 0)
        {
            return true;
        }

        var normalized = (user ?? "anonymous").ToLowerInvariant();
        var isAdmin = normalized.Contains("admin") || normalized.Contains("root");
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        roles.Add("user");
        if (isAdmin)
        {
            roles.Add("admin");
        }

        return tool.RequiredRoles.Any(roles.Contains);
    }
}
