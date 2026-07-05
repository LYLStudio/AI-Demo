using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace McpServer.Infrastructure;

/// <summary>
/// 將工具呼叫結果記錄為可審計的日誌檔案。
/// </summary>
public class AuditLogger
{
    private readonly string _logPath;

    public AuditLogger(IConfiguration configuration)
    {
        _logPath = configuration["Audit:Path"] ?? Path.Combine(AppContext.BaseDirectory, "logs", "audit.log");
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
    }

    public void Write(string toolId, string? user, object? input, string resultStatus, object? result)
    {
        var entry = new
        {
            timestamp = DateTimeOffset.UtcNow,
            tool_id = toolId,
            user = user ?? "anonymous",
            input = Mask(input),
            result_status = resultStatus,
            result = result
        };

        File.AppendAllText(_logPath, JsonSerializer.Serialize(entry) + Environment.NewLine);
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
