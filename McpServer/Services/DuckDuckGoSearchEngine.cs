using System.Net;
using System.Text.Json;
using McpServer.Interfaces;
using McpServer.Models;

namespace McpServer.Services;

/// <summary>
/// DuckDuckGo Instant Answer API 搜尋引擎實作。
/// 透過官方 JSON API (https://duckduckgo.com) 取得 factual definitions, Wikipedia abstracts, and related topics。
/// 支援 User-Agent 輪換、逼真瀏覽器標頭、重試退避等防偵測機制。
/// 
/// API 規格：
/// - Base URL: https://duckduckgo.com
/// - Query Parameters: q (查詢字串), format=json
/// - Cost: Free, no authentication or API key required
/// - Use Case: Retrieves factual definitions, Wikipedia abstracts, and related topics
/// </summary>
public class DuckDuckGoSearchEngine : IWebSearchEngine
{
    private readonly HttpClient _httpClient;
    private readonly IAntiBotStrategy _antiBotStrategy;
    private readonly string _apiBaseUrl;

     /// <summary>
     /// 建立 DuckDuckGo Instant Answer API 搜尋引擎服務。
     /// </summary>
     /// <param name="antiBotStrategy">爬蟲防範策略。</param>
     /// <param name="apiBaseUrl">API Base URL（預設為官方即時 API）。</param>
    public DuckDuckGoSearchEngine(IAntiBotStrategy antiBotStrategy, string? apiBaseUrl = null)
     {
         _antiBotStrategy = antiBotStrategy ?? throw new ArgumentNullException(nameof(antiBotStrategy));
         _apiBaseUrl = !string.IsNullOrWhiteSpace(apiBaseUrl)
             ? apiBaseUrl
             : "https://duckduckgo.com/";

         _httpClient = new HttpClient();
         _httpClient.Timeout = TimeSpan.FromSeconds(30);
     }

    public string Name => "duckduckgo";

    public bool IsDefault => true;

     /// <summary>
     /// 執行搜尋並回傳結果。自動處理重試與延遲。
     /// </summary>
    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(string query, CancellationToken cancellationToken = default)
     {
        if (string.IsNullOrWhiteSpace(query))
            return new List<SearchResultItem>();

         // 使用 DuckDuckGo Instant Answer API 格式
        var parameters = new Dictionary<string, string>
         {
             {"q", query},
             {"format", "json"},
             {"skip_disambig", "1"},
             {"no_html", "1"}
         };

        var queryString = string.Join("&", parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        var url = $"{_apiBaseUrl}?{queryString}";

        const int maxRetries = 3;
        Exception? lastEx = null;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
         {
            if (attempt > 0)
             {
                var delay = (int)(_antiBotStrategy.ComputeDelayMs() * Math.Pow(2, attempt));
                await Task.Delay(Math.Min(delay, 10000), cancellationToken);
             }

            try
             {
                return await FetchResultsAsync(url, cancellationToken);
             }
            catch (Exception ex)
             {
                lastEx = ex;
                 _antiBotStrategy.IncrementAndGetCount();
             }
         }

        if (lastEx != null)
            throw new InvalidOperationException($"搜尋失敗，已嘗試 {maxRetries + 1} 次: {lastEx.Message}", lastEx);

        return new List<SearchResultItem>();
     }

    private async Task<IReadOnlyList<SearchResultItem>> FetchResultsAsync(string url, CancellationToken cancellationToken)
     {
        var request = new HttpRequestMessage(HttpMethod.Get, url);

         // 套用 Anti-Bot 標頭模擬真實瀏覽器
         _antiBotStrategy.ApplyHeaders(request);
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("Accept-Encoding", "gzip, deflate");

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);

         // 如果 API 不可用，嘗試備用 URL
        if (!response.IsSuccessStatusCode && _apiBaseUrl != "https://duckduckgo.com/")
         {
             url = $"https://duckduckgo.com/?{url.Split('?', 2)[1]}";
             request = new HttpRequestMessage(HttpMethod.Get, url);
             _antiBotStrategy.ApplyHeaders(request);
             response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
         }

        response.EnsureSuccessStatusCode();

        var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);

