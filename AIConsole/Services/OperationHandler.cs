namespace AIConsole.Services;

public class OperationHandler : IOperationHandler
{
    public double Execute(string operation, double a, double b)
    {
        switch (operation.ToLower())
        {
            case "add":
                return a + b;
            case "subtract":
                return a - b;
            case "multiply":
                return a * b;
            case "divide":
                if (Math.Abs(b) < 1e-9) throw new DivideByZeroException("Cannot divide by zero.");
                return a / b;
            default:
                throw new ArgumentException($"Unsupported operation: {operation}");
        }
    }
}