// Builds the visible content of one chat message: for the assistant,
// MathSegments + Markdown + inline/display TeX (the same segmentation
// the Android native app uses, including the 1.2.6 run-merge so
// **bold** can span across an inline formula); for the user, images
// on top and text below. Images load asynchronously and fill in.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using M365Hub.Core;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;
using Windows.UI;

namespace M365Hub.App;

public static class MessageView
{
    private const char Placeholder = '\uFFFC';

    /// <summary>Build the inner content of a message bubble.</summary>
    public static StackPanel Build(ChatStore.Msg msg, AppState state, uint textColor, double maxWidth)
    {
        var panel = new StackPanel { Spacing = 6 };
        if (msg.Role == "user")
        {
            foreach (string url in msg.AttImages)
            {
                var img = new Image { Stretch = Stretch.Uniform, MaxHeight = 320, MaxWidth = maxWidth };
                panel.Children.Add(img);
                _ = LoadImageAsync(img, url, state);
            }
            if (msg.Content.Length > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = msg.Content,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 15,
                    Foreground = new SolidColorBrush(ToColor(textColor)),
                });
            }
        }
        else
        {
            foreach (var el in BuildAssistant(msg.Content, state, textColor, maxWidth))
                panel.Children.Add(el);
            foreach (string url in msg.Images)
            {
                var img = new Image { Stretch = Stretch.Uniform, MaxHeight = 320, MaxWidth = maxWidth };
                panel.Children.Add(img);
                _ = LoadImageAsync(img, url, state);
            }
        }
        return panel;
    }

    private static List<UIElement> BuildAssistant(string content, AppState state, uint textColor, double maxWidth)
    {
        var outp = new List<UIElement>();
        // One paragraph run = text segments + inline math merged first,
        // so **bold** can open before a formula and close after it.
        var runText = new System.Text.StringBuilder();
        var runFormulas = new List<string>();
        void FlushRun()
        {
            if (runText.Length == 0) return;
            var rtb = BuildRichText(runText.ToString(), runFormulas, textColor);
            if (rtb != null) outp.Add(rtb);
            runText.Clear();
            runFormulas.Clear();
        }
        foreach (var seg in MathSegments.Parse(content))
        {
            if (seg.Kind == MathSegments.DisplayMath)
            {
                FlushRun();
                var tc = new TeXControl { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = maxWidth };
                tc.SetFormula(seg.Content, 18, textColor, true);
                outp.Add(tc);
            }
            else if (seg.Kind == MathSegments.Code)
            {
                FlushRun();
                var code = new TextBlock
                {
                    Text = seg.Content,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                };
                outp.Add(Glass.Card(code, ThemeOf(textColor), 10));
            }
            else if (seg.Kind == MathSegments.InlineMath)
            {
                runText.Append(Placeholder);
                runFormulas.Add(seg.Content);
            }
            else
            {
                runText.Append(seg.Content);
            }
        }
        FlushRun();
        return outp;
    }

    /// <summary>One merged run (text + inline formulas) as a RichTextBlock.</summary>
    private static RichTextBlock? BuildRichText(string text, List<string> formulas, uint textColor)
    {
        var parsed = Markdown.Parse(text);
        var rtb = new RichTextBlock
        {
            FontSize = 15,
            Foreground = new SolidColorBrush(ToColor(textColor)),
            TextWrapping = TextWrapping.Wrap,
        };
        int formulaIdx = 0;
        int pos = 0;
        foreach (string line in parsed.Text.Split('\n'))
        {
            var para = new Paragraph();
            rtb.Blocks.Add(para);
            int lineStart = pos;
            int lineEnd = pos + line.Length;
            // Formatting is constant between span edges inside this line.
            var edges = new SortedSet<int> { lineStart, lineEnd };
            foreach (var s in parsed.Spans)
            {
                if (s.Start > lineStart && s.Start < lineEnd) edges.Add(s.Start);
                if (s.End > lineStart && s.End < lineEnd) edges.Add(s.End);
            }
            var edgeList = edges.ToList();
            for (int ei = 0; ei + 1 < edgeList.Count; ei++)
            {
                int a = edgeList[ei], b = edgeList[ei + 1];
                if (a >= b) continue;
                string chunk = parsed.Text.Substring(a, b - a);
                AppendChunk(para, chunk, parsed.Spans, a, textColor, ref formulaIdx, formulas);
            }
            pos = lineEnd + 1; // skip '\n'
        }
        return rtb.Blocks.Count == 0 ? null : rtb;
    }

    private static void AppendChunk(Paragraph para, string chunk, List<Markdown.Span> spans,
        int chunkStart, uint textColor, ref int formulaIdx, List<string> formulas)
    {
        // Split the chunk further at inline-math placeholders.
        int i = 0;
        while (i < chunk.Length)
        {
            int ph = chunk.IndexOf(Placeholder, i);
            string plain = ph < 0 ? chunk.Substring(i) : chunk.Substring(i, ph - i);
            if (plain.Length > 0)
            {
                var run = new Run { Text = plain };
                ApplySpans(run, spans, chunkStart + i, textColor);
                para.Inlines.Add(run);
            }
            if (ph < 0) break;
            if (formulaIdx < formulas.Count)
            {
                var tc = new TeXControl();
                tc.SetFormula(formulas[formulaIdx], 15, textColor, false);
                formulaIdx++;
                var container = new InlineUIContainer { Child = tc };
                para.Inlines.Add(container);
            }
            i = ph + 1;
        }
    }

    private static void ApplySpans(Run run, List<Markdown.Span> spans, int pos, uint textColor)
    {
        double size = 15;
        bool bold = false, italic = false, code = false, gray = false;
        foreach (var s in spans)
        {
            if (pos < s.Start || pos >= s.End) continue;
            switch (s.Kind)
            {
                case Markdown.Bold: bold = true; break;
                case Markdown.Italic: italic = true; break;
                case Markdown.Code: code = true; break;
                case Markdown.H1: size = 24; bold = true; break;
                case Markdown.H2: size = 20; bold = true; break;
                case Markdown.H3: size = 17; bold = true; break;
                case Markdown.Hr: gray = true; break;
            }
        }
        run.FontSize = size;
        if (bold) run.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        if (italic) run.FontStyle = Windows.UI.Text.FontStyle.Italic;
        if (code) run.FontFamily = new FontFamily("Consolas");
        if (gray) run.Foreground = new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));
    }

    private static async Task LoadImageAsync(Image img, string url, AppState state)
    {
        byte[]? bytes = await state.Api.FetchImageBytesAsync(url).ConfigureAwait(false);
        if (bytes == null || bytes.Length == 0) return;
        try
        {
            var bmp = new BitmapImage();
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer()).AsTask().ConfigureAwait(false);
            stream.Seek(0);
            await bmp.SetSourceAsync(stream).AsTask().ConfigureAwait(false);
            img.DispatcherQueue.TryEnqueue(() => { img.Source = bmp; });
        }
        catch { }
    }

    private static Color ToColor(uint c) => Color.FromArgb(
        (byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c);

    private static ElementTheme ThemeOf(uint textColor)
        => textColor == 0xFFFFFFFF ? ElementTheme.Dark : ElementTheme.Light;
}
