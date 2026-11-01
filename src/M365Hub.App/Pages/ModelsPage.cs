// Model list (read-only; the registry is static in the gateway).
// The chat default is gpt-6.1-sol-reasoning.
using M365Hub.Core;

namespace M365Hub.App.Pages;

public sealed class ModelsPage : PageBase
{
    public ModelsPage(AppState state, MainWindow win) : base(state, win)
    {
        Title("模型");
        Body.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
        {
            Text = "聊天默认模型：gpt-6.1-sol-reasoning（可在聊天页顶栏切换）。",
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
        });
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var d = await State.Api.GetAsync("/api/admin/models");
            var models = ModelInfo.FromJson(d["models"]);
            var list = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 4 };
            foreach (var m in models)
            {
                list.Children.Add(Row(m.Id, m.Name != m.Id ? "（" + m.Name + "）" : ""));
            }
            if (models.Count == 0) list.Children.Add(Row("", "没有返回模型。"));
            Body.Children.Add(Card(list));
        }
        catch (Exception e)
        {
            ShowErr(e.Message);
        }
    }
}
