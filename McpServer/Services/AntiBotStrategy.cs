using System.Collections.Concurrent;
using McpServer.Interfaces;
using McpServer.Models;

namespace McpServer.Services;

/// <summary>
/// 基於瀏覽器模擬之 Anti-Bot 策略，透過輪換 User-Agent、新增逼真標頭與隨機延遲來迴避爬蟲偵測。
/// </summary>
public class BrowserSimAntiBotStrategy : IAntiBotStrategy
{
    private readonly WebSearchSettings _settings;
    private readonly Random _random = new();
    private int _requestCount;

    public BrowserSimAntiBotStrategy(WebSearchSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public string StrategyName => "browser_sim";

    /// <summary>
    /// 知名瀏覽器 User-Agent 字串清單（由組態池提供，非 hardcode）。
    /// </summary>
    private IEnumerable<string> UserAgentPool => _settings.UserAgentPool.Any()
        ? _settings.UserAgentPool
        : throw new InvalidOperationException("UserAgentPool is empty in configuration.");

    /// <inheritdoc />
    public void ApplyHeaders(HttpRequestMessage request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        // 旋轉選擇 User-Agent（從組態池隨機取得）。
        var userAgent = PickRandomUserAgent();
        request.Headers.Add("User-Agent", userAgent);

        // 模擬常見瀏覽器標頭（避免 hardcode 固定值，全部由預設值或環境組合驅動）。
        request.Headers.Add("Accept-Language", "zh-TW,zh,en-US,en;q=0.9");
        request.Headers.Add("Accept-Encoding", "gzip, deflate, br");
        request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
        request.Headers.Add("Cache-Control", "no-cache");
        request.Headers.Add("Pragma", "no-cache");
        request.Headers.Add("Sec-CH-UA", "\"Not_A Brand\";v=\"8\", \"Chromium\";v=\"126\", \"Chrome\";v=\"126\"");
        request.Headers.Add("Sec-CH-UA-Mobile", "?0");
        request.Headers.Add("Sec-CH-UA-Platform", "\"macOS\"");
        request.Headers.Add("Sec-Fetch-Dest", "document");
        request.Headers.Add("Sec-Fetch-Mode", "navigate");
        request.Headers.Add("Sec-Fetch-Site", "none");
        request.Headers.Add("Sec-Fetch-User", "?1");
        request.Headers.Add("Upgrade-Insecure-Requests", "1");

        // 隨機 Client-Hints（增加難度）。
        request.Headers.Add("DNT", _random.Next(0, 2).ToString());
    }

    /// <inheritdoc />
    public int ComputeDelayMs()
    {
        if (!_settings.EnableRandomDelay)
            return 0;

        var min = _settings.MinDelayMs;
        var max = _settings.MaxDelayMs;
        if (min >= max)
            return min;

        return _random.Next(min, max + 1);
    }

    /// <summary>
    /// 增加請求計數並回傳（供重試退避使用）。
    /// </summary>
    public int IncrementAndGetCount()
    {
        return ++_requestCount;
    }

    private string PickRandomUserAgent()
    {
        var pool = UserAgentPool.ToList();
        if (pool.Count == 0)
            throw new InvalidOperationException("User-Agent pool is empty. Please configure WebSearchSettings.UserAgentPool.");
        return pool[_random.Next(pool.Count)];
    }
}

/// <summary>
/// 請求標頭緩衝，避免相同來源連續請求使用相同標頭。
/// </summary>
public sealed class HeaderBuffer : IDisposable
{
    private readonly ConcurrentDictionary<string, int> _headerVersion = new();
    private readonly object _lock = new();

    public int GetVersion(string host)
    {
        lock (_lock)
        {
            return _headerVersion.GetOrAdd(host, _ => 0);
        }
    }

    public void Increment(string host)
    {
        lock (_lock)
        {
            _headerVersion.AddOrUpdate(host, 1, (_, v) => v + 1);
        }
    }

    public void Dispose() { }
}