using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIConsole.Models;
using AIConsole.Services;

// 1. Setup Configuration and Services
string configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
var configService = new ConfigService(configFilePath);
var chatConfig = configService.GetConfig();

// Determine if we should show thinking based on command line args
bool isThinkingEnabled = chatConfig.ShowThinking;
string[] cmdArgs = Environment.GetCommandLineArgs();
foreach (var arg in cmdArgs)
{
    if (arg == "--no-think") isThinkingEnabled = false;
}

// 2. Initialize Registry and MCP client
var toolRegistry = new ToolRegistry();
McpClientService? mcpClient = null;
McpServerInfo? serverInfo = null;

var candidateUrls = new[]
{
    chatConfig.McpServerBaseUrl,
    chatConfig.BaseUrl,
    "http://127.0.0.1:5000",
    "http://127.0.0.1:5080",
    "http://localhost:5000",
    "http://localhost:5080"
}
.Where(url => !string.IsNullOrWhiteSpace(url))
.Select(url => url!.TrimEnd('/'))
.Distinct(StringComparer.OrdinalIgnoreCase)
.ToList();

foreach (var candidateUrl in candidateUrls)
{
    try
    {
        var candidateClient = new McpClientService(candidateUrl);
        serverInfo = await candidateClient.InitializeAsync();
        mcpClient = candidateClient;
        Console.WriteLine($"[MCP] Connected to {serverInfo?.Server ?? "McpServer"} v{serverInfo?.Version ?? "unknown"} at {candidateUrl}");
        break;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[MCP] Unable to connect to {candidateUrl}: {ex.Message}");
    }
}

if (mcpClient is null || serverInfo is null)
{
    throw new InvalidOperationException("Unable to connect to McpServer. Please start the McpServer project and verify the URL.");
}

var tools = await mcpClient.ListToolsAsync();
foreach (var tool in tools)
{
    toolRegistry.RegisterTool(new McpToolProxy(tool, mcpClient));
}

// 3. Create Chat Service
using var chatService = new ChatService(chatConfig, toolRegistry);

Console.WriteLine("AIConsole Raw HttpClient streaming demo (think=high)");
Console.WriteLine("Type 'exit' or 'quit' to end the session.");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    var history = new List<ChatMessage>
    {
        new ChatMessage("system", "You are a helpful assistant. Please always respond in Traditional Chinese (繁體中文).")
    };

    while (true)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("\n[您的問題]: ");
        Console.ResetColor();
        
        var userInput = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(userInput)) continue;
        if (userInput.Equals("exit", StringComparison.OrdinalIgnoreCase) || 
            userInput.Equals("quit", StringComparison.OrdinalIgnoreCase)) break;

        history.Add(new ChatMessage("user", userInput));

        if (await TryHandleStockQuery(userInput, toolRegistry, history, cts.Token))
        {
            continue;
        }

        bool turnCompleted = false;

        while (!turnCompleted && !cts.IsCancellationRequested)
        {
            ToolCall? capturedToolCall = null;

            await chatService.StreamChatAsync(history, cts.Token, (state, element) => 
            {
                // Check for tool call in the current chunk
                if (element.TryGetProperty("tool_call", out var tc))
                {
                    capturedToolCall = new ToolCall
                    {
                        Name = tc.GetProperty("name").GetString() ?? "",
                        Arguments = new Dictionary<string, object>()
                    };
                    if (tc.TryGetProperty("arguments", out var argsEl))
                    {
                        foreach(var prop in argsEl.EnumerateObject())
                        {
                            capturedToolCall.Arguments[prop.Name] = prop.Value.ToString(); 
                        }
                    }
                }

                ProcessChunkInternal(state, element, isThinkingEnabled);
            }, () => 
            {
                Console.WriteLine("\n[Stream Ended]");
            });

            if (capturedToolCall != null)
            {
                var tool = toolRegistry.GetTool(capturedToolCall.Name);
                if (tool != null)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\n[工具呼叫]: {capturedToolCall.Name}({string.Join(", ", capturedToolCall.Arguments.Select(kv => $"{kv.Key}={kv.Value}"))})");
                    Console.ResetColor();

                    var result = await tool.ExecuteAsync(capturedToolCall.Arguments);
                    history.Add(new ChatMessage("tool", result.Content));
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n[Error] Tool '{capturedToolCall.Name}' not found.");
                    Console.ResetColor();
                    turnCompleted = true; 
                }
            }
            else
            {
                turnCompleted = true; // No tool call, end the turn
            }
        }
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("\nCancelled.");
}
catch (Exception ex)
{
    Console.WriteLine($"\nAn error occurred: {ex.Message}");
}

