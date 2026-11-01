// Accounts: search + status filter, per-account cards (calls in
// 30 days, in-flight, cooldown, token expiry, bound proxy), select
// for batch ops (schedule / web search / system prompt), PKCE and
// device-code add flows (auto-copy link, paste callback, errors
// shown inside the dialog), model test. Mirrors Android Screens.java.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using M365Hub.Core;
using System.Text.Json.Nodes;
using Windows.ApplicationModel.DataTransfer;

namespace M365Hub.App.Pages;

public sealed class AccountsPage : PageBase
{
    private List<AccountInfo> _accounts = new();
    private readonly HashSet<string> _selected = new();
    private readonly Dictionary<string, int> _calls30 = new();
    private string _query = "";
    private int _filter; // 0 all, 1 online, 2 cooling, 3 offline
    private readonly TextBox _search = new() { PlaceholderText = "搜索邮箱或名字", Width = 280 };
    private readonly StackPanel _list = new() { Spacing = 12 };
    private readonly StackPanel _batchBar = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly CheckBox _schedBox = new() { Content = "排程", IsThreeState = true, IsChecked = null };
    private readonly CheckBox _webBox = new() { Content = "联网搜索", IsThreeState = true, IsChecked = null };
    private readonly TextBox _promptBox = new() { PlaceholderText = "系统提示词（留空不改）", Width = 320 };

