using System.Text;
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
    private readonly StockApiSettings _stockApiSettings;

    public StockInfoTool(HttpClient httpClient, IOptions<StockApiSettings> stockApiSettings)
    {
        _httpClient = httpClient;
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

        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        var candidates = BuildCandidates(normalizedSymbol, market);

        foreach (var candidate in candidates)
        {
            var response = await _httpClient.GetAsync(BuildUrl(candidate), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                continue;
            }

            var payload = await response.Content.ReadFromJsonAsync<StockInfoResponse>(cancellationToken: cancellationToken);

            var items = payload?.MsgArray ?? new List<StockMessage>();
            if (items.Count == 0)
            {
                continue;
            }
            if (HasCompanyName(items))
            {
                return new
                {
                    symbol = normalizedSymbol,
                    market = candidate,
                    data = items.Select(x => new
                    {
                        x.ChannelId,
                        Name = $"base64|{Convert.ToBase64String(Encoding.UTF8.GetBytes($"{x.Name}"))}|base64",
                        FullName = $"base64|{Convert.ToBase64String(Encoding.UTF8.GetBytes($"{x.FullName}"))}|base64",
                        x.Open,
                        x.High,
                        x.Low,
                        x.Volume,
                        x.YesterdayClose,
                        x.LimitUp,
                        x.LimitDown,
                        x.Time,
                        TimeInMillisecond = x.Tlong,
                        x.Close,
                        Price = x.Close,
                        PauseOrDelayFlag = x.P,
                        x.InfoType,
                        x.InfoIndex,
                        TradeTypeFlag = x.Mt,
                        SpecialFlag = x.Ip,
                        x.BestAskPrices,
                        x.BestAskVolumes,
                        x.BestBidPrices,
                        x.BestBidVolumes,
                        x.Ps,
                        x.Pz,
                        x.Bp,
                        x.Code,
                        x.Date,
                        x.DateSnapshot,
                        MarketType = x.Exchange,
                        x.Key,
                        MarketCapSharePercent = x.MPercent
                    })
                };
            }
        }

        return new { symbol = normalizedSymbol, status = "not_found" };
    }

    private static bool HasCompanyName(IReadOnlyList<StockMessage> items)
    {
        foreach (var item in items)
        {
            return !string.IsNullOrWhiteSpace(item.Name) || !string.IsNullOrWhiteSpace(item.FullName);
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
