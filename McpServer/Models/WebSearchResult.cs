namespace McpServer.Models;

/// <summary>
/// 搜尋引擎回傳之單一搜尋結果項目。
/// </summary>
public class SearchResultItem
{
     /// <summary>
     /// 頁面標題。
     /// </summary>
    public string? Title { get; set; }

     /// <summary>
     /// 頁面 URL。
     /// </summary>
    public string? Url { get; set; }

     /// <summary>
     /// 頁面摘要 / 描述。
     /// </summary>
    public string? Description { get; set; }

     /// <summary>
     /// 來源搜尋引擎名稱。
     /// </summary>
    public string? SourceEngine { get; set; }
}

/// <summary>
/// 搜尋結果集合封裝。
/// </summary>
public class WebSearchResult
{
     /// <summary>
     /// 原始查詢字串。
     /// </summary>
    public string? Query { get; set; }

     /// <summary>
     /// 搜尋結果項目清單。
     /// </summary>
    public List<SearchResultItem> Results { get; set; } = new();

     /// <summary>
     /// 總結果數（由搜尋引擎回傳）。
     /// </summary>
    public int TotalCount { get; set; }

     /// <summary>
     /// 使用的搜尋引擎名稱。
     /// </summary>
    public string? EngineUsed { get; set; }
}