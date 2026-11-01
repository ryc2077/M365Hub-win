// Splits an assistant message into text / math / code segments, the
// same segmentation the old web console used and the Android native
// MathContent.java uses (fenced ```latex blocks are math, other
// fenced blocks are literal code, \[...\] and $$...$$ are display
// math, \(...\) and $...$ are inline math with the same "looks like
// LaTeX" guard so "$20" stays text). No WinUI types.
namespace M365Hub.Core;

public static class MathSegments
{
    public const int Text = 0;
    public const int InlineMath = 1;
    public const int DisplayMath = 2;
    public const int Code = 3;

    public sealed class Seg
    {
        public int Kind;
        public string Content;
        public Seg(int kind, string content) { Kind = kind; Content = content; }
    }

    // Environments the model sometimes emits bare, without \[...\] or
    // $$...$$ around them; treat them as display math too.
    private static readonly string[] MathEnvs = {
        "aligned", "aligned*", "gathered", "gathered*", "cases", "array"
    };

    // Finds a bare \begin{env}...\end{env} at or after from; returns
    // {begin, innerStart, innerEnd, afterEnd} or null.
    private static int[]? BeginEnv(string s, int from)
    {
        int b = s.IndexOf("\\begin{", from, StringComparison.Ordinal);
        if (b < 0) return null;
        int nameEnd = s.IndexOf('}', b + 7);
        if (nameEnd < 0) return null;
        string env = s.Substring(b + 7, nameEnd - (b + 7));
        bool ok = false;
        foreach (string e in MathEnvs)
        {
            if (e == env) { ok = true; break; }
        }
        if (!ok) return null;
        string endMark = "\\end{" + env + "}";
        int end = s.IndexOf(endMark, nameEnd + 1, StringComparison.Ordinal);
        if (end < 0) return null;
        return new int[] { b, nameEnd + 1, end, end + endMark.Length };
    }

    public static List<Seg> Parse(string content)
    {
        var outp = new List<Seg>();
        int i = 0;
        int n = content.Length;
        while (i < n)
        {
            int fence = content.IndexOf("```", i, StringComparison.Ordinal);
            int disp1 = content.IndexOf("\\[", i, StringComparison.Ordinal);
            int disp2 = content.IndexOf("$$", i, StringComparison.Ordinal);
            int inl1 = content.IndexOf("\\(", i, StringComparison.Ordinal);
            int[]? be = BeginEnv(content, i);
            int beg = be == null ? -1 : be[0];
            int next = n;
            int[] cands = { fence, disp1, disp2, inl1, beg };
            foreach (int c in cands)
            {
                if (c >= 0 && c < next) next = c;
            }
            // Inline $...$ needs its own scan (guard against $20 etc).
            int dollar = ScanDollar(content, i);
            if (dollar >= 0 && dollar < next)
            {
                next = dollar;
            }
            if (next == n)
            {
                outp.Add(new Seg(Text, content.Substring(i)));
                break;
            }
            if (next > i) outp.Add(new Seg(Text, content.Substring(i, next - i)));
            if (next == beg && be != null)
            {
                outp.Add(new Seg(DisplayMath, content.Substring(be[1], be[2] - be[1]).Trim()));
                i = be[3];
            }
            else if (next == fence)
            {
                int nl = content.IndexOf('\n', fence + 3);
                int end = content.IndexOf("```", nl < 0 ? fence + 3 : nl + 1, StringComparison.Ordinal);
                if (end < 0)
                {
                    outp.Add(new Seg(Text, content.Substring(fence)));
                    break;
                }
                string lang = nl < 0 ? "" : content.Substring(fence + 3, nl - (fence + 3)).Trim().ToLowerInvariant();
                string inner = nl < 0 ? "" : content.Substring(nl + 1, end - (nl + 1));
                if (lang == "latex" || lang == "math" || lang == "tex")
                {
                    outp.Add(new Seg(DisplayMath, inner.Trim()));
                }
                else
                {
                    outp.Add(new Seg(Code, inner));
                }
                i = end + 3;
            }
            else if (next == disp1)
            {
                int end = content.IndexOf("\\]", disp1 + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    outp.Add(new Seg(Text, content.Substring(disp1)));
                    break;
                }
                outp.Add(new Seg(DisplayMath, content.Substring(disp1 + 2, end - (disp1 + 2)).Trim()));
                i = end + 2;
            }
            else if (next == disp2)
            {
                int end = content.IndexOf("$$", disp2 + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    outp.Add(new Seg(Text, content.Substring(disp2)));
                    break;
                }
                outp.Add(new Seg(DisplayMath, content.Substring(disp2 + 2, end - (disp2 + 2)).Trim()));
                i = end + 2;
            }
            else if (next == inl1)
            {
                int end = content.IndexOf("\\)", inl1 + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    outp.Add(new Seg(Text, content.Substring(inl1)));
                    break;
                }
                outp.Add(new Seg(InlineMath, content.Substring(inl1 + 2, end - (inl1 + 2)).Trim()));
                i = end + 2;
            }
            else
            {
                int end = content.IndexOf('$', dollar + 1);
                outp.Add(new Seg(InlineMath, content.Substring(dollar + 1, end - (dollar + 1)).Trim()));
                i = end + 1;
            }
        }
        return outp;
    }

    private static int ScanDollar(string s, int from)
    {
        for (int i = from; i < s.Length; i++)
        {
            if (s[i] != '$') continue;
            if (i > 0 && s[i - 1] == '\\') continue;
            if (i + 1 < s.Length && s[i + 1] == '$') continue;
            int end = s.IndexOf('$', i + 1);
            if (end < 0) return -1;
            string inner = s.Substring(i + 1, end - (i + 1));
            if (inner.Length > 0 && inner.Length < 500
                    && (inner.Contains('\\') || inner.Contains('^') || inner.Contains('_') || inner.Contains('{')))
            {
                return i;
            }
            // The closing $ may itself open a real span ("$20 and $x^2$").
            i = end - 1;
        }
        return -1;
    }
}