        return ParseJsonResponse(jsonContent);
     }

    private IReadOnlyList<SearchResultItem> ParseJsonResponse(string jsonContent)
     {
        var results = new List<SearchResultItem>();

        if (string.IsNullOrWhiteSpace(jsonContent))
            return results;

        try
         {
             using var doc = JsonDocument.Parse(jsonContent);
             var root = doc.RootElement;

              // 主要結果: Text, AbstractTitle, AbstractUrl, Abstract
            if (root.TryGetProperty("Text", out var textProp) &&
                root.TryGetProperty("AbstractTitle", out var titleProp) &&
                root.TryGetProperty("AbstractUrl", out var urlProp) &&
                root.TryGetProperty("Abstract", out var abstractProp))
              {
                 results.Add(new SearchResultItem
                   {
                      Title = $"{titleProp.GetString() ?? ""} - {textProp.GetString() ?? ""}",
                      Url = urlProp.GetString() ?? "",
                      Description = abstractProp.GetString() ?? "",
                      SourceEngine = Name
                   });
              }

              // 相關搜尋: RelatedTopics[]
            if (root.TryGetProperty("RelatedTopics", out var relatedTopics) && relatedTopics.ValueKind == JsonValueKind.Array)
              {
                 foreach (var topic in relatedTopics.EnumerateArray().Take(9))
                   {
                      if (topic.TryGetProperty("Text", out var text) &&
                          topic.TryGetProperty("FirstURL", out var firstUrl))
                       {
                          results.Add(new SearchResultItem
                           {
                              Title = text.GetString() ?? "",
                              Url = firstUrl.GetString() ?? "",
                              Description = "",
                              SourceEngine = Name
                           });
                       }
                      else if (topic.TryGetProperty("Topics", out var subTopics) && subTopics.ValueKind == JsonValueKind.Array)
                       {
                         foreach (var subTopic in subTopics.EnumerateArray().Take(5))
                           {
                              if (subTopic.TryGetProperty("Text", out var subText) &&
                                  subTopic.TryGetProperty("FirstURL", out var subUrl))
                               {
                                  results.Add(new SearchResultItem
                                   {
                                      Title = subText.GetString() ?? "",
                                      Url = subUrl.GetString() ?? "",
                                      Description = "",
                                      SourceEngine = Name
                                   });
                               }
                           }
                       }
                   }
              }

              // 建議搜尋: Suggestions[]
            if (root.TryGetProperty("Suggestions", out var suggestions) && suggestions.ValueKind == JsonValueKind.Array)
              {
                 foreach (var suggestion in suggestions.EnumerateArray().Take(5))
                   {
                      var text = suggestion.GetString();
                      if (!string.IsNullOrWhiteSpace(text))
                       {
                          results.Add(new SearchResultItem
                           {
                              Title = $"{text} (建議查詢)",
                              Url = "",
                              Description = "DuckDuckGo 搜尋建議",
                              SourceEngine = Name
                           });
                       }
                   }
              }
         }
        catch (JsonException)
         {
              // 如果 JSON 解析失敗，嘗試使用純文字格式
            return ParsePlainTextResponse(jsonContent);
         }

        return results;
     }

    private IReadOnlyList<SearchResultItem> ParsePlainTextResponse(string content)
     {
        var results = new List<SearchResultItem>();

        if (string.IsNullOrWhiteSpace(content))
            return results;

        var lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < lines.Length; i += 2)
         {
             if (i + 1 < lines.Length)
               {
                  results.Add(new SearchResultItem
                   {
                      Title = WebUtility.HtmlDecode(lines[i].Trim()),
                      Url = WebUtility.HtmlDecode(lines[i + 1].Trim()),
                      Description = "",
                      SourceEngine = Name
                   });
               }
         }

        return results;
     }
}