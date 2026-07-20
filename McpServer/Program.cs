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
builder.Services.AddHttpClient(); // Use IHttpClientFactory for proper HttpClient management

builder.Services.Configure<OllamaSettings>(builder.Configuration.GetSection("Ollama"));
builder.Services.Configure<StockApiSettings>(builder.Configuration.GetSection("StockApi"));
builder.Services.Configure<WebSearchSettings>(builder.Configuration.GetSection("WebSearch"));

// Validate critical configuration on startup
var ollamaConfig = builder.Configuration.GetSection("Ollama").Get<OllamaSettings>();
if (string.IsNullOrWhiteSpace(ollamaConfig?.BaseUrl))
{
    Console.Error.WriteLine("[ERROR] Ollama:BaseUrl is required in appsettings.json");
    Environment.Exit(1);
}

var stockApiConfig = builder.Configuration.GetSection("StockApi").Get<StockApiSettings>();
if (string.IsNullOrWhiteSpace(stockApiConfig?.BaseUrl))
{
    Console.Error.WriteLine("[ERROR] StockApi:BaseUrl is required in appsettings.json");
    Environment.Exit(1);
}

builder.Services.AddSingleton<AuditLogger>();
builder.Services.AddSingleton<IToolRegistry, ToolRegistry>();
builder.Services.AddSingleton<ITool, CalculatorTool>();
builder.Services.AddSingleton<ITool, SearchDocsTool>();
builder.Services.AddSingleton<ITool, ReadFileTool>();
builder.Services.AddSingleton<ITool, HttpRequestTool>();
builder.Services.AddSingleton<ITool, RunQueryTool>();
builder.Services.AddSingleton<ITool, StockInfoTool>();
builder.Services.AddSingleton<ITool, WebSearchTool>();
builder.Services.AddSingleton<IChunker, SimpleChunker>();
builder.Services.AddSingleton<IEmbedder, RandomEmbedder>();
builder.Services.AddSingleton<IRetriever, InMemoryRetriever>();
builder.Services.AddSingleton<ILLMAdapter, OllamaClient>();
builder.Services.AddSingleton<IContextBuilder, ContextBuilder>();
builder.Services.AddSingleton<IPostProcessor, PostProcessor>();
builder.Services.AddSingleton<IIngestor, MarkdownIngestor>();
builder.Services.AddSingleton<IIngestor, JsonIngestor>();
builder.Services.AddSingleton<IIngestor, PdfIngestor>();

// WebSearch services
builder.Services.AddWebSearchServices(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/openapi/{documentName}.json");
}

// Request size limit to prevent DoS via huge payloads
app.Use(async (context, next) =>
{
    context.Request.EnableBuffering();
    if (context.Request.ContentLength > 10 * 1024 * 1024) // 10MB limit
     {
        context.Response.StatusCode = 413;
        await context.Response.WriteAsync("Request too large");
        return;
     }
    context.Request.Body.Position = 0; // Reset for downstream reads
    await next();
});

// Map all [Controller] routes (including /mcp)
app.MapControllers();

// Legacy health check endpoint (for convenience) - improved with granular checks
app.MapGet("/health", async (IConfiguration configuration, IRetriever retriever, ILLMAdapter llm, CancellationToken cancellationToken) =>
{
    var components = new Dictionary<string, bool>();
    
     // Check retriever health
    try
     {
         await retriever.SearchAsync("health", 1, cancellationToken);
        components["retriever"] = true;
     }
    catch
     {
        components["retriever"] = false;
     }

     // Check Ollama LLM health
    try
     {
         await llm.GenerateAsync("health", configuration["Ollama:Model"], cancellationToken);
        components["ollama"] = true;
     }
    catch
     {
        components["ollama"] = false;
     }

    var allHealthy = components.Values.All(v => v);
    
    return Results.Ok(new 
     { 
        status = allHealthy ? "healthy" : "degraded", 
        timestamp = DateTimeOffset.UtcNow.ToString("O"),
        components 
     });
});

// CLI ingest command (legacy convenience)
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