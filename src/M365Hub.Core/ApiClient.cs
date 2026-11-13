// Minimal HTTP client for the local m365hub gateway (127.0.0.1:8230).
// The console session is a cookie (m365hub_admin); it is stored in a
// small file under %LOCALAPPDATA%\M365Hub so a cold app start does not
// force another login. Direct port of Android ApiClient.java.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace M365Hub.Core;

public sealed class ApiException : Exception
{
    public ApiException(string message) : base(message) { }
}

public sealed class ApiClient
{
    public const string Base = "http://127.0.0.1:8230";

    // Loopback must never go through the system proxy: HttpClient
    // honours proxy auto-detect by default, and a proxy-stalled first
    // request was misread as a hung gateway in 1.0.4. Remote image
    // downloads keep a separate default client (proxy allowed).
    private readonly HttpClient _http = new(new HttpClientHandler { UseProxy = false });
    private readonly HttpClient _httpRemote = new();
    private readonly string? _cookieFile;

    public ApiClient(string? cookieFile = null)
    {
        _cookieFile = cookieFile;
    }

    public string? Cookie
    {
        get
        {
            try
            {
                if (_cookieFile != null && File.Exists(_cookieFile))
                    return File.ReadAllText(_cookieFile).Trim();
            }
            catch { }
            return null;
        }
        set
        {
            try
            {
                if (_cookieFile == null) return;
                if (value == null) File.Delete(_cookieFile);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_cookieFile)!);
                    File.WriteAllText(_cookieFile, value);
                }
            }
            catch { }
        }
    }

    public void ClearCookie() => Cookie = null;

    public Task<JsonObject> GetAsync(string path) => CallAsync("GET", path, null, 15, 120);
    public Task<JsonObject> DeleteAsync(string path) => CallAsync("DELETE", path, null, 15, 120);
    /// <summary>Readiness probe: 2s timeouts so a hung gateway cannot stall the poll loop.</summary>
    public Task<JsonObject> GetFastAsync(string path) => CallAsync("GET", path, null, 2, 2);
    public Task<JsonObject> PostAsync(string path, JsonNode? body) => CallAsync("POST", path, body, 15, 120);
    /// <summary>Admin login with a 10s total timeout: a hung gateway must surface an error instead of looking dead.</summary>
    public Task<JsonObject> PostLoginAsync(string path, JsonNode? body) => CallAsync("POST", path, body, 5, 5);

    private async Task<JsonObject> CallAsync(string method, string path, JsonNode? body,
        int connectSeconds, int readSeconds)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(connectSeconds + readSeconds));
        using var req = new HttpRequestMessage(new HttpMethod(method), Base + path);
        string? ck = Cookie;
        if (ck != null) req.Headers.Add("Cookie", "m365hub_admin=" + ck);
        if (body != null)
        {
            req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }
        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, cts.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is TaskCanceledException or HttpRequestException)
        {
            throw new ApiException(e.Message);
        }
        if (resp.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (string sc in setCookies)
            {
                foreach (string part in sc.Split(';'))
                {
                    string p = part.Trim();
                    if (p.StartsWith("m365hub_admin=", StringComparison.Ordinal))
                        Cookie = p.Substring("m365hub_admin=".Length);
                }
            }
        }
        string text = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        JsonObject d;
        try
        {
            d = string.IsNullOrEmpty(text) ? new JsonObject() : JsonNode.Parse(text)!.AsObject();
        }
        catch
        {
            d = new JsonObject();
        }
        if ((int)resp.StatusCode >= 400)
        {
            string m = d["message"]?.GetValue<string>() ?? d["error"]?.GetValue<string>()
                ?? "HTTP " + (int)resp.StatusCode;
            throw new ApiException(m);
        }
        return d;
    }

    /// <summary>
    /// Fetch a gateway-served image (e.g. /v1/images/...) with the session
    /// cookie, a data: URL (user upload), or a local file path (persisted
    /// thumbnail). Returns raw bytes, or null on failure.
    /// </summary>
    public async Task<byte[]?> FetchImageBytesAsync(string url)
    {
        try
        {
            if (url.StartsWith("data:image/", StringComparison.Ordinal))
            {
                int comma = url.IndexOf(',');
                if (comma < 0) return null;
                return Convert.FromBase64String(url.Substring(comma + 1));
            }
            if (url.StartsWith("file://", StringComparison.Ordinal) || url.StartsWith("/")
                || File.Exists(url))
            {
                // Persisted thumbnail file (see ChatPage); a Windows
                // absolute path (C:\...) is caught by File.Exists.
                string path = url.StartsWith("file://", StringComparison.Ordinal) ? url.Substring(7) : url;
                return await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            }
            bool remote = url.StartsWith("http", StringComparison.Ordinal);
            string full = remote ? url : Base + url;
            using var req = new HttpRequestMessage(HttpMethod.Get, full);
            string? ck = Cookie;
            if (ck != null) req.Headers.Add("Cookie", "m365hub_admin=" + ck);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            // Remote image hosts keep the default (proxy-aware) client;
            // gateway-served paths stay on the no-proxy loopback client.
            using var resp = await (remote ? _httpRemote : _http).SendAsync(req, cts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public static JsonArray Arr(JsonObject d, string k)
        => d[k] as JsonArray ?? new JsonArray();
}