async Task<bool> TryHandleStockQuery(string userInput, ToolRegistry toolRegistry, List<ChatMessage> history, CancellationToken cancellationToken)
{
    var normalized = userInput.Trim();
    if (string.IsNullOrWhiteSpace(normalized))
    {
        return false;
    }

    var intent = ParseStockQuery(normalized);
    if (!intent.ShouldQuery || string.IsNullOrWhiteSpace(intent.Symbol))
    {
        return false;
    }

    var tool = toolRegistry.GetTool("stock_info");
    if (tool == null)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("\n[MCP] 未找到 stock_info 工具。請確認 McpServer 已註冊該工具。" );
        Console.ResetColor();
        return false;
    }

    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"\n[工具呼叫]: stock_info(symbol={intent.Symbol})");
    Console.ResetColor();

    var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["symbol"] = intent.Symbol });
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"[工具結果] {result.Content}");
    Console.ResetColor();
    history.Add(new ChatMessage("tool", result.Content));

    if (intent.IsPurchase && intent.Quantity.HasValue)
    {
        var parsed = TryExtractPriceAndName(result.Content);
        if (parsed.Price.HasValue)
        {
            var shares = intent.Quantity.Value * 1000;
            var total = parsed.Price.Value * shares;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[計算] {intent.Quantity.Value} 張 × 1000 股/張 × {parsed.Price.Value:F2} 元/股 = 約 {total:N0} 元（未含手續費與稅費）");
            Console.ResetColor();
            history.Add(new ChatMessage("assistant", $"以目前股價約 {parsed.Price.Value:F2} 元/股，買 {intent.Quantity.Value} 張（{shares:N0} 股）約需 {total:N0} 元，未含手續費與稅費。"));
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[計算] 無法從股票查詢結果中取得價格，請稍後再試。");
            Console.ResetColor();
        }
    }

    return true;
}

