using System.Text.Json;
using System.Text.Json.Serialization;

namespace McpServer.Models;

/// <summary>
/// JSON-RPC 2.0 Request model for MCP protocol.
/// See https://spec.jsonrpc.org/
/// </summary>
public class McpRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc => "2.0";

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }

    /// <summary>
    /// The request identifier (number or string). Required for all non-notifications.
    /// </summary>
    [JsonPropertyName("id")]
    public JsonElement? Id { get; set; }
}

/// <summary>
/// JSON-RPC 2.0 Success Response model for MCP protocol.
/// </summary>
public class McpResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc => "2.0";

    [JsonPropertyName("id")]
    public object? Id { get; set; } = null;

    [JsonPropertyName("result")]
    public object? Result { get; set; }

    public static McpResponse Ok(object? id, object result)
    {
        return new() { Id = id, Result = result };
    }
}

/// <summary>
/// JSON-RPC 2.0 Error Response model for MCP protocol.
/// </summary>
public class McpErrorResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc => "2.0";

    [JsonPropertyName("id")]
    public object? Id { get; set; } = null;

    [JsonPropertyName("error")]
    public ErrorDetail Error { get; set; } = new();

    public static McpErrorResponse From(object? id, int code, string message, object? data = null)
    {
        return new()
        {
            Id = id,
            Error = new()
            {
                Code = code,
                Message = message,
                Data = data
            }
        };
    }

    public static McpErrorResponse From(object? id, int code, string message, Dictionary<string, object?>? data)
    {
        return From(id, code, message, (object?)data);
    }
}

public class ErrorDetail
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}

/// <summary>
/// MCP Server capabilities response from initialize.
/// </summary>
public class McpCapabilities
{
    [JsonPropertyName("tools")]
    public object? Tools { get; set; } = new { listChanged = false };

    [JsonPropertyName("logging")]
    public object? Logging { get; set; } = new { };
}

/// <summary>
/// Initialize request parameters.
/// </summary>
public class InitializeRequest
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = "2025-06-18";

    [JsonPropertyName("clientInfo")]
    public ClientInfo? ClientInfo { get; set; }

    [JsonPropertyName("capabilities")]
    public object? Capabilities { get; set; } = new { };
}

public class ClientInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}

/// <summary>
/// Initialize response (server info + capabilities).
/// </summary>
public class InitializeResponse
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = "2025-06-18";

    [JsonPropertyName("serverInfo")]
    public ServerInfo ServerInfo { get; set; } = new();

    [JsonPropertyName("capabilities")]
    public McpCapabilities Capabilities { get; set; } = new();
}

public class ServerInfo
{
    [JsonPropertyName("name")]
    public string Name => "McpServer";

    [JsonPropertyName("version")]
    public string Version => "1.0.0";
}
