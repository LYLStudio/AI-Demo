using System.Text;
using System.Text.Json;
using McpServer.Interfaces;
using McpServer.Models;

namespace McpServer.Services;

/// <summary>
/// 將 Markdown 內容載入為文件片段。
/// </summary>
public class MarkdownIngestor : IIngestor
{
    private readonly IChunker _chunker;

    public MarkdownIngestor(IChunker chunker)
    {
        _chunker = chunker;
    }

    public string SupportedType => "markdown";

    public bool CanHandle(string ingestionType) => string.Equals(ingestionType, SupportedType, StringComparison.OrdinalIgnoreCase);

    public Task<IReadOnlyList<DocChunk>> IngestAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        var files = ExpandPaths(sourcePath);
        var chunks = new List<DocChunk>();
        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            chunks.AddRange(_chunker.Chunk(content, file));
        }

        return Task.FromResult<IReadOnlyList<DocChunk>>(chunks);
    }

    private static IReadOnlyList<string> ExpandPaths(string sourcePath)
    {
        if (File.Exists(sourcePath))
        {
            return new[] { sourcePath };
        }

        if (Directory.Exists(sourcePath))
        {
            return Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories).ToList();
        }

        throw new FileNotFoundException($"The source path '{sourcePath}' does not exist.");
    }
}

/// <summary>
/// 將 JSON 內容載入為文件片段。
/// </summary>
public class JsonIngestor : IIngestor
{
    private readonly IChunker _chunker;

    public JsonIngestor(IChunker chunker)
    {
        _chunker = chunker;
    }

    public string SupportedType => "json";

    public bool CanHandle(string ingestionType) => string.Equals(ingestionType, SupportedType, StringComparison.OrdinalIgnoreCase);

    public Task<IReadOnlyList<DocChunk>> IngestAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        var files = ExpandPaths(sourcePath);
        var chunks = new List<DocChunk>();
        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            chunks.AddRange(_chunker.Chunk(content, file));
        }

        return Task.FromResult<IReadOnlyList<DocChunk>>(chunks);
    }

    private static IReadOnlyList<string> ExpandPaths(string sourcePath)
    {
        if (File.Exists(sourcePath))
        {
            return new[] { sourcePath };
        }

        if (Directory.Exists(sourcePath))
        {
            return Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories).ToList();
        }

        throw new FileNotFoundException($"The source path '{sourcePath}' does not exist.");
    }
}

/// <summary>
/// PDF 內容的文字抽取 stub。
/// </summary>
public class PdfIngestor : IIngestor
{
    private readonly IChunker _chunker;

    public PdfIngestor(IChunker chunker)
    {
        _chunker = chunker;
    }

    public string SupportedType => "pdf";

    public bool CanHandle(string ingestionType) => string.Equals(ingestionType, SupportedType, StringComparison.OrdinalIgnoreCase);

    public Task<IReadOnlyList<DocChunk>> IngestAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        var files = ExpandPaths(sourcePath);
        var chunks = new List<DocChunk>();
        foreach (var file in files)
        {
            var content = $"[PDF stub] {Path.GetFileName(file)}";
            chunks.AddRange(_chunker.Chunk(content, file));
        }

        return Task.FromResult<IReadOnlyList<DocChunk>>(chunks);
    }

    private static IReadOnlyList<string> ExpandPaths(string sourcePath)
    {
        if (File.Exists(sourcePath))
        {
            return new[] { sourcePath };
        }

        if (Directory.Exists(sourcePath))
        {
            return Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories).ToList();
        }

        throw new FileNotFoundException($"The source path '{sourcePath}' does not exist.");
    }
}

/// <summary>
/// 基於字數的分塊器。
/// </summary>
public class SimpleChunker : IChunker
{
    public IReadOnlyList<DocChunk> Chunk(string content, string source, int maxTokens = 256)
    {
        var words = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<DocChunk>();
        for (var index = 0; index < words.Length; index += Math.Max(1, maxTokens / 2))
        {
            var section = words.Skip(index).Take(Math.Max(1, maxTokens / 2)).ToArray();
            if (section.Length == 0)
            {
                break;
            }

            chunks.Add(new DocChunk
            {
                Source = source,
                Content = string.Join(' ', section),
                Metadata = new Dictionary<string, object?> { ["length"] = section.Length }
            });
        }

        return chunks;
    }
}

