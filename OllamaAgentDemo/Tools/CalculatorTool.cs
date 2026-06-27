using System;
using System.Data;

namespace OllamaAgentDemo.Tools;

public class CalculatorTool : ITool
{
    public string Name => "Calculator";

    public Task<string> ExecuteAsync(string expression)
    {
        try
        {
            // 使用 DataTable 來計算字串表達式 (簡單的 Demo 用法)
            var dt = new DataTable();
            var result = dt.Compute(expression, "");
            return Task.FromResult($"{result}");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error: {ex.Message}");
        }
    }
}