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
        Id = JsonSafe.Str(o["id"]),
        Email = JsonSafe.Str(o["email"]),
        DisplayName = JsonSafe.Str(o["displayName"]),
        Status = JsonSafe.Str(o["status"]),
        ExpiresAt = JsonSafe.Str(o["expiresAt"]),
        BoundProxy = JsonSafe.Str(o["boundProxy"]),
        InFlight = JsonSafe.Int(o["inFlight"]),
        CooldownSec = JsonSafe.Int(o["cooldownSec"]),
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
                string id = JsonSafe.Str(o["id"], JsonSafe.Str(o["name"]));
                if (id.Length > 0) list.Add(new ModelInfo { Id = id, Name = JsonSafe.Str(o["name"], id) });
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
        Url = JsonSafe.Str(o["url"]),
        Failures = JsonSafe.Int(o["failures"]),
        CooldownUntil = JsonSafe.Str(o["cooldownUntil"]),
        LastCheck = JsonSafe.Str(o["lastCheck"]),
        LatencyMs = JsonSafe.Int(o["latencyMs"]),
        LastError = JsonSafe.Str(o["lastError"]),
        Health = JsonSafe.Str(o["health"]),
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
        Requests = JsonSafe.Int(o["requests"]),
        PromptTokens = JsonSafe.Long(o["promptTokens"]),
        OutputTokens = JsonSafe.Long(o["outputTokens"]),
        SavedTokens = JsonSafe.Long(o["savedTokens"]),
        SessionHits = JsonSafe.Int(o["sessionHits"]),
        HitRate = JsonSafe.Double(o["hitRate"]),
    };
}
