// Owns the bundled m365hub gateway process on Windows: picks the
// right binary for the CPU (x64 or ARM64), starts it next to the app,
// polls /healthz until it answers, and tails its log on failure.
// The gateway exe ships beside the app: m365hub.exe (x64) and
// m365hub-arm64.exe (ARM64); the choice is made at runtime from
// RuntimeInformation.OSArchitecture, so one install package covers
// both architectures.
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace M365Hub.Core;

public sealed class GatewayManager
{
    public string AppDataDir { get; }
    public string LogPath => Path.Combine(AppDataDir, "serve-windows.log");
    public string DataDir => Path.Combine(AppDataDir, "data");

    private Process? _proc;
    private bool _owned;

    /// <summary>True when the gateway we spawned ourselves has already exited.</summary>
    public bool OwnedGatewayExited => _owned && _proc is { HasExited: true };

    public GatewayManager(string? appDataDir = null)
    {
        AppDataDir = appDataDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "M365Hub");
    }

    public bool IsRunning => _proc is { HasExited: false };

    /// <summary>Path of the gateway exe for this CPU, or null if not shipped.</summary>
    public string? GatewayExePath()
    {
        string baseDir = AppContext.BaseDirectory;
        bool arm64 = RuntimeInformation.OSArchitecture == Architecture.Arm64;
        string[] candidates = arm64
            ? new[] { "m365hub-arm64.exe", "m365hub.exe" }
            : new[] { "m365hub.exe", "m365hub-arm64.exe" };
        foreach (string name in candidates)
        {
            string p = Path.Combine(baseDir, name);
            if (File.Exists(p)) return p;
            string g = Path.Combine(baseDir, "gateway", name);
            if (File.Exists(g)) return g;
        }
        return null;
    }

    // Probe outcome for 127.0.0.1:8230: nothing listening, a live gateway
    // answering HTTP, or a hung one that accepts TCP but never answers.
    private enum Probe { None, Live, Hung }

    private static Probe ProbeGateway()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        try
        {
            // Task.Run so the async call never captures the UI thread's
            // context: Start() runs on the UI thread and blocks here.
            using var resp = Task.Run(() => http.GetAsync("http://127.0.0.1:8230/healthz")).GetAwaiter().GetResult();
            return Probe.Live;
        }
        catch (TaskCanceledException)
        {
            // Timeout with no HTTP answer: something took the TCP
            // connection but never replied -> hung gateway.
            return Probe.Hung;
        }
        catch
        {
            // Connection refused etc: nothing listening.
            return Probe.None;
        }
    }

    /// <summary>Kill only processes literally named m365hub (the gateway exe). Never touch anything else on the port.</summary>
    private static void ClearHungGateway()
    {
        foreach (var p in Process.GetProcessesByName("m365hub"))
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            try { p.WaitForExit(2000); } catch { }
        }
    }

    /// <summary>Start the gateway if it is not already listening. Returns false + error when the exe is missing.</summary>
    public bool Start(out string? error)
    {
        error = null;
        if (IsRunning) return true;
        // Probe first with a 2s timeout (Mac 1.0.26, same fix): a hung
        // old gateway used to make the app spawn a second gateway that
        // could never bind, then poll for a minute.
        switch (ProbeGateway())
        {
            case Probe.Live:
                // A gateway is already answering: adopt it, never kill it.
                _owned = false;
                return true;
            case Probe.Hung:
                ClearHungGateway();
                System.Threading.Thread.Sleep(1000);
                if (ProbeGateway() == Probe.Hung)
                {
                    error = "端口 8230 被一个无响应的旧网关占用，且清理失败（只会清理 m365hub 进程）。请手动结束占用 8230 的程序后重试。";
                    return false;
                }
                break;
            default:
                break;
        }
        string? exe = GatewayExePath();
        if (exe == null)
        {
            error = "找不到网关程序 m365hub.exe（x64）/ m365hub-arm64.exe（ARM64）";
            return false;
        }
        try
        {
            Directory.CreateDirectory(AppDataDir);
            Directory.CreateDirectory(DataDir);
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "serve",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.Environment["M365_DATA_DIR"] = DataDir;
            psi.Environment["M365_LISTEN"] = "127.0.0.1:8230";
            _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _proc.OutputDataReceived += (_, e) => { if (e.Data != null) AppendLog(e.Data); };
            _proc.ErrorDataReceived += (_, e) => { if (e.Data != null) AppendLog(e.Data); };
            _proc.Start();
            _proc.BeginOutputReadLine();
            _proc.BeginErrorReadLine();
            _owned = true;
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    /// <summary>Poll /healthz until the gateway answers (60 tries, 1s apart). Reports "第 N 次" progress.</summary>
    public async Task<bool> WaitReadyAsync(ApiClient api, IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        for (int n = 1; n <= 60; n++)
        {
            if (ct.IsCancellationRequested) return false;
            if (_proc is { HasExited: true })
            {
                // Our own gateway already died (e.g. the port was taken):
                // polling for the rest of the minute is pointless.
                return false;
            }
            try
            {
                await api.GetFastAsync("/healthz").ConfigureAwait(false);
                return true;
            }
            catch { }
            progress?.Report("正在启动本地网关…（第 " + n + " 次）");
            try { await Task.Delay(1000, ct).ConfigureAwait(false); } catch { return false; }
        }
        return false;
    }

    public void Stop()
    {
        // Only stop a gateway we spawned ourselves; an adopted one is
        // left alone (it may belong to another app install).
        try { if (_owned) _proc?.Kill(entireProcessTree: true); } catch { }
        _proc = null;
        _owned = false;
    }

    private void AppendLog(string line)
    {
        try { File.AppendAllText(LogPath, line + Environment.NewLine); } catch { }
    }

    /// <summary>Last ~4000 chars of the gateway log, for the failure dialog.</summary>
    public string LogTail()
    {
        try
        {
            if (!File.Exists(LogPath)) return "(暂无日志)";
            string t = File.ReadAllText(LogPath);
            return t.Length > 4000 ? t.Substring(t.Length - 4000) : t;
        }
        catch (Exception e)
        {
            return "(读日志失败: " + e.Message + ")";
        }
    }
}
