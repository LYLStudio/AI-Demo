using Microsoft.Extensions.DependencyInjection;
using McpServer.Interfaces;
using McpServer.Models;

namespace McpServer.Services;

/// <summary>
/// 提供 WebSearch 服務之 DI 容器擴充方法。
/// 遵循 DIP：註冊抽象 (Interface) 對具體實作。
/// </summary>
public static class WebSearchServiceCollectionExtensions
{
        /// <summary>
        /// 將所有 WebSearch 相關服務加入 IServiceCollection。
        /// </summary>
    public static IServiceCollection AddWebSearchServices(
        this IServiceCollection services,
        IConfiguration configuration)
        {
        // 綁定組態設定（非 hardcode）。
        var webSearchSettings = configuration.GetSection("WebSearch").Get<WebSearchSettings>();
        if (webSearchSettings is null)
        {
            throw new InvalidOperationException("WebSearch configuration section is missing from appsettings.json.");
        }

        // 註冊 Anti-Bot 策略（預設使用 BrowserSim）。
        services.AddSingleton<IAntiBotStrategy>(sp => new BrowserSimAntiBotStrategy(webSearchSettings));

        // 依據組態中搜尋引擎提供者清單動態註冊。
        var providers = webSearchSettings.SearchEngines.ToList();
        if (providers.Count == 0)
        {
            // 預設 DuckDuckGo 提供者（若組態未提供）。
            providers.Add(new SearchEngineProvider
            {
                Name = "duckduckgo",
                SearchUrlTemplate = "https://html.duckduckgo.com/html/?q={query}",
                IsDefault = true
            });
        }

        foreach (var provider in providers)
        {
            IWebSearchEngine engine = provider.Name.ToLowerInvariant() switch
            {
                "duckduckgo" => new DuckDuckGoSearchEngine(
                    new BrowserSimAntiBotStrategy(webSearchSettings),
                    provider.SearchUrlTemplate),
                _ => throw new InvalidOperationException($"不支援的搜尋引擎提供者: {provider.Name}")
            };

            services.AddSingleton<IWebSearchEngine>(engine);
        }

        return services;
        }
}