    public AccountsPage(AppState state, MainWindow win) : base(state, win)
    {
        Title("账号");
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(new GlassButton("＋ 添加账号（PKCE）", async (_, _) => await ShowAuthDialogAsync("pkce")));
        actions.Children.Add(new GlassButton("设备码（回退）", async (_, _) => await ShowAuthDialogAsync("device")));
        actions.Children.Add(new GlassButton("全部刷新", async (_, _) => await RefreshAllAsync()));
        actions.Children.Add(new GlassButton("模型测试", async (_, _) => await ShowModelTestAsync()));
        Body.Children.Add(actions);

        _search.TextChanged += (_, _) => { _query = _search.Text; RenderList(); };
        Body.Children.Add(_search);
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        string[] names = { "全部", "在线", "冷却", "离线" };
        for (int i = 0; i < names.Length; i++)
        {
            int fi = i;
            filters.Children.Add(new GlassButton(names[i], (_, _) => { _filter = fi; RenderList(); }));
        }
        Body.Children.Add(filters);

        var applyBtn = new GlassButton("应用到选中账号", async (_, _) => await ApplyBatchAsync());
        var clearBtn = new GlassButton("清除提示词", async (_, _) => await ClearPromptAsync());
        var batchRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        batchRow.Children.Add(_schedBox);
        batchRow.Children.Add(_webBox);
        batchRow.Children.Add(_promptBox);
        batchRow.Children.Add(applyBtn);
        batchRow.Children.Add(clearBtn);
        _batchBar.Children.Add(new TextBlock { Text = "批量操作（已选账号）：三态框留空 = 不改", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        _batchBar.Children.Add(batchRow);
        Body.Children.Add(_batchBar);
        Body.Children.Add(_list);
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        ClearErr();
        try
        {
            var d = await State.Api.GetAsync("/api/accounts");
            _accounts = ApiClient.Arr(d, "accounts")
                .Select(x => AccountInfo.FromJson(x!.AsObject())).ToList();
            var u = await State.Api.GetAsync("/api/usage?days=30");
            _calls30.Clear();
            if (u["byAccount"] is JsonObject byAcc)
            {
                foreach (var kv in byAcc)
                {
                    int n = kv.Value?["requests"]?.GetValue<int>() ?? 0;
                    _calls30[kv.Key] = n;
                }
            }
            RenderList();
        }
        catch (Exception e)
        {
            ShowErr(e.Message);
        }
    }

    private void RenderList()
    {
        _list.Children.Clear();
        _batchBar.Visibility = _selected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var a in _accounts)
        {
            if (_query.Length > 0
                && !a.Email.Contains(_query, StringComparison.OrdinalIgnoreCase)
                && !a.DisplayName.Contains(_query, StringComparison.OrdinalIgnoreCase))
                continue;
            if (_filter == 1 && !a.IsOnline) continue;
            if (_filter == 2 && !a.IsCooling) continue;
            if (_filter == 3 && a.IsOnline) continue;
            int calls = _calls30.TryGetValue(a.Id, out int c1) ? c1
                : _calls30.TryGetValue(a.Email, out int c2) ? c2 : 0;
            string status = a.IsCooling ? "冷却中" : a.IsOnline ? "在线" : a.Status;
            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(new TextBlock
            {
                Text = (a.DisplayName.Length > 0 ? a.DisplayName + " · " : "") + a.Email + " · " + status,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            });
            stack.Children.Add(Row("调用 " + calls + " 次（30 天） · 在途 " + a.InFlight + " · 冷却 " + a.CooldownSec + " 秒",
                a.ExpiresAt.Length > 0 ? " · Token 过期 " + TimeShort(a.ExpiresAt) : ""));
            stack.Children.Add(Row("绑定代理：", a.BoundProxy.Length > 0 ? a.BoundProxy : "未绑定"));
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
            bool sel = _selected.Contains(a.Id);
            var selBtn = new GlassButton(sel ? "✓ 已选" : "选择", (_, _) =>
            {
                if (_selected.Contains(a.Id)) _selected.Remove(a.Id); else _selected.Add(a.Id);
                RenderList();
            });
            var delBtn = new GlassButton("删除", async (_, _) =>
            {
                try
                {
                    await State.Api.PostAsync("/api/accounts/delete",
                        new JsonObject { ["id"] = a.Id });
                    await LoadAsync();
                }
                catch (Exception e) { ShowErr(e.Message); }
            });
            btnRow.Children.Add(selBtn);
            btnRow.Children.Add(delBtn);
            stack.Children.Add(btnRow);
            _list.Children.Add(Card(stack));
        }
        if (_list.Children.Count == 0)
            _list.Children.Add(new TextBlock { Text = "没有符合条件的账号。" });
    }

    private async Task RefreshAllAsync()
    {
        try
        {
            var d = await State.Api.PostAsync("/api/accounts/refresh", new JsonObject());
            int ok = 0, total = 0;
            foreach (var r in ApiClient.Arr(d, "results"))
            {
                total++;
                if (r!["ok"]?.GetValue<bool>() == true) ok++;
            }
            ShowErr(""); // reuse the error line as a status line
            ErrText.Text = "刷新完成：" + ok + "/" + total + " 成功。";
            ErrText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 0));
            ErrText.Visibility = Visibility.Visible;
            await LoadAsync();
        }
        catch (Exception e) { ShowErr(e.Message); }
    }

    private async Task ApplyBatchAsync()
    {
        if (_selected.Count == 0) return;
        var body = new JsonObject();
        var ids = new JsonArray();
        foreach (string id in _selected) ids.Add(id);
        body["ids"] = ids;
        if (_schedBox.IsChecked.HasValue) body["schedule"] = _schedBox.IsChecked.Value;
        if (_webBox.IsChecked.HasValue) body["web_search"] = _webBox.IsChecked.Value;
        if (_promptBox.Text.Length > 0) body["system_prompt"] = _promptBox.Text;
        try
        {
            var d = await State.Api.PostAsync("/api/accounts/batch", body);
            ErrText.Text = "已更新 " + (d["updated"]?.GetValue<int>() ?? 0) + " 个账号。";
            ErrText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 0));
            ErrText.Visibility = Visibility.Visible;
            _selected.Clear();
            await LoadAsync();
        }
        catch (Exception e) { ShowErr(e.Message); }
    }

    private async Task ClearPromptAsync()
    {
        if (_selected.Count == 0) return;
        var body = new JsonObject();
        var ids = new JsonArray();
        foreach (string id in _selected) ids.Add(id);
        body["ids"] = ids;
        body["system_prompt"] = "";
        try
        {
            await State.Api.PostAsync("/api/accounts/batch", body);
            _selected.Clear();
            await LoadAsync();
        }
        catch (Exception e) { ShowErr(e.Message); }
    }

    private async Task ShowAuthDialogAsync(string flow)
    {
        string authUrl, userCode = "";
        try
        {
            var d = await State.Api.PostAsync("/api/auth/start",
                new JsonObject { ["flow"] = flow });
            authUrl = d["authUrl"]?.GetValue<string>() ?? "";
            userCode = d["userCode"]?.GetValue<string>() ?? "";
            if (authUrl.Length == 0) { ShowErr("网关没有返回授权链接。"); return; }
        }
        catch (Exception e) { ShowErr(e.Message); return; }

        // Auto-copy the link: on this machine the browser opens the
        // right page, but the copy is the fallback if it lands on a
        // home page instead.
        var dp = new DataPackage();
        dp.SetText(authUrl);
        Clipboard.SetContent(dp);

        var hint = new TextBlock
        {
            Text = flow == "device"
                ? "授权链接已复制。在浏览器打开链接，输入下面的代码完成登录。"
                : "授权链接已复制到剪贴板；若浏览器只打开首页，请在地址栏粘贴链接，完成登录后把地址栏的完整回调 URL 粘贴回来。",
            TextWrapping = TextWrapping.Wrap,
        };
        var input = new TextBox
        {
            PlaceholderText = flow == "device" ? "用户代码" : "粘贴浏览器地址栏的完整回调 URL",
            Text = flow == "device" ? userCode : "",
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 64,
            Margin = new Thickness(0, 8, 0, 0),
        };
        if (flow == "device")
        {
            input.FontSize = 24;
            input.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        }
        var dlgErr = new TextBlock
        {
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 60, 60)),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        var openBtn = new GlassButton("打开浏览器", async (_, _) =>
        {
            try { await Windows.System.Launcher.LaunchUriAsync(new Uri(authUrl)); } catch { }
        });
        var pasteBtn = new GlassButton("从剪贴板粘贴", async (_, _) =>
        {
            try
            {
                var content = Clipboard.GetContent();
                if (content.Contains(StandardDataFormats.Text))
                    input.Text = await content.GetTextAsync();
            }
            catch { }
        });
        row.Children.Add(openBtn);
        row.Children.Add(pasteBtn);
        var panel = new StackPanel();
        panel.Children.Add(hint);
        panel.Children.Add(input);
        panel.Children.Add(dlgErr);
        panel.Children.Add(row);

        var dlg = new ContentDialog
        {
            Title = flow == "device" ? "添加账号（设备码）" : "添加账号（PKCE）",
            Content = panel,
            PrimaryButtonText = "确认并添加",
            SecondaryButtonText = "复制授权链接",
            CloseButtonText = "取消",
            XamlRoot = Win.Content.XamlRoot,
        };
        dlg.SecondaryButtonClick += (_, _) =>
        {
            var dp2 = new DataPackage();
            dp2.SetText(authUrl);
            Clipboard.SetContent(dp2);
        };
        dlg.PrimaryButtonClick += async (_, args) =>
        {
            var def = args.GetDeferral();
            try
            {
                string value = input.Text.Trim();
                if (value.Length == 0)
                {
                    dlgErr.Text = flow == "device" ? "请输入用户代码。" : "请先粘贴回调 URL。";
                    dlgErr.Visibility = Visibility.Visible;
                    args.Cancel = true;
                    return;
                }
                var body = new JsonObject { ["flow"] = flow };
                if (flow == "device") body["userCode"] = value;
                else body["callbackUrl"] = value;
                var d = await State.Api.PostAsync("/api/auth/callback", body);
                ErrText.Text = "已授权 " + (d["email"]?.GetValue<string>() ?? "") + "。";
                ErrText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 0));
                ErrText.Visibility = Visibility.Visible;
                await LoadAsync();
            }
            catch (Exception e)
            {
                dlgErr.Text = e.Message;
                dlgErr.Visibility = Visibility.Visible;
                args.Cancel = true;
            }
            finally
            {
                def.Complete();
            }
        };
        await dlg.ShowAsync();
    }

    private async Task ShowModelTestAsync()
    {
        var modelBox = new ComboBox { MinWidth = 260 };
        var promptBox = new TextBox { Text = "你好", Margin = new Thickness(0, 8, 0, 0) };
        var result = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        try
        {
            var d = await State.Api.GetAsync("/api/admin/models");
            foreach (var m in ModelInfo.FromJson(d["models"])) modelBox.Items.Add(m.Id);
            if (modelBox.Items.Count > 0) modelBox.SelectedIndex = 0;
        }
        catch { }
        var runBtn = new GlassButton("运行测试", async (_, _) =>
        {
            if (modelBox.SelectedItem is not string model) return;
            result.Text = "测试中…";
            try
            {
                var d = await State.Api.PostAsync("/api/admin/models/test",
                    new JsonObject { ["model"] = model, ["prompt"] = promptBox.Text });
                if (d["ok"]?.GetValue<bool>() == true)
                {
                    result.Text = "✓ 健康 · " + (d["duration_ms"]?.GetValue<int>() ?? 0) + " ms · "
                        + (d["account"]?.GetValue<string>() ?? "") + "\n"
                        + (d["content"]?.GetValue<string>() ?? "");
                }
                else
                {
                    result.Text = "✗ 失败 · " + (d["duration_ms"]?.GetValue<int>() ?? 0) + " ms · "
                        + (d["error"]?.GetValue<string>() ?? "");
                }
            }
            catch (Exception e) { result.Text = "✗ 失败 · " + e.Message; }
        });
        var panel = new StackPanel();
        panel.Children.Add(modelBox);
        panel.Children.Add(promptBox);
        panel.Children.Add(runBtn);
        panel.Children.Add(result);
        var dlg = new ContentDialog
        {
            Title = "模型测试",
            Content = panel,
            CloseButtonText = "关闭",
            XamlRoot = Win.Content.XamlRoot,
        };
        await dlg.ShowAsync();
    }
}
