// Usage: range selector, totals cards, daily token trend (polyline),
// top-8 bars by model / account / key / endpoint (by request count),
// and the request log with per-entry latency. Logs cover 30 days
// even when the range is 90 days.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using M365Hub.Core;
using System.Text.Json.Nodes;
using Windows.UI;

namespace M365Hub.App.Pages;

public sealed class UsagePage : PageBase
{
    private int _days = 7;
    private readonly StackPanel _content = new() { Spacing = 12 };

    public UsagePage(AppState state, MainWindow win) : base(state, win)
    {
        Title("用量统计");
        var range = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (int d in new[] { 1, 7, 30, 90 })
        {
            int dd = d;
            var b = new GlassButton(d + "天", (_, _) => { _days = dd; _ = LoadAsync(); });
            range.Children.Add(b);
        }
        Body.Children.Add(range);
        Body.Children.Add(_content);
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _content.Children.Clear();
        ClearErr();
        try
        {
            var d = await State.Api.GetAsync("/api/usage?days=" + _days);
            var logs = await State.Api.GetAsync("/api/usage/logs?limit=50");
            var t = UsageTotals.FromJson(d["totals"]!.AsObject());
            var cards = new Grid();
            cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cards.RowDefinitions.Add(new RowDefinition());
            cards.RowDefinitions.Add(new RowDefinition());
            void AddCard(Border c, int r, int col)
            {
                Grid.SetRow(c, r);
                Grid.SetColumn(c, col);
                c.Margin = new Thickness(0, 0, 8, 8);
                cards.Children.Add(c);
            }
            AddCard(StatCard("总请求", t.Requests + " 次", Theme), 0, 0);
            AddCard(StatCard("总 Token", "输入 " + t.PromptTokens + " · 输出 " + t.OutputTokens, Theme), 0, 1);
            AddCard(StatCard("缓存命中", t.SessionHits + " 次 · 省 " + t.SavedTokens + " token", Theme), 1, 0);
            var logArr = ApiClient.Arr(logs, "logs");
            double avgMs = 0;
            int latN = 0;
            foreach (var item in logArr)
            {
                var o = item!.AsObject();
                if (o["latencyMs"] != null) { avgMs += o["latencyMs"]!.GetValue<int>(); latN++; }
            }
            AddCard(StatCard("平均延迟", latN > 0 ? ((int)(avgMs / latN)) + " ms" : "—", Theme), 1, 1);
            _content.Children.Add(cards);

            // Daily trend.
            var byDay = d["byDay"] as JsonObject;
            if (byDay != null && byDay.Count > 0)
            {
                var days = byDay.Select(kv => kv.Key).OrderBy(k => k).ToList();
                var vals = days.Select(k =>
                {
                    var o = byDay[k]!.AsObject();
                    return (o["promptTokens"]?.GetValue<long>() ?? 0) + (o["outputTokens"]?.GetValue<long>() ?? 0);
                }).ToList();
                long max = Math.Max(1, vals.Max());
                var canvas = new Canvas { Height = 180, Margin = new Thickness(8) };
                double w = 640, h = 150;
                var line = new Polyline
                {
                    Stroke = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)),
                    StrokeThickness = 2,
                };
                for (int i = 0; i < days.Count; i++)
                {
                    double x = days.Count == 1 ? w / 2 : 20 + i * (w - 40) / (days.Count - 1);
                    double y = 10 + (1 - (double)vals[i] / max) * (h - 20);
                    line.Points.Add(new Windows.Foundation.Point(x, y));
                    var dot = new Ellipse
                    {
                        Width = 6, Height = 6,
                        Fill = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)),
                    };
                    Canvas.SetLeft(dot, x - 3);
                    Canvas.SetTop(dot, y - 3);
                    canvas.Children.Add(dot);
                }
                canvas.Children.Add(line);
                var first = new TextBlock { Text = days[0], FontSize = 11 };
                Canvas.SetLeft(first, 0);
                Canvas.SetTop(first, h + 4);
                canvas.Children.Add(first);
                var last = new TextBlock { Text = days[^1], FontSize = 11 };
                Canvas.SetLeft(last, w - 60);
                Canvas.SetTop(last, h + 4);
                canvas.Children.Add(last);
                var trendCard = new StackPanel { Spacing = 4 };
                trendCard.Children.Add(new TextBlock { Text = "每日 Token（" + _days + " 天）", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                trendCard.Children.Add(canvas);
                _content.Children.Add(Card(trendCard));
            }

            AddGroup("按模型（请求数）", d["byModel"] as JsonObject);
            AddGroup("按账号（请求数）", d["byAccount"] as JsonObject);
            AddGroup("按 Key（请求数）", d["byKey"] as JsonObject);
            AddGroup("按端点（请求数）", d["byEndpoint"] as JsonObject);

            var logPanel = new StackPanel { Spacing = 4 };
            logPanel.Children.Add(new TextBlock { Text = "请求明细（最近 50 条，30 天内）", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            foreach (var item in logArr)
            {
                var o = item!.AsObject();
                string at = o["at"]?.GetValue<string>() ?? "";
                string model = o["model"]?.GetValue<string>() ?? "";
                string ep = o["endpoint"]?.GetValue<string>() ?? "";
                int pt = o["promptTokens"]?.GetValue<int>() ?? 0;
                int ot = o["outputTokens"]?.GetValue<int>() ?? 0;
                int lat = o["latencyMs"]?.GetValue<int>() ?? 0;
                bool ok = o["ok"]?.GetValue<bool>() ?? true;
                logPanel.Children.Add(Row(TimeShort(at) + " · " + model + " · " + ep,
                    " " + pt + "/" + ot + " · " + lat + " ms" + (ok ? "" : " · 失败")));
            }
            if (logArr.Count == 0) logPanel.Children.Add(new TextBlock { Text = "暂无记录。" });
            _content.Children.Add(Card(logPanel));
        }
        catch (Exception e)
        {
            ShowErr(e.Message);
        }
    }

    private void AddGroup(string title, JsonObject? group)
    {
        if (group == null || group.Count == 0) return;
        var rows = group.Select(kv => (Key: kv.Key, Val: kv.Value?["requests"]?.GetValue<int>() ?? 0))
            .OrderByDescending(x => x.Val).Take(8).ToList();
        int max = Math.Max(1, rows.Max(x => x.Val));
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        foreach (var (key, val) in rows)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock { Text = key, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(label, 0);
            var bar = new Rectangle
            {
                Height = 12,
                Width = Math.Max(4, 220.0 * val / max),
                Fill = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(bar, 1);
            var v = new TextBlock { Text = val.ToString() };
            Grid.SetColumn(v, 2);
            row.Children.Add(label);
            row.Children.Add(bar);
            row.Children.Add(v);
            panel.Children.Add(row);
        }
        _content.Children.Add(Card(panel));
    }
}
