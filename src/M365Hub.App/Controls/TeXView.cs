// Renders Core TeX layouts with plain WinUI shapes: each DrawText
// becomes a TextBlock, each DrawLine a Line, each rect a Rectangle.
// No Win2D dependency, so the app only needs the Windows App SDK.
// Measurement uses the same font the drawing uses (Cambria Math),
// so layout and drawing agree; metrics use the same 0.8/0.2 split
// as the test stub.
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using M365Hub.Core;
using Windows.UI;

namespace M365Hub.App;

/// <summary>UI-thread text measurement with Cambria Math.</summary>
public sealed class XamlMeasurer : ITextMeasurer
{
    private readonly TextBlock _tb = new()
    {
        FontFamily = new FontFamily("Cambria Math"),
        TextWrapping = TextWrapping.NoWrap,
    };

    public float MeasureText(string text, float sizePx, int style)
    {
        _tb.Text = text;
        _tb.FontSize = sizePx;
        _tb.FontStyle = style == TeXRenderer.StyleItalic ? FontStyle.Italic : FontStyle.Normal;
        _tb.FontWeight = style == TeXRenderer.StyleBold ? FontWeights.Bold : FontWeights.Normal;
        _tb.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        return (float)_tb.DesiredSize.Width;
    }

    public (float Above, float Below) FontMetrics(float sizePx, int style)
        => (sizePx * 0.8f, sizePx * 0.2f);

    // Cambria Math covers the renderer's symbol set (Greek, long
    // arrows, delimiters), so no ASCII fallback is needed here.
    public bool HasGlyph(string text, float sizePx) => true;
}

/// <summary>ICanvas that builds WinUI shapes inside a Canvas panel.</summary>
public sealed class XamlCanvas : ICanvas
{
    private readonly Canvas _panel;
    private readonly Stack<(float Tx, float Ty, float Sx, float Sy)> _stack = new();
    private float _tx, _ty, _sx = 1, _sy = 1;

    public XamlCanvas(Canvas panel) => _panel = panel;

    private static Color ToColor(uint c) => Color.FromArgb(
        (byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c);

    public void DrawText(string text, float x, float y, float sizePx, int style, uint color)
    {
        float effSize = sizePx * _sy;
        float px = x * _sx + _tx;
        float py = y * _sy + _ty;
        var tb = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Cambria Math"),
            FontSize = effSize,
            FontStyle = style == TeXRenderer.StyleItalic ? FontStyle.Italic : FontStyle.Normal,
            FontWeight = style == TeXRenderer.StyleBold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = new SolidColorBrush(ToColor(color)),
        };
        Canvas.SetLeft(tb, px);
        Canvas.SetTop(tb, py - effSize * 0.8f);
        _panel.Children.Add(tb);
    }

    public void DrawLine(float x1, float y1, float x2, float y2, float strokeWidth, uint color)
    {
        float w = strokeWidth * ((_sx + _sy) / 2);
        var line = new Line
        {
            X1 = x1 * _sx + _tx,
            Y1 = y1 * _sy + _ty,
            X2 = x2 * _sx + _tx,
            Y2 = y2 * _sy + _ty,
            Stroke = new SolidColorBrush(ToColor(color)),
            StrokeThickness = w,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        _panel.Children.Add(line);
    }

    public void FillRect(float x, float y, float w, float h, uint color)
    {
        var r = new Rectangle
        {
            Width = w * _sx,
            Height = h * _sy,
            Fill = new SolidColorBrush(ToColor(color)),
        };
        Canvas.SetLeft(r, x * _sx + _tx);
        Canvas.SetTop(r, y * _sy + _ty);
        _panel.Children.Add(r);
    }

    public void DrawRectStroke(float x, float y, float w, float h, float strokeWidth, uint color)
    {
        var r = new Rectangle
        {
            Width = w * _sx,
            Height = h * _sy,
            Fill = new SolidColorBrush(Colors.Transparent),
            Stroke = new SolidColorBrush(ToColor(color)),
            StrokeThickness = strokeWidth * ((_sx + _sy) / 2),
        };
        Canvas.SetLeft(r, x * _sx + _tx);
        Canvas.SetTop(r, y * _sy + _ty);
        _panel.Children.Add(r);
    }

    public void Save() => _stack.Push((_tx, _ty, _sx, _sy));
    public void Restore()
    {
        if (_stack.Count > 0) (_tx, _ty, _sx, _sy) = _stack.Pop();
    }
    public void Translate(float x, float y) { _tx += _sx * x; _ty += _sy * y; }
    public void Scale(float sx, float sy) { _sx *= sx; _sy *= sy; }
}

/// <summary>A Canvas that displays one TeX formula. Falls back to the
/// raw LaTeX text when the formula is outside the supported subset.</summary>
public sealed class TeXControl : Canvas
{
    private static readonly XamlMeasurer Measurer = new();

    public bool SetFormula(string tex, float fontSize, uint color, bool display)
    {
        Children.Clear();
        var layout = TeXRenderer.Layout(tex, fontSize, color, display, Measurer);
        if (layout == null)
        {
            Children.Add(new TextBlock
            {
                Text = tex,
                FontFamily = new FontFamily("Consolas"),
                FontSize = fontSize,
                Foreground = new SolidColorBrush(Color.FromArgb(
                    (byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color)),
                TextWrapping = TextWrapping.Wrap,
            });
            Width = double.NaN;
            Height = double.NaN;
            return false;
        }
        Width = layout.Width;
        Height = layout.Height;
        layout.Draw(new XamlCanvas(this));
        return true;
    }
}
