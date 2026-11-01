// Settings: gateway version, change admin password, theme, logout,
// stop gateway and exit, and the local paths (for debugging).
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace M365Hub.App.Pages;

public sealed class SettingsPage : PageBase
{
    public SettingsPage(AppState state, MainWindow win) : base(state, win)
    {
        Title("设置");
        var cur = new PasswordBox { PlaceholderText = "当前密码", Width = 320 };
        var next = new PasswordBox { PlaceholderText = "新密码", Width = 320 };
        Body.Children.Add(new TextBlock { Text = "修改管理员密码", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        Body.Children.Add(cur);
        Body.Children.Add(next);
        Btn("修改密码", async (_, _) =>
        {
            try
            {
                await State.Api.PostAsync("/api/admin/password",
                    new JsonObject { ["current"] = cur.Password, ["next"] = next.Password });
                ErrText.Text = "密码已修改。";
                ErrText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 0));
                ErrText.Visibility = Visibility.Visible;
            }
            catch (Exception e) { ShowErr(e.Message); }
        });
        Btn(State.ThemeDark ? "切换到浅色" : "切换到深色", (_, _) => Win.ToggleTheme());
        Btn("退出登录", (_, _) =>
        {
            State.Api.ClearCookie();
            Win.Navigate("login");
        });
        Btn("停止网关并退出", (_, _) =>
        {
            State.Gateway.Stop();
            Application.Current.Exit();
        });
        var info = new StackPanel { Spacing = 2 };
        info.Children.Add(new TextBlock { Text = "本机信息", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        info.Children.Add(Row("CPU 架构：", RuntimeInformation.OSArchitecture.ToString()));
        info.Children.Add(Row("网关程序：", State.Gateway.GatewayExePath() ?? "未找到"));
        info.Children.Add(Row("数据目录：", State.AppDataDir));
        info.Children.Add(Row("网关日志：", State.Gateway.LogPath));
        Body.Children.Add(Card(info));
        _ = LoadVersionAsync(info);
    }

    private async Task LoadVersionAsync(StackPanel info)
    {
        try
        {
            var d = await State.Api.GetAsync("/api/version");
            foreach (var kv in d)
            {
                if (kv.Value is JsonValue v)
                    info.Children.Add(Row(kv.Key + "：", v.ToJsonString().Trim('"')));
            }
        }
        catch { }
    }
}
