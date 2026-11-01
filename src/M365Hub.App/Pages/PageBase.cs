// Shared helpers for the console pages (mirrors Android Screens.java
// helpers: card(), row(), full-width buttons, timeShort).
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace M365Hub.App.Pages;

public abstract class PageBase : UserControl
{
    protected readonly AppState State;
    protected readonly MainWindow Win;
    protected readonly StackPanel Body = new() { Spacing = 12, Margin = new Thickness(24) };
    protected readonly TextBlock ErrText = new()
    {
        Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 60, 60)),
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed,
    };

    protected PageBase(AppState state, MainWindow win)
    {
        State = state;
        Win = win;
        var scroll = new ScrollViewer
        {
            Content = Body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Content = scroll;
        Body.Children.Add(ErrText);
    }

    protected uint TextColor => State.ThemeDark ? 0xFFFFFFFFu : 0xFF1A1A1Au;
    protected ElementTheme Theme => State.ThemeDark ? ElementTheme.Dark : ElementTheme.Light;

    protected void Title(string t)
    {
        Body.Children.Add(new TextBlock
        {
            Text = t,
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
    }

    protected GlassButton Btn(string text, RoutedEventHandler onClick)
    {
        var b = new GlassButton(text, onClick);
        Body.Children.Add(b);
        return b;
    }

    protected void ShowErr(string m)
    {
        ErrText.Text = m;
        ErrText.Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 60, 60));
        ErrText.Visibility = Visibility.Visible;
    }

    protected void ClearErr() => ErrText.Visibility = Visibility.Collapsed;

    protected Border Card(UIElement child) => Glass.Card(child, Theme, 18);

    protected static Border StatCard(string label, string value, ElementTheme theme)
    {
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(new TextBlock { Text = label, FontSize = 13, Foreground = new SolidColorBrush(Color.FromArgb(255, 128, 128, 128)) });
        stack.Children.Add(new TextBlock { Text = value, FontSize = 19, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        return Glass.Card(stack, theme, 18);
    }

    /// <summary>"2026-10-30T15:04:05Z" -> "10-30 15:04".</summary>
    protected static string TimeShort(string iso)
    {
        if (iso.Length >= 16 && iso[4] == '-' && iso[10] == 'T')
            return iso.Substring(5, 5) + " " + iso.Substring(11, 5);
        return iso;
    }

    protected static TextBlock Row(string label, string value) => new()
    {
        Text = label + value,
        TextWrapping = TextWrapping.Wrap,
    };
}
