namespace McpServer.Models;

/// <summary>
/// Ollama 相關設定，包含模型名稱與服務端點。
/// </summary>
public class OllamaSettings
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "gemma4:12b-mlx";
    public bool UseMock { get; set; } = false;
}

/// <summary>
/// 股票資訊 API 的設定。
/// </summary>
public class StockApiSettings
{
    public string BaseUrl { get; set; } = "https://mis.twse.com.tw/stock/api/getStockInfo.jsp";
}
