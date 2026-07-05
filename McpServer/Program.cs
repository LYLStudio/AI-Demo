using McpServer.Infrastructure;
using McpServer.Interfaces;
using McpServer.Models;
using McpServer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "McpServer API";
        document.Info.Version = "v1";
        document.Info.Description = "A .NET 10 MCP-style server with a RAG pipeline and local Ollama integration.";
        return Task.CompletedTask;
    });
});
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.Configure<OllamaSettings>(builder.Configuration.GetSection("Ollama"));
builder.Services.Configure<StockApiSettings>(builder.Configuration.GetSection("StockApi"));

builder.Services.AddSingleton<AuditLogger>();
builder.Services.AddSingleton<IToolRegistry, ToolRegistry>();
builder.Services.AddSingleton<ITool, SearchDocsTool>();
builder.Services.AddSingleton<ITool, ReadFileTool>();
builder.Services.AddSingleton<ITool, HttpRequestTool>();
builder.Services.AddSingleton<ITool, RunQueryTool>();
builder.Services.AddSingleton<ITool, StockInfoTool>();
builder.Services.AddSingleton<IChunker, SimpleChunker>();
builder.Services.AddSingleton<IEmbedder, RandomEmbedder>();
builder.Services.AddSingleton<IRetriever, InMemoryRetriever>();
builder.Services.AddSingleton<ILLMAdapter, OllamaClient>();
builder.Services.AddSingleton<IContextBuilder, ContextBuilder>();
builder.Services.AddSingleton<IPostProcessor, PostProcessor>();
builder.Services.AddSingleton<IIngestor, MarkdownIngestor>();
builder.Services.AddSingleton<IIngestor, JsonIngestor>();
builder.Services.AddSingleton<IIngestor, PdfIngestor>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/openapi/{documentName}.json");
}

app.MapGet("/swagger", () => Results.Content("""
<!doctype html>
<html>
  <head>
    <meta charset="utf-8" />
    <title>McpServer Swagger UI</title>
    <link rel="stylesheet" href="https://unpkg.com/swagger-ui-dist@5/swagger-ui.css" />
  </head>
  <body>
    <div id="swagger-ui"></div>
    <script src="https://unpkg.com/swagger-ui-dist@5/swagger-ui-bundle.js"></script>
    <script>
      window.onload = () => {
        SwaggerUIBundle({
          url: '/openapi/v1.json',
          dom_id: '#swagger-ui'
        });
      };
    </script>
  </body>
</html>
""", "text/html"));

app.MapControllers();
app.MapGet("/health", async (IConfiguration configuration, IRetriever retriever, ILLMAdapter llm, CancellationToken cancellationToken) =>
{
    try
    {
        await retriever.SearchAsync("health", 1, cancellationToken);
        var response = await llm.GenerateAsync("health", configuration["Ollama:Model"], cancellationToken);
        return Results.Ok(new { status = "ok", response });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { status = "degraded", error = ex.Message });
    }
});

if (args.Length > 0 && args[0].Equals("ingest", StringComparison.OrdinalIgnoreCase))
{
    var sourceIndex = Array.FindIndex(args, arg => arg.Equals("--source", StringComparison.OrdinalIgnoreCase));
    var typeIndex = Array.FindIndex(args, arg => arg.Equals("--type", StringComparison.OrdinalIgnoreCase));
    var source = sourceIndex >= 0 && sourceIndex + 1 < args.Length ? args[sourceIndex + 1] : null;
    var type = typeIndex >= 0 && typeIndex + 1 < args.Length ? args[typeIndex + 1] : null;
    var resolvedSource = ResolveSourcePath(source);

    if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(type))
    {
        Console.Error.WriteLine("Usage: dotnet run -- ingest --source <path> --type <markdown|json|pdf>");
        return;
    }

    var ingestors = app.Services.GetServices<IIngestor>().ToList();
    var ingestor = ingestors.FirstOrDefault(candidate => candidate.CanHandle(type));
    if (ingestor is null)
    {
        Console.Error.WriteLine($"No ingestor is registered for type '{type}'.");
        return;
    }

    var chunks = await ingestor.IngestAsync(resolvedSource);
    Console.WriteLine($"Ingested {chunks.Count} chunks from {source}.");
    foreach (var chunk in chunks.Take(5))
    {
        Console.WriteLine($"- {chunk.Content}");
    }

    return;
}

app.Run();

static string ResolveSourcePath(string? source)
{
    if (string.IsNullOrWhiteSpace(source))
    {
        return string.Empty;
    }

    if (Path.IsPathRooted(source))
    {
        return source;
    }

    var candidates = new[]
    {
        Path.GetFullPath(source, Directory.GetCurrentDirectory()),
        Path.GetFullPath(source, Path.Combine(Directory.GetCurrentDirectory(), "..")),
        Path.GetFullPath(source, AppContext.BaseDirectory),
    };

    return candidates.FirstOrDefault(candidate => File.Exists(candidate) || Directory.Exists(candidate)) ?? candidates[0];
}
