namespace AIConsole.Services;

using System;
using System.IO;
using System.Text.Json;
using AIConsole.Models;

public class ConfigService : IConfigService
{
    private readonly string _configFilePath;

    public ConfigService(string configFilePath)
    {
        _configFilePath = configFilePath;
    }

    public ChatConfig GetConfig()
    {
        if (!File.Exists(_configFilePath))
        {
            var defaultConfig = new ChatConfig();
            SaveConfig(defaultConfig);
            return defaultConfig;
        }

        try
        {
            string json = File.ReadAllText(_configFilePath);
            return JsonSerializer.Deserialize<ChatConfig>(json) ?? new ChatConfig();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[warn] Error loading config: {ex.Message}. Using default settings.");
            return new ChatConfig();
        }
    }

    public void SaveConfig(ChatConfig config)
    {
        try
        {
            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[warn] Error saving config: {ex.Message}");
        }
    }
}