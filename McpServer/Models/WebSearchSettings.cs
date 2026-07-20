using System.ComponentModel.DataAnnotations;

namespace McpServer.Models;

/// <summary>
/// WebSearch 工具設定，提供外部化組態以支援任意搜尋引擎與爬蟲策略。
/// </summary>
public class WebSearchSettings
{
    /// <summary>
    /// 搜尋引擎提供者清單（支援 DuckDuckGo、Bing、Google 等）。
    /// </summary>
    [Required]
    public List<SearchEngineProvider> SearchEngines { get; set; } = new();

    /// <summary>
    /// 模擬瀏覽器所需之 User-Agent 池。
    /// </summary>
    [Required]
    public List<string> UserAgentPool { get; set; } = new();

    /// <summary>
    /// 請求之間的最小延遲（毫秒），用於降低被偵測風險。
    /// </summary>
    public int MinDelayMs { get; set; } = 1000;

    /// <summary>
    /// 請求之間的最大延遲（毫秒）。
    /// </summary>
    public int MaxDelayMs { get; set; } = 3000;

    /// <summary>
    /// 啟用隨機 Delay 以模擬人類行為。
    /// </summary>
    public bool EnableRandomDelay { get; set; } = true;

    /// <summary>
    /// 最大請求重試次數。
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 重試之間的金爾賽退避倍率（exponential backoff multiplier）。
    /// </summary>
    public double RetryBackoffMultiplier { get; set; } = 2.0;
}

/// <summary>
/// 單一搜尋引擎提供者組態。
/// </summary>
public class SearchEngineProvider
{
    /// <summary>
    /// 搜尋引擎識別碼（如 "duckduckgo"、"bing"）。
    /// </summary>
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 搜尋 URL Template，{query} 會被替換為編碼後的查詢字串。
    /// </summary>
    [Required]
    public string SearchUrlTemplate { get; set; } = string.Empty;

    /// <summary>
    /// 此搜尋引擎預設啟用。
    /// </summary>
    public bool IsDefault { get; set; } = false;
}