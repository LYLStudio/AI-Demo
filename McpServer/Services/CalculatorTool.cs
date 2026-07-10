using System.Data;
using System.Text.Json;
using McpServer.Interfaces;

namespace McpServer.Services;

/// <summary>
/// 數學運算器工具，支援基本 arithmetic 表達式計算。
/// </summary>
public class CalculatorTool : ITool
{
    public string Id => "calculator";

    public string Description => "Evaluate a mathematical expression and return the result.";

    public Dictionary<string, object?> Schema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["expression"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "The mathematical expression to evaluate (e.g., '1+2*3')" }
        },
        ["required"] = new[] { "expression" }
    };

    public IList<string> RequiredRoles => new List<string> { "user" };

    public Task<object?> ExecuteAsync(JsonElement? input, CancellationToken cancellationToken = default)
    {
        try
        {
            // 從輸入中取得 expression 屬性
            var expression = input?.ValueKind == JsonValueKind.Object && input.Value.TryGetProperty("expression", out var exprProperty)
                ? exprProperty.GetString() ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(expression))
            {
                return Task.FromResult<object?>("Error: Expression is empty or null.");
            }

            // 使用 DataTable 來計算字串表達式
            var dt = new DataTable();
            var result = dt.Compute(expression, "");
            return Task.FromResult<object?>(new { expression, result });
        }
        catch (Exception ex)
        {
            return Task.FromResult<object?>($"Error: {ex.Message}");
        }
    }
}