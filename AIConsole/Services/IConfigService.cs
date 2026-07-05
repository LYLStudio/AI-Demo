namespace AIConsole.Services;

using AIConsole.Models;

public interface IConfigService
{
    ChatConfig GetConfig();
    void SaveConfig(ChatConfig config);
}