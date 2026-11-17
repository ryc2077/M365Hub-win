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
                _settings = JsonSafe.Obj(JsonNode.Parse(File.ReadAllText(_settingsFile)));
        }
        catch { _settings = new JsonObject(); }
    }

    private void SaveSettings()
    {
        try { File.WriteAllText(_settingsFile, _settings.ToJsonString()); } catch { }
    }

    public bool ThemeDark
    {
        get => JsonSafe.Bool(_settings["theme_dark"]);
        set { _settings["theme_dark"] = value; SaveSettings(); }
    }

    public string ChatAccountId
    {
        get => JsonSafe.Str(_settings["chat_account_id"]);
        set { _settings["chat_account_id"] = value; SaveSettings(); }
    }

    public string ChatModel
    {
        get => JsonSafe.Str(_settings["chat_model"]);
        set { _settings["chat_model"] = value; SaveSettings(); }
    }

    public string ChatImagesDir => Path.Combine(AppDataDir, "chat-images");
}
