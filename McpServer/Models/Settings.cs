namespace McpServer.Models;

/// <summary>
/// Ollama 相關設定，包含模型名稱與服務端點。
/// </summary>
public class OllamaSettings
{
    public const string SectionName = "Ollama";

    public required string BaseUrl { get; set; }
    public required string Model { get; set; }
    public bool UseMock { get; set; }
}

/// <summary>
/// 股票資訊 API 的設定。
/// </summary>
public class StockApiSettings
{
    public const string SectionName = "StockApi";

    public required string BaseUrl { get; set; }
}

/// <summary>
/// 查詢工具相關設定（JSON/SQLite）。
/// </summary>
public class QuerySettings
{
    public const string SectionName = "Query";

    public string? Source { get; set; }      // json, sqlite (default: null)
    public string? JsonFile { get; set; }    // path to JSON data file
    public string? SqliteDb { get; set; }    // path to SQLite database
}

/// <summary>
/// 審計日誌設定。
/// </summary>
public class AuditSettings
{
    public const string SectionName = "Audit";

    public string? Path { get; set; }        // log file path
    public int RetentionDays { get; set; } = 30;  // days to keep logs
}

/// <summary>
/// 安全設定（API Key、Rate Limiting）。
/// </summary>
public class SecuritySettings
{
    public const string SectionName = "Security";

    public string? ApiKey { get; set; }
    public int MaxRequestBodyBytes { get; set; } = 10 * 1024 * 1024; // 10MB default
}
