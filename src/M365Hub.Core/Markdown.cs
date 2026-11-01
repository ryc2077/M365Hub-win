// Minimal Markdown for assistant messages (parity with the web
// console's renderRich and the Android native Markdown.java):
// headings, bullet/numbered lists, horizontal rules, bold, italic,
// inline code. Pure string work, no WinUI types, so it is
// unit-testable on any host; the UI layer maps the spans to
// WinUI Runs. Math and fenced code blocks are segmented out before
// this runs, so markers inside them are never touched here.
//
// Deliberately small: no tables, no links, no nested bold-in-italic.
// An opening marker only counts when the character before it is not a
// letter/digit and the content does not start with a space, so "x**2"
// and "a*b*c" stay literal. A closing marker only counts when the
// content does not end with a space; the character after it may be
// anything, so "**方法总结：**中点问题" renders bold (Chinese text
// has no space after the marker, and '中' counts as a letter).
namespace M365Hub.Core;

public static class Markdown
{
    public const int Bold = 1;
    public const int Italic = 2;
    public const int Code = 3;
    public const int H1 = 4;
    public const int H2 = 5;
    public const int H3 = 6;
    public const int Bullet = 7;
    public const int Ordered = 8;
    public const int Hr = 9;

    public sealed class Span
    {
        public int Kind;
        public int Start;
        public int End;
        public int Number; // ordered-list number, else 0
        public Span(int kind, int start, int end) { Kind = kind; Start = start; End = end; }
    }

    public sealed class Parsed
    {
        public string Text = "";
        public List<Span> Spans = new();
    }

    public static Parsed Parse(string input)
    {
        var outp = new Parsed();
        var sb = new System.Text.StringBuilder();
        string[] lines = input.Split('\n');
        for (int li = 0; li < lines.Length; li++)
        {
            if (li > 0) sb.Append('\n');
            ParseLine(lines[li], sb, outp.Spans);
        }
        outp.Text = sb.ToString();
        return outp;
    }

    private static void ParseLine(string line, System.Text.StringBuilder sb, List<Span> spans)
    {
        string t = line.Trim();
        // Horizontal rule: a line of only -, * or _ (3+).
        if (t.Length >= 3)
        {
            char c = t[0];
            if (c == '-' || c == '*' || c == '_')
            {
                bool all = true;
                for (int i = 0; i < t.Length; i++)
                {
                    if (t[i] != c) { all = false; break; }
                }
                if (all)
                {
                    int start = sb.Length;
                    for (int i = 0; i < 24; i++) sb.Append('\u2500');
                    spans.Add(new Span(Hr, start, sb.Length));
                    return;
                }
            }
        }
        // Heading: up to 3 leading spaces, 1-3 #'s, a space, content.
        int p = 0;
        while (p < line.Length && line[p] == ' ' && p < 3) p++;
        int hashes = 0;
        while (p + hashes < line.Length && line[p + hashes] == '#') hashes++;
        if (hashes >= 1 && hashes <= 3 && p + hashes < line.Length
                && line[p + hashes] == ' ')
        {
            int contentStart = p + hashes + 1;
            int start = sb.Length;
            ScanInline(line.Substring(contentStart), sb, spans);
            int kind = hashes == 1 ? H1 : hashes == 2 ? H2 : H3;
            spans.Add(new Span(kind, start, sb.Length));
            return;
        }
        // List item: "- ", "* ", "+ " or "1. "/"1) ".
        string rest = line.Substring(p);
        if (rest.Length >= 2 && (rest[0] == '-' || rest[0] == '*'
                || rest[0] == '+') && rest[1] == ' ')
        {
            int start = sb.Length;
            sb.Append('\u2022');
            sb.Append("  ");
            ScanInline(rest.Substring(2), sb, spans);
            spans.Add(new Span(Bullet, start, sb.Length));
            return;
        }
        if (rest.Length >= 3 && char.IsDigit(rest[0]))
        {
            int j = 1;
            while (j < rest.Length && char.IsDigit(rest[j])) j++;
            if (j < rest.Length && (rest[j] == '.' || rest[j] == ')')
                    && j + 1 < rest.Length && rest[j + 1] == ' ')
            {
                int num = 0;
                int.TryParse(rest.Substring(0, j), out num);
                int start = sb.Length;
                sb.Append(rest.Substring(0, j + 1));
                sb.Append(' ');
                ScanInline(rest.Substring(j + 2), sb, spans);
                var sp = new Span(Ordered, start, sb.Length) { Number = num };
                spans.Add(sp);
                return;
            }
        }
        ScanInline(line, sb, spans);
    }

