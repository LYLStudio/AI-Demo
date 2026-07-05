using System.Text.Json;

namespace McpServer.Infrastructure;

/// <summary>
/// 提供基礎 JSON schema 驗證能力，支援物件、字串、數字、布林與陣列。
/// </summary>
public static class SchemaValidator
{
    public static bool Validate(JsonElement? input, Dictionary<string, object?>? schema)
    {
        if (schema is null || schema.Count == 0)
        {
            return true;
        }

        if (input is null || input.Value.ValueKind == JsonValueKind.Undefined)
        {
            return false;
        }

        if (!schema.TryGetValue("type", out var typeValue) || typeValue is not string type)
        {
            return true;
        }

        return type switch
        {
            "object" => ValidateObject(input.Value, schema),
            "string" => input.Value.ValueKind == JsonValueKind.String,
            "number" => input.Value.ValueKind is JsonValueKind.Number,
            "integer" => input.Value.ValueKind is JsonValueKind.Number && input.Value.TryGetInt32(out _),
            "boolean" => input.Value.ValueKind == JsonValueKind.True || input.Value.ValueKind == JsonValueKind.False,
            "array" => ValidateArray(input.Value, schema),
            _ => true
        };
    }

    private static bool ValidateObject(JsonElement element, Dictionary<string, object?> schema)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (schema.TryGetValue("required", out var requiredValue) && requiredValue is IEnumerable<object?> requiredItems)
        {
            foreach (var requiredName in requiredItems.OfType<string>())
            {
                if (!element.TryGetProperty(requiredName, out _))
                {
                    return false;
                }
            }
        }

        if (schema.TryGetValue("properties", out var propertiesValue) && propertiesValue is Dictionary<string, object?> properties)
        {
            foreach (var (propertyName, propertySchema) in properties)
            {
                if (!element.TryGetProperty(propertyName, out var propertyValue))
                {
                    continue;
                }

                if (propertySchema is Dictionary<string, object?> propertyDefinition && !Validate(propertyValue, propertyDefinition))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool ValidateArray(JsonElement element, Dictionary<string, object?> schema)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        if (schema.TryGetValue("items", out var itemsValue) && itemsValue is Dictionary<string, object?> itemSchema)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (!Validate(item, itemSchema))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
