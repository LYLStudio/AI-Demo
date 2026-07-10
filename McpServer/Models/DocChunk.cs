namespace McpServer.Models;

/// <summary>
/// 表示一個已分塊、可供 RAG 檢索的文件片段。
/// </summary>
public class DocChunk
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Source { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public Dictionary<string, object?> Metadata { get; set; } = new();
    public List<float> Embedding { get; set; } = new();
}