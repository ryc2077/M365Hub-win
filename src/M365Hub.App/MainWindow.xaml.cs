using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using M365Hub.Core;

namespace M365Hub.App;

/// <summary>Shell: Mica window, NavigationView (chat first), history in
/// the pane footer, startup overlay until the gateway answers.</summary>
public sealed partial class MainWindow : Window
{
    public AppState State { get; } = new();
    private string _page = "chat";
    private bool _started;

    public MainWindow()
    {
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();
        Title = "M365Hub";
        try { AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "app.ico")); } catch { /* 图标缺失时不影响启动 */ }
        ApplyTheme();
        Glass.ApplyButton(NewChatBtn, 20);
        Glass.ApplyButton(ThemeBtn, 20);
        Loaded += (_, _) => { if (!_started) { _started = true; _ = StartupAsync(); } };
    }

    public void ApplyTheme()
    {
        RootGrid.RequestedTheme = State.ThemeDark ? ElementTheme.Dark : ElementTheme.Light;
        ThemeBtn.Content = State.ThemeDark ? "☀️" : "🌙";
    }

    public void ToggleTheme()
    {
        State.ThemeDark = !State.ThemeDark;
        ApplyTheme();
        Navigate(_page);
    }

    private async Task StartupAsync()
    {
        StartupOverlay.Visibility = Visibility.Visible;
        StartupButtons.Visibility = Visibility.Collapsed;
        LogView.Visibility = Visibility.Collapsed;
        var progress = new Progress<string>(s =>
            StartupStatus.Text = s);
        if (!State.Gateway.Start(out string? err))
        {
            ShowStartupFailure("网关启动失败：" + err);
            return;
        }
        bool ready = await State.Gateway.WaitReadyAsync(State.Api, progress);
        if (!ready)
        {
            ShowStartupFailure("本地网关 60 秒内没有应答。可能是被系统杀掉，或端口 8230 被占用。");
            return;
        }
        // Session check: logged in -> chat, otherwise login page.
        try
        {
            var s = await State.Api.GetAsync("/api/admin/session");
            StartupOverlay.Visibility = Visibility.Collapsed;
            RefreshHistory();
            Navigate(s["loggedIn"]?.GetValue<bool>() == true ? "chat" : "login");
        }
        catch (Exception e)
        {
            ShowStartupFailure("网关已应答，但会话检查失败：" + e.Message);
        }
    }

    private void ShowStartupFailure(string msg)
    {
        StartupStatus.Text = msg;
        StartupButtons.Visibility = Visibility.Visible;
        StartupOverlay.Visibility = Visibility.Visible;
    }

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        State.Gateway.Stop();
        _ = StartupAsync();
    }

    private void Log_Click(object sender, RoutedEventArgs e)
    {
        LogText.Text = State.Gateway.LogTail();
        LogView.Visibility = Visibility.Visible;
    }

    private void Theme_Click(object sender, RoutedEventArgs e) => ToggleTheme();

    private void NewChat_Click(object sender, RoutedEventArgs e)
    {
        State.Chat.NewConv();
        RefreshHistory();
        Navigate("chat");
    }

    public void RefreshHistory()
    {
        HistoryList.ItemsSource = null;
        HistoryList.ItemsSource = State.Chat.List
            .Select(c => new HistoryItem { Id = c.Id, Title = c.Title })
            .ToList();
    }

    public sealed class HistoryItem
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
    }

    private void History_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryList.SelectedItem is HistoryItem item)
        {
            State.Chat.CurrentId = item.Id;
            State.Chat.Save();
            Navigate("chat");
        }
    }

    private void HistoryDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string id)
        {
            State.Chat.Delete(id);
            RefreshHistory();
            Navigate("chat");
        }
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
            Navigate(tag);
    }

    /// <summary>Show a page (re-created each time, so it always reads fresh state).</summary>
    public void Navigate(string page)
    {
        _page = page;
        UserControl? ctl = page switch
        {
            "chat" => new Pages.ChatPage(State, this),
            "dashboard" => new Pages.DashboardPage(State, this),
            "accounts" => new Pages.AccountsPage(State, this),
            "models" => new Pages.ModelsPage(State, this),
            "usage" => new Pages.UsagePage(State, this),
            "proxies" => new Pages.ProxiesPage(State, this),
            "conversations" => new Pages.ConversationsPage(State, this),
            "memory" => new Pages.LocalMemoryPage(State, this),
            "settings" => new Pages.SettingsPage(State, this),
            "login" => new Pages.LoginPage(State, this),
            _ => new Pages.ChatPage(State, this),
        };
        PageHost.Children.Clear();
        PageHost.Children.Add(ctl);
        // Keep the nav selection in sync (login has no nav item).
        foreach (var mi in Nav.MenuItems.OfType<NavigationViewItem>())
        {
            if ((mi.Tag as string) == page) { Nav.SelectedItem = mi; break; }
        }
    }
}
