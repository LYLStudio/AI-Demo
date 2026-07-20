using McpServer.Models;

namespace McpServer.Interfaces;

/// <summary>
/// 提供來自不同搜尋引擎之搜尋能力抽象。
/// </summary>
public interface IWebSearchEngine
{
     /// <summary>
      /// 取得搜尋引擎識別碼。
      /// </summary>
    string Name { get; }

      /// <summary>
      /// 判斷此搜尋引擎是否預設啟用。
      /// </summary>
    bool IsDefault { get; }

      /// <summary>
      /// 依據查詢字串執行網頁搜尋。
      /// </summary>
    Task<IReadOnlyList<SearchResultItem>> SearchAsync(string query, CancellationToken cancellationToken = default);
}

/// <summary>
/// 提供爬蟲偵測防範策略抽象。
/// </summary>
public interface IAntiBotStrategy
{
      /// <summary>
      /// 取得策略識別碼。
      /// </summary>
    string StrategyName { get; }

       /// <summary>
       /// 在發送請求前修改 HttpRequestMessage 標頭以模擬真實瀏覽器。
       /// </summary>
    void ApplyHeaders(HttpRequestMessage request);

        /// <summary>
        /// 計算兩次請求之間之隨機延遲時間（毫秒）。
        /// </summary>
    int ComputeDelayMs();

        /// <summary>
        /// 增加請求計數並回傳（供重試退避使用）。
        /// </summary>
    int IncrementAndGetCount();
}
