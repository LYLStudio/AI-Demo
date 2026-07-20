using System.Text.Json.Serialization;
namespace McpServer.Models;

public class StockInfoResponse
{
    [JsonPropertyName("msgArray")]
    public List<StockMessage>? MsgArray { get; set; }

    [JsonPropertyName("referer")]
    public string? Referer { get; set; }

    [JsonPropertyName("userDelay")]
    public int? UserDelay { get; set; }

    [JsonPropertyName("rtcode")]
    public string? RtCode { get; set; }

    [JsonPropertyName("queryTime")]
    public QueryTimeInfo? QueryTime { get; set; }

    [JsonPropertyName("rtmessage")]
    public string? RtMessage { get; set; }

    [JsonPropertyName("exKey")]
    public string? ExKey { get; set; }

    [JsonPropertyName("cachedAlive")]
    public int CachedAlive { get; set; }
}

public class QueryTimeInfo
{
    [JsonPropertyName("sysDate")]
    public string? SysDate { get; set; }

    [JsonPropertyName("stockInfoItem")]
    public int StockInfoItem { get; set; }

    [JsonPropertyName("stockInfo")]
    public int StockInfo { get; set; }

    [JsonPropertyName("sessionStr")]
    public string? SessionStr { get; set; }

    [JsonPropertyName("sysTime")]
    public string? SysTime { get; set; }

    [JsonPropertyName("showChart")]
    public bool ShowChart { get; set; }

    [JsonPropertyName("sessionFromTime")]
    public int SessionFromTime { get; set; }

    [JsonPropertyName("sessionLatestTime")]
    public int SessionLatestTime { get; set; }
}

