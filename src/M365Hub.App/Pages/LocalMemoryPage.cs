// Local project memory (tag-based, same store as the gateway's
// /local-memory page): list keys, view summary + turns, delete.
// This is the viewer the Android native app lacks (it only toasts).
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using M365Hub.Core;
using System.Text.Json.Nodes;

namespace M365Hub.App.Pages;

public sealed class LocalMemoryPage : PageBase
{
    private readonly StackPanel _list = new() { Spacing = 12 };
    private readonly TextBlock _detail = new()
    {
        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
        FontSize = 13,
        TextWrapping = TextWrapping.Wrap,
    };

    public LocalMemoryPage(AppState state, MainWindow win) : base(state, win)
    {
        Title("本地记忆");
        Body.Children.Add(new TextBlock
        {
            Text = "按标签（项目）分开存储。请求时附标签，网关按相关度选标签注入；账号轮换或新会话时，未见过的标签会自动注入一次。",
            TextWrapping = TextWrapping.Wrap,
        });
        Body.Children.Add(_list);
        Body.Children.Add(Card(_detail));
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _list.Children.Clear();
        try
        {
            var d = await State.Api.GetAsync("/api/local-memory");
            foreach (var item in ApiClient.Arr(d, "memories"))
            {
                var o = item!.AsObject();
                string key = o["key"]?.GetValue<string>() ?? "";
                int turns = o["turns"]?.GetValue<int>() ?? 0;
                int chars = o["chars"]?.GetValue<int>() ?? 0;
                bool hasSummary = o["hasSummary"]?.GetValue<bool>() ?? false;
                var stack = new StackPanel { Spacing = 2 };
                stack.Children.Add(new TextBlock
                {
                    Text = key + " · " + turns + " 轮 · " + chars + " 字 · " + (hasSummary ? "已压缩" : "未压缩"),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                });
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                string k = key;
                row.Children.Add(new GlassButton("查看", async (_, _) => await ShowDetailAsync(k)));
                row.Children.Add(new GlassButton("删除", async (_, _) => await DeleteAsync(k)));
                stack.Children.Add(row);
                _list.Children.Add(Card(stack));
            }
            if (_list.Children.Count == 0)
                _list.Children.Add(new TextBlock { Text = "暂无本地记忆。" });
        }
        catch (Exception e)
        {
            ShowErr(e.Message);
        }
    }

    private async Task ShowDetailAsync(string key)
    {
        try
        {
            var d = await State.Api.GetAsync("/api/local-memory?key=" + Uri.EscapeDataString(key));
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(key);
            string summary = d["summary"]?.GetValue<string>() ?? "";
            if (summary.Length > 0) sb.AppendLine("[摘要]\n" + summary + "\n");
            foreach (var t in ApiClient.Arr(d, "turns"))
            {
                var o = t!.AsObject();
                sb.AppendLine("[" + (o["role"]?.GetValue<string>() ?? "") + "] "
                    + (o["content"]?.GetValue<string>() ?? ""));
            }
            _detail.Text = sb.ToString();
        }
        catch (Exception e)
        {
            ShowErr(e.Message);
        }
    }

    private async Task DeleteAsync(string key)
    {
        try
        {
            await State.Api.DeleteAsync("/api/local-memory?key=" + Uri.EscapeDataString(key));
            await LoadAsync();
        }
        catch (Exception e)
        {
            ShowErr(e.Message);
        }
    }
}
