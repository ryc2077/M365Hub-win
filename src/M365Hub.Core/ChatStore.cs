// Chat conversations persisted under %LOCALAPPDATA%\M365Hub (not in
// the gateway's data dir, so they survive gateway restarts and are
// deleted with the app data, same as the old WebView app's
// localStorage history and the Android native ChatStore).
// Only text is persisted: uploaded image data URLs are stripped on
// save (they would blow up the file); generated-image server URLs
// are kept because they are small. Direct port of Android ChatStore.java.
using System.Text.Json;
using System.Text.Json.Nodes;

namespace M365Hub.Core;

public sealed class ChatStore
{
    public sealed class Msg
    {
        public string Role = "";
        public string Content = "";
        public List<string> Images = new();
        public List<string> AttImages = new();
        public Msg() { }
        public Msg(string role, string content) { Role = role; Content = content; }
    }

    public sealed class Conv
    {
        public string Id = "";
        public string Title = "新对话";
        public string CreatedAt = "";
        public List<Msg> Messages = new();
        public Conv() { }
        public Conv(string id, string title)
        {
            Id = id; Title = title;
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        }
    }

    private readonly string _file;
    public List<Conv> List = new();
    public string? CurrentId;

    public ChatStore(string file)
    {
        _file = file;
        Load();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_file)) return;
            var d = JsonNode.Parse(File.ReadAllText(_file))!.AsObject();
            CurrentId = d["currentId"]?.GetValue<string>();
            var arr = d["list"] as JsonArray;
            if (arr == null) return;
            foreach (var item in arr)
            {
                var c = item!.AsObject();
                var conv = new Conv(c["id"]?.GetValue<string>() ?? "",
                    c["title"]?.GetValue<string>() ?? "新对话");
                conv.CreatedAt = c["createdAt"]?.GetValue<string>() ?? conv.CreatedAt;
                var ms = c["messages"] as JsonArray;
                if (ms != null)
                {
                    foreach (var mj in ms)
                    {
                        var m = mj!.AsObject();
                        var msg = new Msg(m["role"]?.GetValue<string>() ?? "",
                            m["content"]?.GetValue<string>() ?? "");
                        var im = m["images"] as JsonArray;
                        if (im != null) foreach (var x in im) msg.Images.Add(x!.GetValue<string>());
                        var ai = m["attImages"] as JsonArray;
                        if (ai != null) foreach (var x in ai) msg.AttImages.Add(x!.GetValue<string>());
                        conv.Messages.Add(msg);
                    }
                }
                List.Add(conv);
            }
        }
        catch { }
    }

    public void Save()
    {
        try
        {
            if (List.Count > 30) List = List.Take(30).ToList();
            var d = new JsonObject();
            d["currentId"] = CurrentId == null ? null : JsonValue.Create(CurrentId);
            var arr = new JsonArray();
            foreach (var c in List)
            {
                var o = new JsonObject();
                o["id"] = c.Id;
                o["title"] = c.Title;
                o["createdAt"] = c.CreatedAt;
                var ms = new JsonArray();
                var msgs = c.Messages.Count > 200
                    ? c.Messages.Skip(c.Messages.Count - 200) : c.Messages;
                foreach (var m in msgs)
                {
                    var mo = new JsonObject();
                    mo["role"] = m.Role;
                    mo["content"] = m.Content;
                    var im = new JsonArray();
                    foreach (string u in m.Images) im.Add(u);
                    mo["images"] = im;
                    var ai = new JsonArray();
                    foreach (string u in m.AttImages)
                        if (!u.StartsWith("data:", StringComparison.Ordinal)) ai.Add(u);
                    mo["attImages"] = ai;
                    ms.Add(mo);
                }
                o["messages"] = ms;
                arr.Add(o);
            }
            d["list"] = arr;
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, d.ToJsonString());
        }
        catch { }
    }

    public Conv Current()
    {
        foreach (var c in List)
        {
            if (c.Id == CurrentId) return c;
        }
        return NewConv();
    }

    public Conv NewConv()
    {
        var c = new Conv("c" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), "新对话");
        List.Insert(0, c);
        CurrentId = c.Id;
        Save();
        return c;
    }

    public void Delete(string id)
    {
        for (int i = 0; i < List.Count; i++)
        {
            if (List[i].Id == id) { List.RemoveAt(i); break; }
        }
        if (id == CurrentId) CurrentId = null;
        Save();
    }
}
