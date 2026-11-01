// Conversations: local session bindings + cloud conversations from
// /api/m365/conversations, with delete. A cloud_error here means the
// cloud pull failed (DNS on the phone was the old cause); the gateway
// now routes it through the outbound DoH client.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using M365Hub.Core;
using System.Text.Json.Nodes;

namespace M365Hub.App.Pages;

public sealed class ConversationsPage : PageBase
{
    public ConversationsPage(AppState state, MainWindow win) : base(state, win)
    {
        Title("对话");
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var d = await State.Api.GetAsync("/api/m365/conversations");
            var convs = ApiClient.Arr(d, "conversations");
            var cloud = ApiClient.Arr(d, "cloud_conversations");
            string cloudErr = d["cloud_error"]?.GetValue<string>() ?? "";
            bool autoCleanup = d["autoCleanup"]?.GetValue<bool>() ?? false;
            int keepN = d["keepN"]?.GetValue<int>() ?? 0;
            int maxAge = d["maxAgeHours"]?.GetValue<int>() ?? 0;
            var head = new StackPanel { Spacing = 2 };
            head.Children.Add(Row("本地绑定 " + convs.Count + " 个 · 云端 " + cloud.Count + " 个",
                " · 自动清理 " + (autoCleanup ? "开" : "关") + "（保留 " + keepN + " 个 / " + maxAge + " 小时）"));
            if (cloudErr.Length > 0)
                head.Children.Add(new TextBlock
                {
                    Text = "云端拉取失败：" + cloudErr,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 60, 60)),
                    TextWrapping = TextWrapping.Wrap,
                });
            Body.Children.Add(Card(head));

            foreach (var item in convs)
            {
                var o = item!.AsObject();
                string id = o["conversation_id"]?.GetValue<string>() ?? "";
                string tone = o["tone"]?.GetValue<string>() ?? "";
                int turns = o["turns"]?.GetValue<int>() ?? 0;
                string last = o["last_used_at"]?.GetValue<string>() ?? "";
                var stack = new StackPanel { Spacing = 2 };
                stack.Children.Add(new TextBlock
                {
                    Text = (id.Length > 12 ? id.Substring(0, 12) + "…" : id) + " · " + tone + " · " + turns + " 轮",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                });
                if (last.Length > 0) stack.Children.Add(Row("最近使用：", TimeShort(last)));
                string cid = id;
                stack.Children.Add(new GlassButton("删除", async (_, _) =>
                {
                    try
                    {
                        await State.Api.PostAsync("/api/conversations/delete",
                            new JsonObject { ["id"] = cid });
                        Body.Children.Clear();
                        Body.Children.Add(ErrText);
                        Title("对话");
                        await LoadAsync();
                    }
                    catch (Exception e) { ShowErr(e.Message); }
                }));
                Body.Children.Add(Card(stack));
            }
            if (convs.Count == 0)
                Body.Children.Add(new TextBlock { Text = "本地暂无绑定（还没聊过，或绑定 2 小时闲置后被清理）。" });
            foreach (var item in cloud)
            {
                var o = item!.AsObject();
                string id = o["conversation_id"]?.GetValue<string>() ?? o["id"]?.GetValue<string>() ?? "";
                string name = o["chatName"]?.GetValue<string>() ?? o["title"]?.GetValue<string>() ?? "";
                string email = o["accountEmail"]?.GetValue<string>() ?? "";
                var stack = new StackPanel { Spacing = 2 };
                stack.Children.Add(new TextBlock
                {
                    Text = "云端 · " + (name.Length > 0 ? name : id) + (email.Length > 0 ? " · " + email : ""),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                });
                Body.Children.Add(Card(stack));
            }
        }
        catch (Exception e)
        {
            ShowErr(e.Message);
        }
    }
}
