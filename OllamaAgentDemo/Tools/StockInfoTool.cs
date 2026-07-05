using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

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
            // 嘗試依照「上市 -> 上櫃」的順序進行搜尋
            string[] markets = { "tse", "otc" };
            foreach (var market in markets)
            {
                string candidate = $"{market}_{stockIdentifier}.tw";
                // string result = await FetchDataAsync(candidate);
                var stockInfoResponse = await FetchStockInfoAsync(candidate);
                if(stockInfoResponse.MsgArray != null && stockInfoResponse.MsgArray.Count > 0)
                {
                    var stockMessage = stockInfoResponse.MsgArray[0];
                    if(stockMessage.Code?.Trim() != stockIdentifier.Trim())
                    {
                        continue; // 如果股票代碼不匹配，跳過這個結果
                    }

                    return System.Text.Json.JsonSerializer.Serialize(new { stockMessage.ChannelId, stockMessage.Name, stockMessage.FullName, stockMessage.Open, stockMessage.Close, stockMessage.High, stockMessage.Low, stockMessage.Volume });
                    
                }
            }

            return $"Error: 無法找到商品 {stockIdentifier} 的資訊 (已嘗試搜尋上市與上櫃市場)。";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private async Task<string> FetchDataAsync(string identifier)
    {
        string url = $"https://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch={identifier}";
        return await _httpClient.GetStringAsync(url);
    }

    private async Task<StockInfoResponse> FetchStockInfoAsync(string identifier)
    {
        string url = $"https://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch={identifier}";
        return await _httpClient.GetFromJsonAsync<StockInfoResponse>(url);
    }
}

public class StockInfoResponse
{
    [JsonPropertyName("msgArray")]
    public List<StockMessage> MsgArray { get; set; }

    [JsonPropertyName("referer")]
    public string Referer { get; set; }

    [JsonPropertyName("userDelay")]
    public int UserDelay { get; set; }

    [JsonPropertyName("rtcode")]
    public string RtCode { get; set; }

    [JsonPropertyName("queryTime")]
    public QueryTimeInfo QueryTime { get; set; }

    [JsonPropertyName("rtmessage")]
    public string RtMessage { get; set; }

    [JsonPropertyName("exKey")]
    public string ExKey { get; set; }

    [JsonPropertyName("cachedAlive")]
    public int CachedAlive { get; set; }
}

public class QueryTimeInfo
{
    [JsonPropertyName("sysDate")]
    public string SysDate { get; set; }

    [JsonPropertyName("stockInfoItem")]
    public int StockInfoItem { get; set; }

    [JsonPropertyName("stockInfo")]
    public int StockInfo { get; set; }

    [JsonPropertyName("sessionStr")]
    public string SessionStr { get; set; }

    [JsonPropertyName("sysTime")]
    public string SysTime { get; set; }

    [JsonPropertyName("showChart")]
    public bool ShowChart { get; set; }

    [JsonPropertyName("sessionFromTime")]
    public int SessionFromTime { get; set; }

    [JsonPropertyName("sessionLatestTime")]
    public int SessionLatestTime { get; set; }
}

public class StockMessage
{
    [JsonPropertyName("@")]
    public string ChannelId2 { get; set; } // 頻道識別代號（通常與 ch 相同）

    [JsonPropertyName("tv")]
    public string Tv { get; set; } // 當盤成交量（單筆成交張數）

    [JsonPropertyName("ps")]
    public string Ps { get; set; } // 盤後暫估成交量

    [JsonPropertyName("pid")]
    public string Pid { get; set; } // 委託統計流程識別碼

    [JsonPropertyName("pz")]
    public string Pz { get; set; } // 盤後暫估成交價

    [JsonPropertyName("bp")]
    public string Bp { get; set; } // 揭示暫存狀態旗標

    [JsonPropertyName("fv")]
    public string Fv { get; set; } // 擬開盤/擬收盤成交量（試撮合張數）

    [JsonPropertyName("oa")]
    public string Oa { get; set; } // 擬開盤/擬收盤最佳賣出價

