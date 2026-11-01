// Admin login, including the forced password change when the
// gateway still runs the default password (no session is issued
// until the password is replaced).
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json.Nodes;

namespace M365Hub.App.Pages;

public sealed class LoginPage : PageBase
{
    private readonly PasswordBox _pw = new() { PlaceholderText = "管理员密码", Width = 320 };
    private readonly PasswordBox _newPw = new() { PlaceholderText = "新密码（首次登录请先改密）", Width = 320, Visibility = Visibility.Collapsed };
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap };

    public LoginPage(AppState state, MainWindow win) : base(state, win)
    {
        Title("登录 M365Hub");
        Body.Children.Add(new TextBlock
        {
            Text = "网关已启动。请输入控制台管理员密码（首次登录用默认密码 admin123，登录后会被要求改掉）。",
            TextWrapping = TextWrapping.Wrap,
        });
        Body.Children.Add(_pw);
        Body.Children.Add(_newPw);
        Body.Children.Add(_hint);
        Btn("登录", async (_, _) => await LoginAsync());
    }

    private bool _needsChange;

    private async Task LoginAsync()
    {
        ClearErr();
        string pw = _pw.Password;
        if (pw.Length == 0) { ShowErr("请输入密码。"); return; }
        try
        {
            if (_needsChange)
            {
                if (_newPw.Password.Length == 0) { ShowErr("请输入新密码。"); return; }
                await State.Api.PostAsync("/api/admin/password",
                    new JsonObject { ["current"] = pw, ["next"] = _newPw.Password });
                Win.Navigate("chat");
                return;
            }
            var d = await State.Api.PostAsync("/api/admin/login",
                new JsonObject { ["password"] = pw });
            if (d["must_change_password"]?.GetValue<bool>() == true)
            {
                _needsChange = true;
                _newPw.Visibility = Visibility.Visible;
                _hint.Text = "当前是默认密码，请先设置一个新密码再登录。";
                return;
            }
            Win.Navigate("chat");
        }
        catch (Exception e)
        {
            ShowErr(e.Message);
        }
    }
}
