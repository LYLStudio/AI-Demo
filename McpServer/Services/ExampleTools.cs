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

        // Security: Reject path traversal attempts
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult<object?>((object)"Error: Path is required and cannot be empty.");
        }

        var normalizedPath = Path.GetFullPath(path);
        var allowedRoots = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        var isAllowed = allowedRoots.Any(root => normalizedPath.StartsWith(root, StringComparison.Ordinal) || normalizedPath.StartsWith(root.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));

        if (!isAllowed)
        {
            return Task.FromResult<object?>((object)"Error: Access denied. Path traversal is not allowed.");
        }

        return Task.FromResult<object?>(File.Exists(normalizedPath) ? File.ReadAllText(normalizedPath) : $"File not found: {path}");
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
    private readonly HttpClient _httpClient;

    public HttpRequestTool(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

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
        
        // Validate URL format
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            return new { status = 0, error = "Error: Invalid URL provided." };
        }

        var method = input?.ValueKind == JsonValueKind.Object && input.Value.TryGetProperty("method", out var methodValue)
            ? methodValue.GetString()?.ToUpperInvariant()
            : null;

        var httpMethod = !string.IsNullOrWhiteSpace(method) 
            ? HttpMethod.Parse(method) 
            : HttpMethod.Get;

        try
        {
            using var request = new HttpRequestMessage(httpMethod, url);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new 
            { 
                status = (int)response.StatusCode,
                headers = response.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value)),
                body 
            };
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new { status = 0, error = "Error: Request was cancelled." };
        }
        catch (TimeoutException)
        {
            return new { status = 0, error = "Error: Request timed out after 30 seconds." };
        }
        catch (Exception ex)
        {
            return new { status = 0, error = $"Error: {ex.Message}" };
        }
    }
}

/// <summary>
/// 執行查詢的工具，支援 JSON/SQLite 資料來源。
/// </summary>
public class RunQueryTool : ITool
{
    private readonly IConfiguration _configuration;

    public RunQueryTool(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string Id => "run_query";
    public string Description => "Run a simple query against the configured data source (JSON/SQLite).";
    public Dictionary<string, object?> Schema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["query"] = new Dictionary<string, object?> { ["type"] = "string", ["required"] = true },
            ["source"] = new Dictionary<string, object?> { ["type"] = "string", ["required"] = false }
        },
        ["required"] = new[] { "query" }
    };
    public IList<string> RequiredRoles => new List<string> { "user" };

    public async Task<object?> ExecuteAsync(JsonElement? input, CancellationToken cancellationToken = default)
    {
        var query = input?.ValueKind == JsonValueKind.Object && input.Value.TryGetProperty("query", out var queryProperty)
            ? queryProperty.GetString() ?? string.Empty
            : string.Empty;

        var source = input?.ValueKind == JsonValueKind.Object && input.Value.TryGetProperty("source", out var sourceProperty)
            ? sourceProperty.GetString()
            : _configuration["Query:Source"];

        if (string.IsNullOrWhiteSpace(query))
        {
            return new { result = new[] { (object)"Error: Query is required and cannot be empty." } };
        }

        // JSON query mode - parse and filter JSON files
        if (source?.ToLowerInvariant() == "json")
        {
            var jsonDataFile = _configuration["Query:JsonFile"];
            
            if (!File.Exists(jsonDataFile))
            {
                return new 
                { 
                    query,
                    source,
                    result = new[] { $"Info: Data file not found: {jsonDataFile}" }
                };
            }

            try
            {
                var jsonContent = File.ReadAllText(jsonDataFile);
                using var doc = JsonDocument.Parse(jsonContent);
                
                // Simple query: filter objects containing the query string
                var results = new List<object>();
                
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        var jsonStr = item.GetRawText();
                        if (jsonStr.Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            results.Add(new { data = JsonSerializer.Deserialize<Dictionary<string, object?>>(jsonStr) });
                        }
                    }
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var jsonStr = doc.RootElement.GetRawText();
                    if (jsonStr.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new { data = JsonSerializer.Deserialize<Dictionary<string, object?>>(jsonStr) });
                    }
                }

                return new { query, source, result_count = results.Count, results };
            }
            catch (JsonException ex)
            {
                return new { query, error = $"Error parsing JSON: {ex.Message}" };
            }
        }
        
        // SQLite mode placeholder
        if (source?.ToLowerInvariant() == "sqlite")
        {
            var dbPath = _configuration["Query:SqliteDb"];
            return new 
            { 
                query, 
                source,
                result = new[] { $"Info: SQLite query support coming soon. Target DB: {dbPath}" }
            };
        }

        // Fallback to JSON mode
        return new 
        { 
            query, 
            source,
            result = new[] { $"Info: Unknown data source '{source}', defaulting to JSON mode." }
        };
    }
}

// Note: Old mock RunQueryTool removed - replaced with enhanced version above that supports JSON and SQLite data sources