/// <summary>
/// 產生隨機向量的嵌入器 stub。
/// </summary>
public class RandomEmbedder : IEmbedder
{
    private readonly Random _random = new();

    public Task<List<float>> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Enumerable.Range(0, 8).Select(_ => (float)(_random.NextDouble() * 2 - 1)).ToList());
    }
}

/// <summary>
/// 使用記憶體陣列實作的簡易檢索器。
/// </summary>
public class InMemoryRetriever : IRetriever
{
    private readonly List<DocChunk> _chunks = new();

    public Task AddAsync(IEnumerable<DocChunk> chunks, CancellationToken cancellationToken = default)
    {
        _chunks.AddRange(chunks);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DocChunk>> SearchAsync(string query, int topK = 5, CancellationToken cancellationToken = default)
    {
        var results = _chunks
            .Where(chunk => chunk.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(topK)
            .ToList();

        return Task.FromResult<IReadOnlyList<DocChunk>>(results);
    }
}

/// <summary>
/// 呼叫本機 Ollama 的適配器，若沒有服務則回退為 mock。
/// </summary>
public class OllamaClient : ILLMAdapter
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public OllamaClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _baseUrl = configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
    }

    public async Task<string> GenerateAsync(string prompt, string? model = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var requestBody = new
            {
                model = model ?? "gemma4:31b-mlx",
                prompt,
                stream = false
            };

            using var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/generate", requestBody, cancellationToken);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, object?>>(cancellationToken: cancellationToken);
            return payload?.GetValueOrDefault("response")?.ToString() ?? "";
        }
        catch
        {
            return "{\"action\":\"CALL_TOOL\",\"tool_id\":\"search_docs\",\"input\":{\"query\":\"mock response\"}}";
        }
    }
}

/// <summary>
/// 將上下文資訊組合為可送給模型的 prompt。
/// </summary>
public class ContextBuilder : IContextBuilder
{
    public string BuildPrompt(string userQuery, IReadOnlyList<DocChunk> chunks)
    {
        var joined = string.Join(Environment.NewLine, chunks.Select(chunk => $"- {chunk.Content}"));
        return $"System: 你是遵循 MCP 的助理。當需要外部資料或操作時，請回傳 JSON 格式的 CALL_TOOL 指令。\nUser: {userQuery}\nContext: {joined}\n如果要呼叫工具，回傳：\n{{\"action\":\"CALL_TOOL\",\"tool_id\":\"<tool_id>\",\"input\":{{...}}}}\n否則直接回覆答案。\n";
    }
}

/// <summary>
/// 對模型回應進行後處理，辨識工具呼叫與標準回答。
/// </summary>
public class PostProcessor : IPostProcessor
{
    private readonly IToolRegistry _toolRegistry;

    public PostProcessor(IToolRegistry toolRegistry)
    {
        _toolRegistry = toolRegistry;
    }

    public async Task<CallToolResponse> ProcessAsync(string modelResponse, string? user, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelResponse))
        {
            return new CallToolResponse { Success = false, Error = "Model responded with an empty output." };
        }

        try
        {
            using var document = JsonDocument.Parse(modelResponse);
            if (document.RootElement.TryGetProperty("action", out var action) && action.GetString() == "CALL_TOOL")
            {
                var toolId = document.RootElement.GetProperty("tool_id").GetString() ?? string.Empty;
                JsonElement? input = document.RootElement.TryGetProperty("input", out var inputElement) ? inputElement : (JsonElement?)null;
                return await _toolRegistry.ExecuteAsync(toolId, input, user, cancellationToken);
            }
        }
        catch
        {
            return new CallToolResponse { Success = false, Error = "Model response was not valid JSON." };
        }

        return new CallToolResponse { Success = true, Details = modelResponse };
    }
}
