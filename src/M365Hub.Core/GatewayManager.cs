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

    /// <summary>Start the gateway if it is not already listening. Returns false + error when the exe is missing.</summary>
    public bool Start(out string? error)
    {
        error = null;
        if (IsRunning) return true;
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
        try { _proc?.Kill(entireProcessTree: true); } catch { }
        _proc = null;
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
