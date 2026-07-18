using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace McpServer.Infrastructure;

/// <summary>
/// 將工具呼叫結果記錄為可審計的日誌檔案。
/// </summary>
public class AuditLogger
{
    private readonly string _logPath;
    private readonly object _writeLock = new();

    public AuditLogger(IConfiguration configuration)
    {
        _logPath = configuration["Audit:Path"] ?? Path.Combine(AppContext.BaseDirectory, "logs", $"audit_{DateTime.Now:yyyy-MM-dd}.log");
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
    }

    public void Write(string toolId, string? user, object? input, string resultStatus, object? result)
    {
        var entry = new
        {
            timestamp = DateTimeOffset.UtcNow.ToString("O"),
            tool_id = toolId,
            user = user ?? "anonymous",
            input = Mask(input),
            result_status = resultStatus,
            result = result
        };

        var jsonLine = JsonSerializer.Serialize(entry) + Environment.NewLine;
        
        // Thread-safe write with lock to prevent corruption from concurrent access
        lock (_writeLock)
        {
            try
            {
                File.AppendAllText(_logPath, jsonLine);
            }
            catch (IOException ex)
            {
                // Fail silently for logging - don't break the main flow
                Console.Error.WriteLine($"AuditLogger write failed: {ex.Message}");
            }
        }
    }

    private static object? Mask(object? input)
    {
        if (input is null)
        {
            return null;
        }

        if (input is JsonElement element)
        {
            return MaskJson(element);
        }

        if (input is string)
        {
            return input;
        }

        if (input is IDictionary<string, object?> dictionary)
        {
            var masked = new Dictionary<string, object?>();
            foreach (var (key, value) in dictionary)
            {
                masked[key] = IsSensitive(key) ? "[REDACTED]" : Mask(value);
            }

            return masked;
        }

        return input;
    }

    private static object? MaskJson(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(property => property.Name, property => MaskJson(property.Value)),
            JsonValueKind.Array => element.EnumerateArray().Select(MaskJson).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => null
        };
    }

    private static bool IsSensitive(string key) => key.Contains("token", StringComparison.OrdinalIgnoreCase)
        || key.Contains("password", StringComparison.OrdinalIgnoreCase)
        || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || key.Contains("api", StringComparison.OrdinalIgnoreCase) && key.Contains("key", StringComparison.OrdinalIgnoreCase);
}
