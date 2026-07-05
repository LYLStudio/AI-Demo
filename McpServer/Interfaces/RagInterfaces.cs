using System.Text.Json;
using McpServer.Models;

namespace McpServer.Interfaces;

/// <summary>
/// 定義一個可由 MCP 工具註冊表執行的工具。
/// </summary>
public interface ITool
{
    /// <summary>
    /// 取得工具識別碼。
    /// </summary>
    string Id { get; }

    /// <summary>
    /// 取得工具說明文字。
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 取得工具輸入的 JSON schema。
    /// </summary>
    Dictionary<string, object?> Schema { get; }

    /// <summary>
    /// 取得執行工具所需的角色集合。
    /// </summary>
    IList<string> RequiredRoles { get; }

    /// <summary>
    /// 依據輸入執行工具。
    /// </summary>
    Task<object?> ExecuteAsync(JsonElement? input, CancellationToken cancellationToken = default);
}

/// <summary>
/// 定義 MCP 工具註冊與執行的核心入口。
/// </summary>
public interface IToolRegistry
{
    /// <summary>
    /// 列出所有已註冊工具的描述。
    /// </summary>
    IReadOnlyList<ToolDescriptor> ListTools();

    /// <summary>
    /// 依據工具 ID 取得工具實例。
    /// </summary>
    bool TryGetTool(string toolId, out ITool? tool);

    /// <summary>
    /// 執行指定工具。
    /// </summary>
    Task<CallToolResponse> ExecuteAsync(string toolId, JsonElement? input, string? user = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// 定義文件內容攝取流程的抽象介面。
/// </summary>
public interface IIngestor
{
    /// <summary>
    /// 取得支援的來源類型。
    /// </summary>
    string SupportedType { get; }

    /// <summary>
    /// 判斷此攝取器是否能處理指定類型。
    /// </summary>
    bool CanHandle(string ingestionType);

    /// <summary>
    /// 將指定來源讀入並轉為文件片段集合。
    /// </summary>
    Task<IReadOnlyList<DocChunk>> IngestAsync(string sourcePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// 定義文件分塊器的抽象介面。
/// </summary>
public interface IChunker
{
    /// <summary>
    /// 將內容切割為多個文件片段。
    /// </summary>
    IReadOnlyList<DocChunk> Chunk(string content, string source, int maxTokens = 256);
}

/// <summary>
/// 定義向量嵌入生成器的抽象介面。
/// </summary>
public interface IEmbedder
{
    /// <summary>
    /// 依據文字內容建立向量嵌入。
    /// </summary>
    Task<List<float>> EmbedAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>
/// 定義檢索器的抽象介面。
/// </summary>
public interface IRetriever
{
    /// <summary>
    /// 將文件片段加入檢索庫。
    /// </summary>
    Task AddAsync(IEnumerable<DocChunk> chunks, CancellationToken cancellationToken = default);

    /// <summary>
    /// 依據查詢字串回傳最相近的片段。
    /// </summary>
    Task<IReadOnlyList<DocChunk>> SearchAsync(string query, int topK = 5, CancellationToken cancellationToken = default);
}

/// <summary>
/// 定義呼叫 Ollama 模型的抽象介面。
/// </summary>
public interface ILLMAdapter
{
    /// <summary>
    /// 根據 prompt 產生模型回應。
    /// </summary>
    Task<string> GenerateAsync(string prompt, string? model = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// 定義建立 RAG prompt 的抽象介面。
/// </summary>
public interface IContextBuilder
{
    /// <summary>
    /// 建立包含 system instructions、使用者查詢與上下文的 prompt。
    /// </summary>
    string BuildPrompt(string userQuery, IReadOnlyList<DocChunk> chunks);
}

/// <summary>
/// 定義模型回應後處理的抽象介面。
/// </summary>
public interface IPostProcessor
{
    /// <summary>
    /// 對模型輸出進行解析與執行工具呼叫。
    /// </summary>
    Task<CallToolResponse> ProcessAsync(string modelResponse, string? user, CancellationToken cancellationToken = default);
}