static (bool ShouldQuery, string? Symbol, bool IsPurchase, int? Quantity) ParseStockQuery(string input)
{
    var normalized = input.Trim();
    if (string.IsNullOrWhiteSpace(normalized))
    {
        return (false, null, false, null);
    }

    var hasStockIntent = normalized.Contains("股價", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("股票", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("台股", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("stock price", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("stock", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("買", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("購買", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("買入", StringComparison.OrdinalIgnoreCase);

    if (!hasStockIntent)
    {
        return (false, null, false, null);
    }

    var codeMatch = Regex.Match(normalized, @"(?<!\d)(\d{4,5})(?!\d)");
    var symbol = codeMatch.Success ? codeMatch.Groups[1].Value : null;

    if (string.IsNullOrWhiteSpace(symbol))
    {
        var knownSymbols = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["台積電"] = "2330",
            ["tsmc"] = "2330",
            ["鴻海"] = "2317",
            ["聯電"] = "2303",
            ["世界先進"] = "5347"
        };

        foreach (var (name, knownSymbol) in knownSymbols)
        {
            if (normalized.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                symbol = knownSymbol;
                break;
            }
        }
    }

    var quantity = ParseLotQuantity(normalized);
    var isPurchase = (normalized.Contains("買", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("購買", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("買入", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("多少錢", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("總價", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("價錢", StringComparison.OrdinalIgnoreCase))
        && quantity.HasValue;

    return (string.IsNullOrWhiteSpace(symbol) ? false : true, symbol, isPurchase, quantity);
}

static int? ParseLotQuantity(string input)
{
    var match = Regex.Match(input, @"(?<qty>[\d零一二三四五六七八九十]+)\s*張", RegexOptions.IgnoreCase);
    if (match.Success)
    {
        return ParseChineseNumber(match.Groups["qty"].Value);
    }

    match = Regex.Match(input, @"(?:買|購買|買入)\s*(?<qty>[\d零一二三四五六七八九十]+)\s*張", RegexOptions.IgnoreCase);
    if (match.Success)
    {
        return ParseChineseNumber(match.Groups["qty"].Value);
    }

    return null;
}

static int ParseChineseNumber(string input)
{
    if (int.TryParse(input, out var numeric))
    {
        return numeric;
    }

    var mapping = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["零"] = 0,
        ["一"] = 1,
        ["二"] = 2,
        ["兩"] = 2,
        ["三"] = 3,
        ["四"] = 4,
        ["五"] = 5,
        ["六"] = 6,
        ["七"] = 7,
        ["八"] = 8,
        ["九"] = 9,
        ["十"] = 10
    };

    return mapping.TryGetValue(input, out var value) ? value : 0;
}

static (decimal? Price, string? CompanyName) TryExtractPriceAndName(string content)
{
    try
    {
        using var doc = JsonDocument.Parse(content);
        if (!doc.RootElement.TryGetProperty("result", out var resultElement))
        {
            return (null, null);
        }

        if (!resultElement.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
        {
            return (null, null);
        }

        foreach (var item in dataElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            decimal? price = null;
            if (item.TryGetProperty("z", out var priceElement) && priceElement.ValueKind == JsonValueKind.String)
            {
                decimal.TryParse(priceElement.GetString(), out var parsedPrice);
                price = parsedPrice;
            }
            else if (item.TryGetProperty("z", out var priceNumericElement) && priceNumericElement.ValueKind == JsonValueKind.Number)
            {
                price = priceNumericElement.GetDecimal();
            }

            string? companyName = null;
            if (item.TryGetProperty("n", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
            {
                companyName = nameElement.GetString();
            }
            if (string.IsNullOrWhiteSpace(companyName) && item.TryGetProperty("nf", out var fullNameElement) && fullNameElement.ValueKind == JsonValueKind.String)
            {
                companyName = fullNameElement.GetString();
            }

            return (price, companyName);
        }
    }
    catch
    {
        return (null, null);
    }

    return (null, null);
}

void ProcessChunkInternal(StreamState state, JsonElement root, bool isThinkingEnabled)
{
    if (root.TryGetProperty("message", out var message))
    {
        // Handle Thinking
        if (message.TryGetProperty("thinking", out var thinkingEl) && thinkingEl.ValueKind == JsonValueKind.String)
        {
            var thinking = thinkingEl.GetString();
            if (!string.IsNullOrEmpty(thinking))
            {
                if (!state.IsThinkingPrinted)
                {
                    state.IsThinkingPrinted = true;
                    if (isThinkingEnabled)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write("[思考] "); 
                        Console.Write(thinking);
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write("[思考] 思考中...");
                    }
                }
                else if (isThinkingEnabled)
                {
                    Console.Write(thinking);
                }
            }
        }

        // Handle Content
        if (message.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
        {
            var content = contentEl.GetString();
            if (!string.IsNullOrEmpty(content))
            {
                if (state.IsThinkingPrinted)
                {
                    Console.WriteLine(); 
                    state.IsThinkingPrinted = false; 
                }

                if (!state.IsContentPrinted)
                {
                    Console.ForegroundColor = ConsoleColor.Green; 
                    Console.Write("[答案] "); 
                    state.IsContentPrinted = true;
                }
                Console.Write(content);
            }
        }
        Console.ResetColor();
    }
}