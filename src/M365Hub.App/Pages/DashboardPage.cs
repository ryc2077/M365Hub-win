// Dashboard: 24h / cumulative overview cards, cache card, session
// count, and the recent-requests table (the upstream console's
// dashboard data, via the existing /api/usage, /api/usage/logs,
// /api/sessions and /api/stats endpoints).
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using M365Hub.Core;
using System.Text.Json.Nodes;

namespace M365Hub.App.Pages;

public sealed class DashboardPage : PageBase
{
    public DashboardPage(AppState state, MainWindow win) : base(state, win)
    {
        Title("仪表盘");
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var d1 = await State.Api.GetAsync("/api/usage?days=1");
            var d365 = await State.Api.GetAsync("/api/usage?days=365");
            var sess = await State.Api.GetAsync("/api/sessions");
            var logs = await State.Api.GetAsync("/api/usage/logs?limit=6");
            var stats = await State.Api.GetAsync("/api/stats");

            var t1 = UsageTotals.FromJson(d1["totals"]!.AsObject());
            var t365 = UsageTotals.FromJson(d365["totals"]!.AsObject());
            var cards = new Grid();
            cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            void AddCard(Border c, int r, int col)
            {
                Grid.SetRow(c, r);
                Grid.SetColumn(c, col);
                c.Margin = new Thickness(0, 0, 8, 8);
                cards.Children.Add(c);
            }
            cards.RowDefinitions.Add(new RowDefinition());
            cards.RowDefinitions.Add(new RowDefinition());
            AddCard(StatCard("24 小时请求", t1.Requests + " 次 · " + (t1.PromptTokens + t1.OutputTokens) + " token", Theme), 0, 0);
            AddCard(StatCard("累计请求", t365.Requests + " 次 · " + (t365.PromptTokens + t365.OutputTokens) + " token", Theme), 0, 1);
            AddCard(StatCard("缓存", "命中 " + t365.SessionHits + " 次 · 命中率 " + t365.HitRate.ToString("P0") + " · 省 " + t365.SavedTokens + " token", Theme), 1, 0);
            int sessCount = sess["sessions"] is JsonArray sa ? sa.Count : 0;
            AddCard(StatCard("会话总数", sessCount.ToString(), Theme), 1, 1);
            Body.Children.Add(cards);

            var statRows = new StackPanel { Spacing = 2 };
            statRows.Children.Add(new TextBlock { Text = "网关状态", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            foreach (var kv in stats)
            {
                if (kv.Value is JsonValue v)
                    statRows.Children.Add(Row(kv.Key + "：", v.ToJsonString().Trim('"')));
            }
            Body.Children.Add(Card(statRows));

            var recent = new StackPanel { Spacing = 4 };
            recent.Children.Add(new TextBlock { Text = "最近请求", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            foreach (var item in ApiClient.Arr(logs, "logs"))
            {
                var o = item!.AsObject();
                string at = o["at"]?.GetValue<string>() ?? "";
                string model = o["model"]?.GetValue<string>() ?? "";
                string ep = o["endpoint"]?.GetValue<string>() ?? "";
                int pt = o["promptTokens"]?.GetValue<int>() ?? 0;
                int ot = o["outputTokens"]?.GetValue<int>() ?? 0;
                bool ok = o["ok"]?.GetValue<bool>() ?? true;
                recent.Children.Add(Row(TimeShort(at) + " · " + model + " · " + ep,
                    " " + pt + "/" + ot + " token" + (ok ? "" : " · 失败")));
            }
            if (ApiClient.Arr(logs, "logs").Count == 0)
                recent.Children.Add(new TextBlock { Text = "暂无请求记录。" });
            Body.Children.Add(Card(recent));
        }
        catch (Exception e)
        {
            ShowErr(e.Message);
        }
    }
}
