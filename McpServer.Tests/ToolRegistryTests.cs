using System.Text.Json;
using McpServer.Interfaces;
using McpServer.Services;
using Microsoft.Extensions.Configuration;

namespace McpServer.Tests;

public class ToolRegistryTests
{
    [Fact]
    public async Task ExecuteAsync_WithSearchDocsTool_ReturnsSuccess()
    {
        var registry = new ToolRegistry(new ITool[] { new SearchDocsTool() }, new TestAuditLogger());

        var response = await registry.ExecuteAsync("search_docs", JsonSerializer.SerializeToElement(new { query = "rag" }));

        Assert.True(response.Success);
        Assert.Equal("search_docs", response.ToolId);
    }

    [Fact]
    public async Task PostProcessor_WithToolCallJson_ExecutesTool()
    {
        var registry = new ToolRegistry(new ITool[] { new SearchDocsTool() }, new TestAuditLogger());
        var processor = new PostProcessor(registry);

        var response = await processor.ProcessAsync("{\"action\":\"CALL_TOOL\",\"tool_id\":\"search_docs\",\"input\":{\"query\":\"rag\"}}", "admin");

        Assert.True(response.Success);
        Assert.Equal("search_docs", response.ToolId);
    }

    [Fact]
    public async Task MarkdownIngestor_WithDirectoryPath_IngestsFiles()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var samplePath = Path.Combine(tempDirectory, "sample.md");
        await File.WriteAllTextAsync(samplePath, "# Demo\n\nThis is a test.");

        try
        {
            var ingestor = new MarkdownIngestor(new SimpleChunker());
            var chunks = await ingestor.IngestAsync(tempDirectory);

            Assert.NotEmpty(chunks);
            Assert.Contains(chunks, chunk => chunk.Content.Contains("test", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private sealed class TestAuditLogger : McpServer.Infrastructure.AuditLogger
    {
        public TestAuditLogger() : base(new ConfigurationBuilder().AddInMemoryCollection().Build())
        {
        }
    }
}
