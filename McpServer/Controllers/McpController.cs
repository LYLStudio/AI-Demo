using McpServer.Interfaces;
using McpServer.Models;
using Microsoft.AspNetCore.Mvc;

namespace McpServer.Controllers;

/// <summary>
/// 提供 MCP 風格端點，支援初始化、工具列舉、工具呼叫與健康檢查。
/// </summary>
[ApiController]
[Route("mcp")]
public class McpController : ControllerBase
{
    private readonly IToolRegistry _toolRegistry;
    private readonly ILLMAdapter _llmAdapter;
    private readonly IRetriever _retriever;
    private readonly IContextBuilder _contextBuilder;
    private readonly IPostProcessor _postProcessor;
    private readonly IConfiguration _configuration;

    public McpController(
        IToolRegistry toolRegistry,
        ILLMAdapter llmAdapter,
        IRetriever retriever,
        IContextBuilder contextBuilder,
        IPostProcessor postProcessor,
        IConfiguration configuration)
    {
        _toolRegistry = toolRegistry;
        _llmAdapter = llmAdapter;
        _retriever = retriever;
        _contextBuilder = contextBuilder;
        _postProcessor = postProcessor;
        _configuration = configuration;
    }

    /// <summary>
    /// 初始化 MCP server metadata 與 capabilities。
    /// </summary>
    [HttpPost("initialize")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Initialize()
    {
        return Ok(new
        {
            server = "McpServer",
            version = "1.0.0",
            metadata = new { protocol = "mcp", model = _configuration["Ollama:Model"] ?? "gemma4:31b-mlx" },
            capabilities = new { tools = true, rag = true, health = true }
        });
    }

    /// <summary>
    /// 列出所有已註冊的工具定義。
    /// </summary>
    [HttpGet("tools")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult ListTools()
    {
        return Ok(_toolRegistry.ListTools());
    }

    /// <summary>
    /// 依據指定工具 ID 執行工具呼叫並回傳執行結果。
    /// </summary>
    [HttpPost("call")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Call([FromBody] CallToolRequest request, CancellationToken cancellationToken)
    {
        if (!ValidateApiKey())
        {
            return Unauthorized(new { error = "Missing or invalid API key." });
        }

        if (string.IsNullOrWhiteSpace(request.ToolId))
        {
            return BadRequest(new { error = "ToolId is required." });
        }

        var result = await _toolRegistry.ExecuteAsync(request.ToolId, request.Input, request.User, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// 檢查 Ollama 與向量檢索組件的健康狀態。
    /// </summary>
    [HttpGet("health")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Health(CancellationToken cancellationToken)
    {
        var ollamaOk = await CheckOllamaAsync(cancellationToken);
        var vectorOk = await CheckVectorStoreAsync(cancellationToken);
        var status = new HealthStatus
        {
            Status = ollamaOk && vectorOk ? "ok" : "degraded",
            Components = new Dictionary<string, object?>
            {
                ["ollama"] = ollamaOk,
                ["vector_db"] = vectorOk,
                ["api_key"] = !string.IsNullOrWhiteSpace(_configuration["Security:ApiKey"])
            }
        };

        return Ok(status);
    }

    /// <summary>
    /// 使用 RAG prompt 將使用者查詢送至 Ollama，並若模型回傳 tool call 則進行執行。
    /// </summary>
    [HttpPost("chat")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Chat([FromBody] string query, CancellationToken cancellationToken)
    {
        if (!ValidateApiKey())
        {
            return Unauthorized(new { error = "Missing or invalid API key." });
        }

        var chunks = await _retriever.SearchAsync(query, 5, cancellationToken);
        var prompt = _contextBuilder.BuildPrompt(query, chunks);
        var llmResponse = await _llmAdapter.GenerateAsync(prompt, _configuration["Ollama:Model"], cancellationToken);
        var result = await _postProcessor.ProcessAsync(llmResponse, "user", cancellationToken);
        return Ok(new { prompt, llmResponse, result });
    }

    private bool ValidateApiKey()
    {
        var expectedApiKey = _configuration["Security:ApiKey"];
        if (string.IsNullOrWhiteSpace(expectedApiKey))
        {
            return true;
        }

        if (!Request.Headers.TryGetValue("Authorization", out var values))
        {
            return false;
        }

        var header = values.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            && header["Bearer ".Length..].Equals(expectedApiKey, StringComparison.Ordinal);
    }

    private async Task<bool> CheckOllamaAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var baseUrl = _configuration["Ollama:BaseUrl"];
            using var response = await client.GetAsync($"{baseUrl}/api/tags", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> CheckVectorStoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _retriever.SearchAsync("health", 1, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