    /** Inline markers inside one line: `code`, **bold**, *italic*. */
    private static void ScanInline(string line, System.Text.StringBuilder sb, List<Span> spans)
        => ScanInline(line, sb, spans, true);

    private static void ScanInline(string line, System.Text.StringBuilder sb, List<Span> spans,
            bool allowBold)
    {
        int i = 0;
        int n = line.Length;
        while (i < n)
        {
            char c = line[i];
            if (c == '`')
            {
                int end = line.IndexOf('`', i + 1);
                if (end > i + 1)
                {
                    string inner = line.Substring(i + 1, end - (i + 1));
                    if (inner.IndexOf('`') < 0)
                    {
                        int start = sb.Length;
                        sb.Append(inner);
                        spans.Add(new Span(Code, start, sb.Length));
                        i = end + 1;
                        continue;
                    }
                }
                sb.Append(c);
                i++;
                continue;
            }
            if (allowBold && c == '*' && i + 1 < n && line[i + 1] == '*')
            {
                int end = line.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end > i + 2)
                {
                    string inner = line.Substring(i + 2, end - (i + 2));
                    if (!string.IsNullOrWhiteSpace(inner)
                            && !char.IsWhiteSpace(inner[0])
                            && !char.IsWhiteSpace(inner[inner.Length - 1])
                            && BoundaryBefore(line, i))
                    {
                        int start = sb.Length;
                        // Bold may contain italic/code inside it.
                        ScanInline(inner, sb, spans, false);
                        spans.Add(new Span(Bold, start, sb.Length));
                        i = end + 2;
                        continue;
                    }
                }
                sb.Append("**");
                i += 2;
                continue;
            }
            if (c == '*')
            {
                // Single *: not part of **, needs a matching * on this line.
                // (A ** pair reaches here only with allowBold=false, i.e.
                // inside bold content; consume the pair, never re-scan the
                // second star as an italic opener.)
                if (i + 1 < n && line[i + 1] == '*') { sb.Append("**"); i += 2; continue; }
                int end = -1;
                for (int j = i + 1; j < n; j++)
                {
                    if (line[j] != '*') continue;
                    if (j + 1 < n && line[j + 1] == '*') { j++; continue; }
                    if (j > 0 && line[j - 1] == '*') continue;
                    end = j;
                    break;
                }
                if (end > i + 1)
                {
                    string inner = line.Substring(i + 1, end - (i + 1));
                    if (!string.IsNullOrWhiteSpace(inner) && inner.IndexOf('*') < 0
                            && !char.IsWhiteSpace(inner[0])
                            && !char.IsWhiteSpace(inner[inner.Length - 1])
                            && BoundaryBefore(line, i))
                    {
                        int start = sb.Length;
                        sb.Append(inner);
                        spans.Add(new Span(Italic, start, sb.Length));
                        i = end + 1;
                        continue;
                    }
                }
                sb.Append(c);
                i++;
                continue;
            }
            sb.Append(c);
            i++;
        }
    }

    private static bool BoundaryBefore(string line, int pos)
    {
        if (pos <= 0) return true;
        char c = line[pos - 1];
        return !char.IsLetterOrDigit(c);
    }
}
