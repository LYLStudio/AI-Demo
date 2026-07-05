using System.Net.Http.Json;
using System.Text.Json;
using McpServer.Interfaces;
using McpServer.Models;
using Microsoft.Extensions.Options;

namespace McpServer.Services;

/// <summary>
/// 查詢台灣上市/上櫃股票資訊的工具。
/// </summary>
public class StockInfoTool : ITool
{
    private readonly HttpClient _httpClient;
    private readonly OllamaSettings _ollamaSettings;
    private readonly StockApiSettings _stockApiSettings;

    public StockInfoTool(HttpClient httpClient, IOptions<OllamaSettings> ollamaSettings, IOptions<StockApiSettings> stockApiSettings)
    {
        _httpClient = httpClient;
        _ollamaSettings = ollamaSettings.Value;
        _stockApiSettings = stockApiSettings.Value;
    }

    public string Id => "stock_info";
    public string Description => "Query stock information from the Taiwan stock API using a stock symbol.";
    public Dictionary<string, object?> Schema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["symbol"] = new Dictionary<string, object?> { ["type"] = "string", ["required"] = true },
            ["market"] = new Dictionary<string, object?> { ["type"] = "string", ["required"] = false }
        },
        ["required"] = new[] { "symbol" }
    };
    public IList<string> RequiredRoles => new List<string> { "user" };

    public async Task<object?> ExecuteAsync(JsonElement? input, CancellationToken cancellationToken = default)
    {
        var symbol = input?.ValueKind == JsonValueKind.Object && input.Value.TryGetProperty("symbol", out var symbolProperty)
            ? symbolProperty.GetString() ?? string.Empty
            : string.Empty;
        var market = input?.ValueKind == JsonValueKind.Object && input.Value.TryGetProperty("market", out var marketProperty)
            ? marketProperty.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(symbol))
        {
            return new { error = "A stock symbol is required." };
        }

        var normalizedSymbol = symbol.Trim().ToLowerInvariant();
        var candidates = BuildCandidates(normalizedSymbol, market);

        foreach (var candidate in candidates)
        {
            var response = await _httpClient.GetAsync(BuildUrl(candidate), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                continue;
            }

            var payload = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
            var items = payload?.RootElement.TryGetProperty("msgArray", out var msgArray) == true
                ? msgArray.EnumerateArray().ToList()
                : new List<JsonElement>();

            if (items.Count == 0)
            {
                continue;
            }

            var normalizedItems = items.Select(item => item.Deserialize<Dictionary<string, JsonElement>>() ?? new Dictionary<string, JsonElement>()).ToList();
            if (HasCompanyName(normalizedItems))
            {
                return new { symbol = normalizedSymbol, market = candidate, data = normalizedItems };
            }
        }

        return new { symbol = normalizedSymbol, status = "not_found" };
    }

    private static bool HasCompanyName(IReadOnlyList<Dictionary<string, JsonElement>> items)
    {
        foreach (var item in items)
        {
            if (item.TryGetValue("n", out var name) && !string.IsNullOrWhiteSpace(name.GetString()))
            {
                return true;
            }

            if (item.TryGetValue("nf", out var fullName) && !string.IsNullOrWhiteSpace(fullName.GetString()))
            {
                return true;
            }
        }

        return false;
    }

    private string[] BuildCandidates(string symbol, string? market)
    {
        if (!string.IsNullOrWhiteSpace(market))
        {
            return new[] { market.ToLowerInvariant() switch
            {
                "tse" => $"tse_{symbol}.tw",
                "otc" => $"otc_{symbol}.tw",
                _ => symbol
            } };
        }

        return new[]
        {
            $"tse_{symbol}.tw",
            $"otc_{symbol}.tw"
        };
    }

    private string BuildUrl(string query)
    {
        var baseUrl = _stockApiSettings.BaseUrl;
        var separator = baseUrl.Contains('?') ? "&" : "?";
        return $"{baseUrl}{separator}ex_ch={query}";
    }
}
