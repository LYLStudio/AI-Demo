using System.Text.Json;
using McpServer.Interfaces;
using McpServer.Models;

namespace McpServer.Services;

/// <summary>
/// 網頁搜尋工具，支援任意搜尋引擎與爬蟲防範機制。
/// 遵循 SOLID 原則：
/// - SRP: 僅負責執行網頁搜尋並回傳結果
/// - OCP: 透過 IWebSearchEngine 介面擴充新搜尋引擎
/// - LSP: 任何實作 IWebSearchEngine 的類別均可替換使用
/// - ISP: 介面方法精簡，不強加不必要成員
/// - DIP: 依賴抽象 (IWebSearchEngine) 而非具體實現
/// </summary>
public class WebSearchTool : ITool
{
    private readonly IEnumerable<IWebSearchEngine> _searchEngines;

      /// <summary>
      /// 建立網頁搜尋工具實例。
      /// </summary>
      /// <param name="searchEngines">搜尋引擎提供者集合（可註冊多個引擎）。</param>
    public WebSearchTool(IEnumerable<IWebSearchEngine> searchEngines)
       {
           _searchEngines = searchEngines ?? throw new ArgumentNullException(nameof(searchEngines));
       }

    public string Id => "web_search";

    public string Description => "搜尋任意網址的任意內容，支援多搜尋引擎與爬蟲防範機制。";

    public Dictionary<string, object?> Schema => new()
      {
          ["type"] = "object",
          ["properties"] = new Dictionary<string, object?>
          {
              ["query"] = new Dictionary<string, object?> 
              { 
                  ["type"] = "string", 
                  ["description"] = "搜尋查詢字串" 
              },
              ["engine"] = new Dictionary<string, object?> 
              { 
                  ["type"] = "string", 
                  ["description"] = "指定使用的搜尋引擎名稱（可選，預設使用第一個註冊的引擎）",
                  ["enum"] = new string[0] // 動態替換
              },
              ["maxResults"] = new Dictionary<string, object?> 
              { 
                  ["type"] = "integer", 
                  ["description"] = "最大結果數量（可選，預設 10）",
                  ["minimum"] = 1,
                  ["maximum"] = 100
              }
          },
          ["required"] = new[] { "query" }
      };

    public IList<string> RequiredRoles => new List<string> { "user" };

      /// <summary>
      /// 執行網頁搜尋。
      /// </summary>
    public async Task<object?> ExecuteAsync(JsonElement? input, CancellationToken cancellationToken = default)
       {
        var queryElement = input?.ValueKind == JsonValueKind.Object && input.Value.TryGetProperty("query", out var queryProp)
              ? queryProp.GetString()
             : null;

        if (string.IsNullOrWhiteSpace(queryElement))
          {
            return new { success = false, error = "查詢字串為空白。" };
          }

        var engineName = input?.ValueKind == JsonValueKind.Object && input.Value.TryGetProperty("engine", out var engineProp)
              ? engineProp.GetString()
             : null;

        var maxResults = 10;
        if (input?.ValueKind == JsonValueKind.Object && input.Value.TryGetProperty("maxResults", out var maxProp))
          {
              try { maxResults = maxProp.GetInt32(); }
              catch { maxResults = 10; }
              maxResults = Math.Clamp(maxResults, 1, 100);
          }

         // 選擇搜尋引擎：指定名稱優先，否則使用第一個（或預設）。
         var engine = !string.IsNullOrWhiteSpace(engineName)
              ? _searchEngines.FirstOrDefault(e => string.Equals(e.Name, engineName, StringComparison.OrdinalIgnoreCase))
             : _searchEngines.FirstOrDefault(e => e.IsDefault) ?? _searchEngines.FirstOrDefault();

        if (engine is null)
          {
            return new { success = false, error = "找不到可用的搜尋引擎。" };
          }

        try
          {
              var results = await engine.SearchAsync(queryElement, cancellationToken);
              return new
              {
                  success = true,
                  query = queryElement,
                  engine = engine.Name,
                  total = results.Count,
                  results = results.Select(r => new
                  {
                      r.Title,
                      r.Url,
                      r.Description,
                      r.SourceEngine
                  }).ToList()
              };
          }
        catch (Exception ex)
          {
            return new { success = false, error = $"搜尋失敗: {ex.Message}" };
          }
      }
}