    [JsonPropertyName("ob")]
    public string Ob { get; set; } // 擬開盤/擬收盤最佳買進價

    [JsonPropertyName("m%")]
    public string MPercent { get; set; } // 市值佔比百分比指標（多為內部參考）

    [JsonPropertyName("^")]
    public string DateSnapshot { get; set; } // 快照快取日期

    [JsonPropertyName("key")]
    public string Key { get; set; } // 查詢檢索鍵（格式如：tse_2317.tw_日期）

    [JsonPropertyName("a")]
    public string BestAskPrices { get; set; } // 最佳五檔「賣出」委託價格（低到高，以 _ 分隔）

    [JsonPropertyName("b")]
    public string BestBidPrices { get; set; } // 最佳五檔「買進」委託價格（高到低，以 _ 分隔）

    [JsonPropertyName("c")]
    public string Code { get; set; } // 股票代號

    [JsonPropertyName("#")]
    public string HashId { get; set; } // 交易所內部流通撮合編碼

    [JsonPropertyName("d")]
    public string Date { get; set; } // 最近交易日期（YYYYMMDD）

    [JsonPropertyName("%")]
    public string TimeSnapshot { get; set; } // 盤後資料快照時間

    [JsonPropertyName("ch")]
    public string ChannelId { get; set; } // 股票代碼加上國家尾綴（如 2317.tw）

    [JsonPropertyName("tlong")]
    public string Tlong { get; set; } // Epoch 毫秒時間戳記

    [JsonPropertyName("ot")]
    public string Ot { get; set; } // 擬開/擬收撮合時間

    [JsonPropertyName("f")]
    public string BestAskVolumes { get; set; } // 最佳五檔「賣出」委託量（張數，對應 a）

    [JsonPropertyName("g")]
    public string BestBidVolumes { get; set; } // 最佳五檔「買進」委託量（張數，對應 b）

    [JsonPropertyName("ip")]
    public string Ip { get; set; } // 處置股票或特殊盤狀態旗標（0為正常）

    [JsonPropertyName("mt")]
    public string Mt { get; set; } // 市場交易機制標記

    [JsonPropertyName("ov")]
    public string Ov { get; set; } // 盤前/試撮累積成交總量

    [JsonPropertyName("h")]
    public string High { get; set; } // 今日最高價

    [JsonPropertyName("i")]
    public string InfoIndex { get; set; } // 交易所揭示序號

    [JsonPropertyName("it")]
    public string InfoType { get; set; } // 揭示種類代碼

    [JsonPropertyName("oz")]
    public string Oz { get; set; } // 擬開盤/擬收盤成交價（試撮價格）

    [JsonPropertyName("l")]
    public string Low { get; set; } // 今日最低價

    [JsonPropertyName("n")]
    public string Name { get; set; } // 股票簡稱

    [JsonPropertyName("o")]
    public string Open { get; set; } // 今日開盤價

    [JsonPropertyName("p")]
    public string P { get; set; } // 暫停交易/延時撮合狀態標記

    [JsonPropertyName("ex")]
    public string Exchange { get; set; } // 交易所類型（tse:上市、otc:上櫃）

    [JsonPropertyName("s")]
    public string S { get; set; } // 當盤累積成交量

    [JsonPropertyName("t")]
    public string Time { get; set; } // 最近成交時間（HH:mm:ss）

    [JsonPropertyName("u")]
    public string LimitUp { get; set; } // 今日漲停價

    [JsonPropertyName("v")]
    public string Volume { get; set; } // 今日累積總成交量（張數）

    [JsonPropertyName("w")]
    public string LimitDown { get; set; } // 今日跌停價

    [JsonPropertyName("nf")]
    public string FullName { get; set; } // 公司全名

    [JsonPropertyName("y")]
    public string YesterdayClose { get; set; } // 昨日收盤價（平盤參考價）

    [JsonPropertyName("z")]
    public string Close { get; set; } // 當盤成交價格（最新價）

    [JsonPropertyName("ts")]
    public string Ts { get; set; } // 撮合序號
}
