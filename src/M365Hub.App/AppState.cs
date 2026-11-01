// Shared app state: gateway, API client, chat history, and the few
// persisted preferences (theme, pinned chat account, chat model).
// Settings live in a small JSON file so they survive restarts and
// are easy to inspect, like the Android SharedPreferences keys.
using System.Text.Json.Nodes;
using M365Hub.Core;

namespace M365Hub.App;

public sealed class AppState
{
    public string AppDataDir { get; }
    public GatewayManager Gateway { get; }
    public ApiClient Api { get; }
    public ChatStore Chat { get; }

    private readonly string _settingsFile;
    private JsonObject _settings = new();

    public AppState()
    {
        AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "M365Hub");
        Directory.CreateDirectory(AppDataDir);
        Gateway = new GatewayManager(AppDataDir);
        Api = new ApiClient(Path.Combine(AppDataDir, "admin-cookie.txt"));
        Chat = new ChatStore(Path.Combine(AppDataDir, "chat-history.json"));
        _settingsFile = Path.Combine(AppDataDir, "settings.json");
        try
        {
            if (File.Exists(_settingsFile))
                _settings = JsonNode.Parse(File.ReadAllText(_settingsFile))!.AsObject();
        }
        catch { _settings = new JsonObject(); }
    }

    private void SaveSettings()
    {
        try { File.WriteAllText(_settingsFile, _settings.ToJsonString()); } catch { }
    }

    public bool ThemeDark
    {
        get => _settings["theme_dark"]?.GetValue<bool>() ?? false;
        set { _settings["theme_dark"] = value; SaveSettings(); }
    }

    public string ChatAccountId
    {
        get => _settings["chat_account_id"]?.GetValue<string>() ?? "";
        set { _settings["chat_account_id"] = value; SaveSettings(); }
    }

    public string ChatModel
    {
        get => _settings["chat_model"]?.GetValue<string>() ?? "";
        set { _settings["chat_model"] = value; SaveSettings(); }
    }

    public string ChatImagesDir => Path.Combine(AppDataDir, "chat-images");
}
