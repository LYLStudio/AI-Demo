using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace OllamaAgentDemo.Tools;

public class StockInfoTool : ITool
{
    private readonly HttpClient _httpClient;

    public StockInfoTool(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public string Name => "StockInfo";

    public async Task<string> ExecuteAsync(string stockIdentifier)
    {
        try
        {
            string url = $"https://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch={stockIdentifier}";
            return await _httpClient.GetStringAsync(url);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }
}