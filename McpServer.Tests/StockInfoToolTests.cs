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
        var tool = new StockInfoTool(client, Options.Create(new OllamaSettings { BaseUrl = "http://localhost:11434", Model = "gemma4:31b-mlx" }), Options.Create(new StockApiSettings { BaseUrl = "https://example.test" }));

        var result = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { symbol = "2330" }));
        var json = JsonSerializer.Serialize(result);

        Assert.Contains("2330", json);
        Assert.Contains("demo", json);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFirstMarketHasNoCompanyName_FallsBackToOtc()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var query = request.RequestUri?.Query ?? string.Empty;
            if (query.Contains("tse_5347.tw"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"msgArray\":[{\"z\":\"100.50\",\"n\":\"\"}]}", Encoding.UTF8, "application/json")
                });
            }

            if (query.Contains("otc_5347.tw"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"msgArray\":[{\"z\":\"100.60\",\"n\":\"世界先進\",\"nf\":\"世界先進股份有限公司\"}]}", Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"msgArray\":[]}", Encoding.UTF8, "application/json")
            });
        });

        var client = new HttpClient(handler);
        var tool = new StockInfoTool(client, Options.Create(new OllamaSettings { BaseUrl = "http://localhost:11434", Model = "gemma4:31b-mlx" }), Options.Create(new StockApiSettings { BaseUrl = "https://example.test" }));

        var result = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { symbol = "5347" }));
        var json = JsonSerializer.Serialize(result);

        Assert.Contains("世界先進", json);
        Assert.Contains("otc_5347.tw", json);
        Assert.Contains("100.60", json);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request, cancellationToken);
    }
}
