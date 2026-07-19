using System.Diagnostics;
using System.Text.Json;
using McpServer.Infrastructure;
using McpServer.Interfaces;
using McpServer.Models;
using Microsoft.AspNetCore.Mvc;

namespace McpServer.Controllers;

/// <summary>
/// MCP (Model Context Protocol) JSON-RPC 2.0 compliant endpoint.
/// All communication goes through POST /mcp with a "method" field.
/// </summary>
[ApiController]
[Route("mcp")]
public class McpController : ControllerBase
{
    private readonly IToolRegistry _toolRegistry;
    private readonly IConfiguration _configuration;
    private readonly AuditLogger _auditLogger;

    public McpController(
        IToolRegistry toolRegistry,
        IConfiguration configuration,
        AuditLogger auditLogger)
    {
        _toolRegistry = toolRegistry;
        _configuration = configuration;
        _auditLogger = auditLogger;
    }

    /// <summary>
    /// MCP JSON-RPC endpoint. All methods are routed through this single POST endpoint.
    /// Supported methods: initialize, tools/list, tools/call
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    [Produces("application/json")]
    public async Task<IActionResult> McpEndpoint()
    {
        string? rawBody;
        using (var reader = new StreamReader(Request.Body))
        {
            rawBody = await reader.ReadToEndAsync();
        }

        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return Ok(McpErrorResponse.From(null, -32700, "Parse error: Invalid JSON.", null));
        }

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;

            // Must be a valid JSON-RPC request (jsonrpc must be "2.0")
            if (!TryGetStringProperty(root, "jsonrpc", out var jsonrpcVersion) || jsonrpcVersion != "2.0")
            {
                return Ok(McpErrorResponse.From(ExtractId(root), -32600, "Invalid Request", null));
            }

            // method is required
            if (!TryGetStringProperty(root, "method", out var method))
            {
                return Ok(McpErrorResponse.From(ExtractId(root), -32600, "Method not found", null));
            }

            object? reqId = ExtractIdForResponse(root);
            JsonElement? paramsElem = TryGetParams(root);

            return method switch
            {
                "initialize" => await HandleInitializeAsync(reqId, paramsElem),
                "tools/list" => HandleListTools(reqId),
                "tools/call" => await HandleCallTool(paramsElem, reqId),
                _ => Ok(McpErrorResponse.From(reqId, -32601, $"Method not found: {method}", null))
            };
        }
        catch (JsonException)
        {
            return Ok(McpErrorResponse.From(null, -32700, "Parse error: Invalid JSON.", null));
        }
        catch (Exception ex)
        {
            return Ok(McpErrorResponse.From(null, -32603, "Internal error", new { error = ex.Message }));
        }
    }

    // --- helpers ---

    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string? value)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString();
            return true;
        }

        value = null;
        return false;
    }

    private static JsonElement? TryGetParams(JsonElement root)
    {
        if (root.TryGetProperty("params", out var prop) && prop.ValueKind != JsonValueKind.Null)
        {
            return prop;
        }
        return null;
    }

    private static object? ExtractId(JsonElement root)
    {
        if (root.TryGetProperty("id", out var idProp))
        {
            return idProp.ValueKind == JsonValueKind.Null ? null : idProp.GetRawText();
        }
        return null;
    }

    private static object? ExtractIdForResponse(JsonElement root)
    {
        if (root.TryGetProperty("id", out var idProp))
        {
            return idProp.ValueKind == JsonValueKind.Null ? null : idProp.GetRawText();
        }
        return null;
    }

    // --- method handlers ---

    private IActionResult HandleInitialize(object? reqId)
    {
        return Ok(McpResponse.Ok(reqId, new InitializeResponse()));
    }
    
    private async Task<IActionResult> HandleInitializeAsync(object? reqId, JsonElement? paramsElem)
    {
        var response = new InitializeResponse();
        
        // Log client info if provided for debugging/auditing
        if (paramsElem is not null && paramsElem.Value.TryGetProperty("clientInfo", out var clientInfo) && 
            clientInfo.ValueKind == JsonValueKind.Object)
        {
            var clientName = clientInfo.GetProperty("name").GetString() ?? "unknown";
            var clientVersion = clientInfo.TryGetProperty("version", out var versionProp) 
                ? versionProp.GetString() ?? "unknown" 
                : "unknown";
            
            _auditLogger.Write("initialize", null, 
                new { clientName, clientVersion }, 
                "client_connected", 
                new { protocolVersion = response.ProtocolVersion });
        }

        return Ok(McpResponse.Ok(reqId, response));
    }

    private IActionResult HandleListTools(object? reqId)
    {
        var tools = _toolRegistry.ListTools().Select(t => new
        {
            name = t.Id,
            description = t.Description,
            inputSchema = NormalizeSchema(t.Schema)
        }).ToList();

        return Ok(McpResponse.Ok(reqId, new { tools }));
    }

    private static object NormalizeSchema(Dictionary<string, object?>? schema)
    {
        if (schema is null || (!schema.ContainsKey("type") && !schema.ContainsKey("properties")))
        {
            return new { type = "object", properties = new Dictionary<string, object>() };
        }

        // Ensure type is set
        if (!schema.ContainsKey("type"))
        {
            schema["type"] = "object";
        }
        if (!schema.ContainsKey("properties"))
        {
            schema["properties"] = new Dictionary<string, object>();
        }
        return schema;
    }

    private async Task<IActionResult> HandleCallTool(JsonElement? paramsElem, object? reqId)
    {
        if (paramsElem is null || paramsElem.Value.ValueKind != JsonValueKind.Object)
        {
            return Ok(McpErrorResponse.From(reqId, -32602, "Invalid params", new { error = "Params must be an object with 'name' and 'arguments'." }));
        }

        var p = paramsElem.Value;

        if (!TryGetStringProperty(p, "name", out var toolName) || string.IsNullOrEmpty(toolName))
        {
            return Ok(McpErrorResponse.From(reqId, -32602, "Invalid params", new { error = "Missing 'name' field." }));
        }

        if (!p.TryGetProperty("arguments", out var argsProp) || argsProp.ValueKind != JsonValueKind.Object)
        {
            return Ok(McpErrorResponse.From(reqId, -32602, "Invalid params", new { error = "Missing or invalid 'arguments' field." }));
        }

        var jsonDoc = JsonDocument.Parse(argsProp.GetRawText());
        var inputElement = jsonDoc.Deserialize<JsonElement?>();

        var result = await _toolRegistry.ExecuteAsync(toolName, inputElement, user: null, HttpContext?.RequestAborted ?? default);

        if (result.Success)
        {
            // Standardize tool result as MCP content format
            // 使用 UnsafeRelaxedJsonEscaping 避免非 ASCII 字符被轉義為 \u00xx
            var serializeOptions = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            
            var contentText = result.Result switch
            {
                string str => str,
                object obj => JsonSerializer.Serialize(obj, serializeOptions),
                _ => ""
            };

            return Ok(McpResponse.Ok(reqId, new
            {
                content = new[] { new { type = "text", text = contentText } }
            }));
        }
        else
        {
            // Standardize error response per JSON-RPC 2.0 spec
            return Ok(McpErrorResponse.From(reqId, -32603, $"Tool execution error: {result.ToolId}", new { 
                tool_id = result.ToolId, 
                message = result.Error,
                details = result.Details 
            }));
        }
    }
}
