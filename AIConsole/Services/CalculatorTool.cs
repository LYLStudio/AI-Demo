namespace AIConsole.Services;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AIConsole.Models;

public class CalculatorTool : ITool
{
    private readonly IOperationHandler _operationHandler;
    
    public CalculatorTool(IOperationHandler operationHandler) { _operationHandler = operationHandler; }
    
    public string Name => "calculator";
    public string Description => "A tool for performing basic mathematical operations: add, subtract, multiply, and divide.";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> arguments)
    {
        try
        {
            if (!arguments.TryGetValue("operation", out var opObj) || !(opObj is string operation))
                throw new ArgumentException("Missing or invalid parameter: 'operation'. Expected a string (e.s., 'add', 'subtract', 'multiply', 'divide').");

            object aRaw = arguments.ContainsKey("a") ? arguments["a"] : null;
            object bRaw = arguments.ContainsKey("b") ? arguments["b"] : null;

            double a = ConvertToDouble(aRaw);
            double b = ConvertToDouble(bRaw);

double result = _operationHandler.Execute(operation, a, b);

            return new ToolResult
            {
                Name = Name,
                Content = result.ToString()
            };
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                Name = Name,
                Content = $"Error: {ex.Message}"
            };
        }
    }

    private double ConvertToDouble(object obj)
    {
        if (obj == null) throw new ArgumentException("Parameter is null");
        try
        {
            return Convert.ToDouble(obj);
        }
        catch (Exception)
        {
            throw new ArgumentException($"Cannot convert {obj} to double.");
        }
    }
}