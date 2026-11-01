// Liquid-glass look for WinUI 3. WinUI 3 has no iOS-style refraction
// material; the platform equivalent is AcrylicBrush (compositor
// backdrop blur + tint + noise), which is what this uses: translucent
// tint, diagonal highlight, hairline edge, rounded corners. Static
// per-button (no per-button live background capture — the compositor
// does the blur), the same trade-off as the Android GlassButton.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace M365Hub.App;

public static class Glass
{
    public static AcrylicBrush ButtonBrush(ElementTheme theme) => new()
    {
        TintColor = theme == ElementTheme.Dark ? Color.FromArgb(255, 46, 46, 52) : Color.FromArgb(255, 255, 255, 255),
        TintOpacity = theme == ElementTheme.Dark ? 0.55 : 0.65,
        TintLuminosityOpacity = 0.9,
        FallbackColor = theme == ElementTheme.Dark ? Color.FromArgb(255, 46, 46, 52) : Color.FromArgb(255, 255, 255, 255),
    };

    public static AcrylicBrush CardBrush(ElementTheme theme) => new()
    {
        TintColor = theme == ElementTheme.Dark ? Color.FromArgb(255, 32, 32, 38) : Color.FromArgb(255, 255, 255, 255),
        TintOpacity = theme == ElementTheme.Dark ? 0.6 : 0.75,
        TintLuminosityOpacity = 0.95,
        FallbackColor = theme == ElementTheme.Dark ? Color.FromArgb(255, 32, 32, 38) : Color.FromArgb(255, 255, 255, 255),
    };

    /// <summary>Turn a plain Button into a glass pill/button.</summary>
    public static void ApplyButton(Button b, double radius = 14)
    {
        b.CornerRadius = new CornerRadius(radius);
        b.BorderThickness = new Thickness(1);
        void Refresh()
        {
            var theme = b.ActualTheme;
            b.Background = ButtonBrush(theme);
            b.BorderBrush = new SolidColorBrush(theme == ElementTheme.Dark
                ? Color.FromArgb(0x59, 255, 255, 255)
                : Color.FromArgb(0x40, 0, 0, 0));
            b.Foreground = new SolidColorBrush(theme == ElementTheme.Dark
                ? Microsoft.UI.Colors.White : Color.FromArgb(255, 26, 26, 26));
        }
        b.ActualThemeChanged += (_, _) => Refresh();
        b.Loaded += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>A rounded card container (dashboard cards, message bubbles).</summary>
    public static Border Card(UIElement child, ElementTheme theme, double radius = 18)
    {
        return new Border
        {
            Background = CardBrush(theme),
            CornerRadius = new CornerRadius(radius),
            BorderBrush = new SolidColorBrush(theme == ElementTheme.Dark
                ? Color.FromArgb(0x59, 255, 255, 255)
                : Color.FromArgb(0x40, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 12, 16, 12),
            Child = child,
        };
    }
}

/// <summary>A Button that is glass from construction (no extra call needed).</summary>
public sealed class GlassButton : Button
{
    public GlassButton()
    {
        Glass.ApplyButton(this, 14);
        Padding = new Thickness(16, 8, 16, 8);
    }

    public GlassButton(string text, RoutedEventHandler onClick) : this()
    {
        Content = text;
        Click += onClick;
    }
}
