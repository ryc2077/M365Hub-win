// Proxies: add (one URL per line), health columns (status,
// latency, failures, cooldown, last error), delete, and a local
// proxy probe (Clash / V2Ray / Surge / Quantumult X on their usual
// ports, verified with a real CONNECT to login.microsoftonline.com).
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using M365Hub.Core;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace M365Hub.App.Pages;

public sealed class ProxiesPage : PageBase
{
    private readonly TextBox _addBox = new()
    {
        PlaceholderText = "每行一个代理 URL，如 http://127.0.0.1:7890 或 socks5://127.0.0.1:1080",
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 64,
    };
    private readonly StackPanel _list = new() { Spacing = 12 };

    public ProxiesPage(AppState state, MainWindow win) : base(state, win)
    {
        Title("代理池");
        Body.Children.Add(_addBox);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new GlassButton("添加", async (_, _) => await AddAsync()));
        row.Children.Add(new GlassButton("检测本机代理并启用", async (_, _) => await ProbeLocalAsync()));
        Body.Children.Add(row);
        Body.Children.Add(_list);
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _list.Children.Clear();
        ClearErr();
        try
        {
            var d = await State.Api.GetAsync("/api/proxies");
            foreach (var item in ApiClient.Arr(d, "proxies"))
            {
                var p = ProxyInfo.FromJson(item!.AsObject());
                var stack = new StackPanel { Spacing = 2 };
                stack.Children.Add(new TextBlock
                {
                    Text = p.Url + " · " + (p.Health.Length > 0 ? p.Health : "—"),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                });
                stack.Children.Add(Row("延迟 " + p.LatencyMs + " ms · 失败 " + p.Failures + " 次",
                    p.CooldownUntil.Length > 0 ? " · 冷却至 " + TimeShort(p.CooldownUntil) : ""));
                if (p.LastError.Length > 0)
                    stack.Children.Add(new TextBlock { Text = "最近错误：" + p.LastError, TextWrapping = TextWrapping.Wrap });
                var del = new GlassButton("删除", async (_, _) =>
                {
                    try
                    {
                        await State.Api.PostAsync("/api/proxies/delete",
                            new JsonObject { ["url"] = p.Url });
                        await LoadAsync();
                    }
                    catch (Exception e) { ShowErr(e.Message); }
                });
                stack.Children.Add(del);
                _list.Children.Add(Card(stack));
            }
            if (_list.Children.Count == 0)
                _list.Children.Add(new TextBlock { Text = "池子是空的。网关直连时没有代理。" });
        }
        catch (Exception e)
        {
            ShowErr(e.Message);
        }
    }

    private async Task AddAsync()
    {
        foreach (string line in _addBox.Text.Split('\n'))
        {
            string url = line.Trim();
            if (url.Length == 0) continue;
            try
            {
                await State.Api.PostAsync("/api/proxies", new JsonObject { ["url"] = url });
            }
            catch (Exception e)
            {
                ShowErr(url + "：" + e.Message);
                return;
            }
        }
        _addBox.Text = "";
        await LoadAsync();
    }

    private async Task ProbeLocalAsync()
    {
        ClearErr();
        ShowErr("正在检测本机代理…");
        string? found = await Task.Run(ProbeLocal);
        if (found == null)
        {
            ShowErr("未发现本机代理（试过 Clash 7890/7891、V2Ray 10809/10808、Surge 6152、QX 6153、1080）。");
            return;
        }
        try
        {
            await State.Api.PostAsync("/api/proxies", new JsonObject { ["url"] = found });
            ErrText.Text = "已启用本机代理 " + found + "。";
            ErrText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 0));
            ErrText.Visibility = Visibility.Visible;
            await LoadAsync();
        }
        catch (Exception e) { ShowErr(e.Message); }
    }

    /// <summary>TCP connect + real CONNECT to login.microsoftonline.com:443 (HTTP) or SOCKS5 handshake.</summary>
    private static string? ProbeLocal()
    {
        var httpPorts = new[] { 7890, 10809, 6152, 6153 };
        var socksPorts = new[] { 7891, 10808, 1080 };
        foreach (int port in httpPorts)
        {
            try
            {
                using var tcp = new TcpClient();
                if (!tcp.ConnectAsync("127.0.0.1", port).Wait(1500)) continue;
                using var s = tcp.GetStream();
                s.WriteTimeout = 1500;
                s.ReadTimeout = 1500;
                byte[] req = Encoding.ASCII.GetBytes(
                    "CONNECT login.microsoftonline.com:443 HTTP/1.1\r\nHost: login.microsoftonline.com:443\r\n\r\n");
                s.Write(req, 0, req.Length);
                var buf = new byte[64];
                int n = s.Read(buf, 0, buf.Length);
                string head = Encoding.ASCII.GetString(buf, 0, n);
                if (head.Contains("200")) return "http://127.0.0.1:" + port;
            }
            catch { }
        }
        foreach (int port in socksPorts)
        {
            try
            {
                using var tcp = new TcpClient();
                if (!tcp.ConnectAsync("127.0.0.1", port).Wait(1500)) continue;
                using var s = tcp.GetStream();
                s.WriteTimeout = 1500;
                s.ReadTimeout = 1500;
                // Greeting: no auth. Then CONNECT to 20.190.128.0 (one of
                // login.microsoftonline.com's IPs is resolved by the proxy).
                s.Write(new byte[] { 0x05, 0x01, 0x00 }, 0, 3);
                var greet = new byte[2];
                if (s.Read(greet, 0, 2) < 2 || greet[0] != 0x05 || greet[1] != 0x00) continue;
                s.Write(new byte[] { 0x05, 0x01, 0x00, 0x01, 20, 190, 128, 0, 0x01, 0xBB }, 0, 10);
                var rep = new byte[10];
                if (s.Read(rep, 0, 10) >= 2 && rep[0] == 0x05 && rep[1] == 0x00)
                    return "socks5://127.0.0.1:" + port;
            }
            catch { }
        }
        return null;
    }
}
