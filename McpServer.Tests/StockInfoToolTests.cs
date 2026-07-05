using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using McpServer.Models;
using McpServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace McpServer.Tests;

public class StockInfoToolTests
{
    [Fact]
    public async Task ExecuteAsync_WithSingleSymbol_TriesTseThenOtc()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var query = request.RequestUri?.Query ?? string.Empty;
            if (query.Contains("tse_2330.tw"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"msgArray\":[{\"n\":\"2330\",\"a\":\"demo\"}]}", Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"msgArray\":[]}", Encoding.UTF8, "application/json")
            });
        });

        var client = new HttpClient(handler);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["StockApi:BaseUrl"] = "https://example.test" })
            .Build();
        var tool = new StockInfoTool(client, Options.Create(new OllamaSettings { BaseUrl = "http://localhost:11434", Model = "gemma4:31b-mlx" }), Options.Create(new StockApiSettings { BaseUrl = "https://example.test" }));

        var result = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { symbol = "2330" }));
        var json = JsonSerializer.Serialize(result);

        Assert.Contains("2330", json);
        Assert.Contains("demo", json);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request, cancellationToken);
    }
}
