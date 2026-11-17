// Safe readers over System.Text.Json.Nodes. Contract: reading a node
// of the wrong shape (object/array where a scalar was expected, a
// number where a string was expected, a missing key) yields the
// fallback instead of throwing InvalidOperationException
// ("The node must be of type 'JsonValue'"). The gateway returns error
// bodies as {"error": {"type", "message", "code"}} — an object, not a
// string — and 1.0.5/1.0.6 crashed on exactly that shape in
// ApiClient's error branch, so every chat message on a gateway with no
// healthy account showed the JsonValue error instead of the real
// reason. Every GetValue<T>/AsObject/AsArray call in the app goes
// through here; no page or model class reads a JsonNode bare.
using System.Text.Json.Nodes;

namespace M365Hub.Core;

public static class JsonSafe
{
    public static string Str(JsonNode? node, string fallback = "")
    {
        if (node is JsonValue v && v.TryGetValue<string>(out string? s) && s != null) return s;
        return fallback;
    }

    /// <summary>Like Str, but preserves null (for nullable fields such as ChatStore.CurrentId).</summary>
    public static string? StrOrNull(JsonNode? node)
    {
        if (node is JsonValue v && v.TryGetValue<string>(out string? s)) return s;
        return null;
    }

    public static int Int(JsonNode? node, int fallback = 0)
    {
        if (node is JsonValue v)
        {
            if (v.TryGetValue<int>(out int i)) return i;
            if (v.TryGetValue<long>(out long l))
                return l >= int.MinValue && l <= int.MaxValue ? (int)l : fallback;
            if (v.TryGetValue<double>(out double d))
                return d >= int.MinValue && d <= int.MaxValue ? (int)d : fallback;
        }
        return fallback;
    }

    public static long Long(JsonNode? node, long fallback = 0)
    {
        if (node is JsonValue v)
        {
            if (v.TryGetValue<long>(out long l)) return l;
            if (v.TryGetValue<int>(out int i)) return i;
            if (v.TryGetValue<double>(out double d))
                return d > -9.223372036854776e18 && d < 9.223372036854776e18 ? (long)d : fallback;
        }
        return fallback;
    }

    public static double Double(JsonNode? node, double fallback = 0)
    {
        if (node is JsonValue v)
        {
            if (v.TryGetValue<double>(out double d)) return d;
            if (v.TryGetValue<long>(out long l)) return l;
            if (v.TryGetValue<int>(out int i)) return i;
        }
        return fallback;
    }

    public static bool Bool(JsonNode? node, bool fallback = false)
    {
        if (node is JsonValue v && v.TryGetValue<bool>(out bool b)) return b;
        return fallback;
    }

    public static JsonObject Obj(JsonNode? node) => node as JsonObject ?? new JsonObject();

    public static JsonArray Arr(JsonNode? node) => node as JsonArray ?? new JsonArray();

    /// <summary>
    /// Human-readable text for an error field that may be a plain string
    /// or an object {"type","message","code"} (the gateway's writeErr
    /// shape). Returns "" when nothing readable is there. Never throws.
    /// </summary>
    public static string ErrText(JsonObject d)
    {
        string m = Str(d["message"]);
        if (m.Length > 0) return m;
        if (d["error"] is JsonObject eo)
        {
            m = Str(eo["message"]);
            if (m.Length > 0) return MapUpstream(m);
            return Str(eo["type"]);
        }
        return Str(d["error"]);
    }

    /// <summary>ErrText, falling back to "HTTP &lt;code&gt;" when the body says nothing.</summary>
    public static string ErrorMessage(JsonObject d, int statusCode)
    {
        string m = ErrText(d);
        return m.Length > 0 ? m : "HTTP " + statusCode;
    }

    // The one upstream failure a fresh install hits: the gateway has no
    // healthy account, so every /api/chat is a 502. Give the user the
    // actionable fix in Chinese instead of the raw English.
    private static string MapUpstream(string m)
    {
        if (m.Contains("no healthy account available", StringComparison.Ordinal))
            return "没有可用的 Microsoft 365 账号，请先在「账号」页添加一个账号，再回来聊天。";
        return m;
    }
}
