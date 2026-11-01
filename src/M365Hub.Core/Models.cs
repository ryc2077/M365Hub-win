// Typed views over the gateway JSON the WinUI pages consume.
// Shapes verified against the gateway source (internal/console):
// /api/accounts -> {id, email, displayName, status, expiresAt,
// updatedAt, boundProxy, encrypted, inFlight, cooldownSec};
// /api/admin/models -> array of strings or {id, name};
// Pool.List() -> {url, failures, cooldownUntil, lastCheck,
// latencyMs, lastError, health}; /api/usage -> {totals, byDay,
// byModel, byAccount, byKey, byEndpoint, recent}.
using System.Text.Json.Nodes;

namespace M365Hub.Core;

public sealed class AccountInfo
{
    public string Id = "";
    public string Email = "";
    public string DisplayName = "";
    public string Status = "";
    public string ExpiresAt = "";
    public string BoundProxy = "";
    public int InFlight;
    public int CooldownSec;

    public static AccountInfo FromJson(JsonObject o) => new()
    {
        Id = o["id"]?.GetValue<string>() ?? "",
        Email = o["email"]?.GetValue<string>() ?? "",
        DisplayName = o["displayName"]?.GetValue<string>() ?? "",
        Status = o["status"]?.GetValue<string>() ?? "",
        ExpiresAt = o["expiresAt"]?.GetValue<string>() ?? "",
        BoundProxy = o["boundProxy"]?.GetValue<string>() ?? "",
        InFlight = o["inFlight"]?.GetValue<int>() ?? 0,
        CooldownSec = o["cooldownSec"]?.GetValue<int>() ?? 0,
    };

    public bool IsOnline => Status == "online" || Status == "active";
    public bool IsCooling => CooldownSec > 0;
    public string ShortLabel => Email.Contains('@') ? Email.Split('@')[0] : Email;
}

public sealed class ModelInfo
{
    public string Id = "";
    public string Name = "";

    /// <summary>/api/admin/models may return plain strings or {id, name} objects.</summary>
    public static List<ModelInfo> FromJson(JsonNode? node)
    {
        var list = new List<ModelInfo>();
        if (node is not JsonArray arr) return list;
        foreach (var item in arr)
        {
            if (item is JsonValue v && v.TryGetValue<string>(out string? s) && s != null)
                list.Add(new ModelInfo { Id = s, Name = s });
            else if (item is JsonObject o)
            {
                string id = o["id"]?.GetValue<string>() ?? o["name"]?.GetValue<string>() ?? "";
                if (id.Length > 0) list.Add(new ModelInfo { Id = id, Name = o["name"]?.GetValue<string>() ?? id });
            }
        }
        return list;
    }
}

public sealed class ProxyInfo
{
    public string Url = "";
    public int Failures;
    public string CooldownUntil = "";
    public string LastCheck = "";
    public int LatencyMs;
    public string LastError = "";
    public string Health = "";

    public static ProxyInfo FromJson(JsonObject o) => new()
    {
        Url = o["url"]?.GetValue<string>() ?? "",
        Failures = o["failures"]?.GetValue<int>() ?? 0,
        CooldownUntil = o["cooldownUntil"]?.GetValue<string>() ?? "",
        LastCheck = o["lastCheck"]?.GetValue<string>() ?? "",
        LatencyMs = o["latencyMs"]?.GetValue<int>() ?? 0,
        LastError = o["lastError"]?.GetValue<string>() ?? "",
        Health = o["health"]?.GetValue<string>() ?? "",
    };
}

public sealed class UsageTotals
{
    public int Requests;
    public long PromptTokens;
    public long OutputTokens;
    public long SavedTokens;
    public int SessionHits;
    public double HitRate;

    public static UsageTotals FromJson(JsonObject o) => new()
    {
        Requests = o["requests"]?.GetValue<int>() ?? 0,
        PromptTokens = o["promptTokens"]?.GetValue<long>() ?? 0,
        OutputTokens = o["outputTokens"]?.GetValue<long>() ?? 0,
        SavedTokens = o["savedTokens"]?.GetValue<long>() ?? 0,
        SessionHits = o["sessionHits"]?.GetValue<int>() ?? 0,
        HitRate = o["hitRate"]?.GetValue<double>() ?? 0,
    };
}
