using System.Text.Json;
using McpServer.Interfaces;

namespace McpServer.Services;

/// <summary>
/// 透過文件系統檢索文件內容的工具。
/// </summary>
public class ReadFileTool : ITool
{
    public string Id => "read_file";
    public string Description => "Read a local file from the workspace.";
    public Dictionary<string, object?> Schema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["path"] = new Dictionary<string, object?> { ["type"] = "string", ["required"] = true }
        },
        ["required"] = new[] { "path" }
    };
    public IList<string> RequiredRoles => new List<string> { "user" };

    public Task<object?> ExecuteAsync(JsonElement? input, CancellationToken cancellationToken = default)
    {
        var path = input?.ValueKind == JsonValueKind.Object && input.Value.TryGetProperty("path", out var pathProperty)
            ? pathProperty.GetString() ?? string.Empty
            : string.Empty;
        return Task.FromResult<object?>(File.Exists(path) ? File.ReadAllText(path) : $"File not found: {path}");
    }
}

/// <summary>
/// 依據查詢搜尋已建檔的文件內容。
/// </summary>
public class SearchDocsTool : ITool
{
    public string Id => "search_docs";
    public string Description => "Search indexed documentation chunks.";
    public Dictionary<string, object?> Schema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["query"] = new Dictionary<string, object?> { ["type"] = "string", ["required"] = true }
        },
        ["required"] = new[] { "query" }
    };
    public IList<string> RequiredRoles => new List<string> { "user" };

    public Task<object?> ExecuteAsync(JsonElement? input, CancellationToken cancellationToken = default)
    {
        var query = input?.ValueKind == JsonValueKind.Object && input.Value.TryGetProperty("query", out var queryProperty)
            ? queryProperty.GetString() ?? string.Empty
            : string.Empty;
        return Task.FromResult<object?>(new { query, matches = new[] { "No indexed docs available yet." } });
    }
}

/// <summary>
/// 發送 HTTP 請求到指定 URL 的工具。
/// </summary>
public class HttpRequestTool : ITool
{
    public string Id => "http_request";
    public string Description => "Issue an HTTP request.";
    public Dictionary<string, object?> Schema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["url"] = new Dictionary<string, object?> { ["type"] = "string", ["required"] = true },
            ["method"] = new Dictionary<string, object?> { ["type"] = "string", ["required"] = false }
        },
        ["required"] = new[] { "url" }
    };
    public IList<string> RequiredRoles => new List<string> { "user" };

    public async Task<object?> ExecuteAsync(JsonElement? input, CancellationToken cancellationToken = default)
    {
        var url = input?.ValueKind == JsonValueKind.Object && input.Value.TryGetProperty("url", out var urlProperty)
            ? urlProperty.GetString() ?? string.Empty
            : string.Empty;
        var method = input?.ValueKind == JsonValueKind.Object && input.Value.TryGetProperty("method", out var methodValue)
            ? methodValue.GetString()
            : "GET";
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(new HttpMethod(method ?? "GET"), url);
        using var response = await client.SendAsync(request, cancellationToken);
        return new { status = (int)response.StatusCode, body = await response.Content.ReadAsStringAsync(cancellationToken) };
    }
}

/// <summary>
/// 執行簡單查詢的工具示例。
/// </summary>
public class RunQueryTool : ITool
{
    public string Id => "run_query";
    public string Description => "Run a simple query against the configured data source.";
    public Dictionary<string, object?> Schema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["query"] = new Dictionary<string, object?> { ["type"] = "string", ["required"] = true }
        },
        ["required"] = new[] { "query" }
    };
    public IList<string> RequiredRoles => new List<string> { "user" };

    public Task<object?> ExecuteAsync(JsonElement? input, CancellationToken cancellationToken = default)
    {
        var query = input?.ValueKind == JsonValueKind.Object && input.Value.TryGetProperty("query", out var queryProperty)
            ? queryProperty.GetString() ?? string.Empty
            : string.Empty;
        return Task.FromResult<object?>(new { query, result = "mock query result" });
    }
}
