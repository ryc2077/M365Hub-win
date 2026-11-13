// Host test console for M365Hub.Core. Ports the Android host tests
// (md-test/TestMd.java, tex-test/TestTex.java) 1:1 so the WinUI Core
// library proves the same behaviour: Markdown spans, TeXSplit rows /
// cells, MathSegments segmentation, TeX layout geometry, and the
// radical as one connected polyline (not a glyph plus a floating bar).
using M365Hub.Core;
using System.Net.Sockets;

int fails = 0;
void Check(string name, bool cond, string? extra = null)
{
    Console.WriteLine((cond ? "PASS " : "FAIL ") + name + (extra == null ? "" : " :: " + extra));
    if (!cond) fails++;
}
bool Has(Markdown.Parsed p, int kind, string text)
{
    foreach (var s in p.Spans)
    {
        if (s.Kind == kind && p.Text.Substring(s.Start, s.End - s.Start) == text) return true;
    }
    return false;
}

var measurer = new StubMeasurer();
var canvas = new RecCanvas();

TeXRenderer.LayoutResult? Render(string name, string tex, bool display)
{
    var r = TeXRenderer.Layout(tex, 30, 0xFF000000, display, measurer);
    bool ok = r != null && r.Width > 0 && r.Height > 0 && r.Ascent > 0;
    Check(name, ok, r == null ? "null" : $"{r.Width}x{r.Height} asc={r.Ascent}");
    return r;
}

// ================= Markdown (from TestMd.java) =================
Markdown.Parsed p;
p = Markdown.Parse("**中国联通**");
Check("bold text", p.Text == "中国联通", p.Text);
Check("bold span", Has(p, Markdown.Bold, "中国联通"), p.Spans.Count + "");
p = Markdown.Parse("- **29元/月**\n- 长期套餐");
Check("bullet text", p.Text == "•  29元/月\n•  长期套餐", p.Text.Replace("\n", "|"));
Check("bullet spans", Has(p, Markdown.Bullet, "•  29元/月") && Has(p, Markdown.Bullet, "•  长期套餐"), "");
Check("bold in list", Has(p, Markdown.Bold, "29元/月"), "");
p = Markdown.Parse("## （1）求 C 的方程");
Check("h2 text", p.Text == "（1）求 C 的方程", p.Text);
Check("h2 span", Has(p, Markdown.H2, "（1）求 C 的方程"), "");
p = Markdown.Parse("*重点* 和 `x+y`");
Check("italic+code text", p.Text == "重点 和 x+y", p.Text);
Check("italic span", Has(p, Markdown.Italic, "重点"), "");
Check("code span", Has(p, Markdown.Code, "x+y"), "");
p = Markdown.Parse("x**2 和 a*b*c，还有 $20");
Check("x**2 literal", p.Text == "x**2 和 a*b*c，还有 $20", p.Text);
Check("no spans for literals", p.Spans.Count == 0, p.Spans.Count + "");
p = Markdown.Parse("---\n1. 第一步\n2. 第二步");
Check("hr line", p.Text.StartsWith("────────────────────────\n"), p.Text.Replace("\n", "|"));
Check("hr span", Has(p, Markdown.Hr, "────────────────────────"), "");
Check("ordered spans", Has(p, Markdown.Ordered, "1. 第一步") && Has(p, Markdown.Ordered, "2. 第二步"), p.Text.Replace("\n", "|"));
p = Markdown.Parse("**M(1, √3)**，并求");
Check("bold parens", p.Text == "M(1, √3)，并求" && Has(p, Markdown.Bold, "M(1, √3)"), p.Text);
p = Markdown.Parse("**a *b* c**");
Check("bold has italic inside", p.Text == "a b c" && Has(p, Markdown.Bold, "a b c") && Has(p, Markdown.Italic, "b"), p.Text);
p = Markdown.Parse("**方法总结：**中点问题先“反求端点”");
Check("bold before CJK", p.Text == "方法总结：中点问题先“反求端点”" && Has(p, Markdown.Bold, "方法总结："), p.Text);
p = Markdown.Parse("*重点*内容");
Check("italic before CJK", p.Text == "重点内容" && Has(p, Markdown.Italic, "重点"), p.Text);
p = Markdown.Parse("** a** 和 **b **");
Check("spaced bold literal", p.Text == "** a** 和 **b **" && p.Spans.Count == 0, p.Text);
p = Markdown.Parse("**令 \uFFFC，就有**");
Check("bold across math", p.Text == "令 \uFFFC，就有" && Has(p, Markdown.Bold, "令 \uFFFC，就有"), p.Text);
p = Markdown.Parse("*见 \uFFFC 式*");
Check("italic across math", p.Text == "见 \uFFFC 式" && Has(p, Markdown.Italic, "见 \uFFFC 式"), p.Text);
p = Markdown.Parse("**x** \uFFFC **y**");
Check("two bolds around math", Has(p, Markdown.Bold, "x") && Has(p, Markdown.Bold, "y"), p.Text);

// TeXSplit rows/cells
var rows = TeXSplit.SplitRows("x &= 1 \\\\ y &= 2");
Check("aligned rows", rows.Count == 2 && rows[0] == "x &= 1" && rows[1] == "y &= 2", string.Join("|", rows));
var cells = TeXSplit.SplitCells("x &= 1");
Check("aligned cells", cells.Count == 2 && cells[0] == "x" && cells[1] == "= 1", string.Join("|", cells));
rows = TeXSplit.SplitRows("a & b \\\\[4pt] c & d");
Check("row gap skipped", rows.Count == 2 && rows[1] == "c & d", string.Join("|", rows));
cells = TeXSplit.SplitCells("\\text{a\\&b} & c");
Check("escaped amp", cells.Count == 2 && cells[0] == "\\text{a\\&b}" && cells[1] == "c", string.Join("|", cells));
rows = TeXSplit.SplitRows("x^2");
Check("single row", rows.Count == 1 && rows[0] == "x^2", string.Join("|", rows));

// ================= TeX layout (from TestTex.java) =================
Render("aligned 2 rows", "\\begin{aligned} |PA|^2+|PQ|^2 &= 2\\lambda \\\\ x+y &= 4 \\end{aligned}", true);
Render("aligned with frac", "\\begin{aligned} x &= \\frac{1}{2} \\\\ y &= \\sqrt{3} \\end{aligned}", true);
Render("gathered", "\\begin{gathered} x^2+y^2=1 \\\\ y=kx+b \\end{gathered}", true);
Render("cases", "f(x)=\\begin{cases} 1, & x>0 \\\\ 0, & x=0 \\end{cases}", true);
Render("array", "\\begin{array}{cc} 1 & 2 \\\\ 3 & 4 \\end{array}", true);
var segs = MathSegments.Parse("where \\(\\begin{aligned} a &= 1 \\\\ b &= 2 \\end{aligned}\\) holds");
var ia = segs.Count == 3 && segs[1].Kind == MathSegments.InlineMath
    ? TeXRenderer.Layout(segs[1].Content, 30, 0xFF000000, false, measurer) : null;
Check("inline aligned renders", ia != null && ia.Width > 0, ia == null ? "null" : ia.Width + "x" + ia.Height);
Render("aligned row gap", "\\begin{aligned} x &= 1 \\\\[4pt] y &= 2 \\end{aligned}", true);
Render("plain still works", "x^2+\\frac{1}{2}", false);
Render("boxed still works", "\\boxed{\\lambda=4}", true);
var one = TeXRenderer.Layout("x=1", 30, 0xFF000000, true, measurer);
var two = TeXRenderer.Layout("\\begin{aligned} x &= 1 \\\\ y &= 2 \\end{aligned}", 30, 0xFF000000, true, measurer);
Check("aligned taller than single", two != null && one != null && two.Height > one.Height * 1.5,
    one == null || two == null ? "null" : one.Height + " vs " + two.Height);
segs = MathSegments.Parse("解得 \\begin{aligned} x &= 1 \\\\ y &= 2 \\end{aligned} 所以");
Check("bare env segmented", segs.Count == 3 && segs[1].Kind == MathSegments.DisplayMath, segs.Count + " segs, kind1=" + (segs.Count > 1 ? segs[1].Kind : -1));
segs = MathSegments.Parse("看公式 \\[\\begin{aligned} x &= 1 \\end{aligned}\\] 结束");
Check("delimited env segmented", segs.Count == 3 && segs[1].Kind == MathSegments.DisplayMath, segs.Count + "");
segs = MathSegments.Parse("**加粗** 与 $x^2$ 混合");
Check("md+math segments", segs.Count == 3 && segs[1].Kind == MathSegments.InlineMath, segs.Count + "");
Render("Longrightarrow display", "1+\\frac{4k}{b}=3 \\quad\\Longrightarrow\\quad b=2k.", true);
Render("Longrightarrow inline", "1+\\frac{4k}{b}=3 \\quad\\Longrightarrow\\quad b=2k.", false);
Render("arrows", "x \\to y, a \\Rightarrow b, A \\iff B, f: X \\mapsto Y, a \\longleftarrow b", false);
Render("floor/angle delims", "\\left\\lfloor x \\right\\rfloor + \\left\\langle a, b \\right\\rangle", false);
Render("backslash space", "a\\ b", false);
segs = MathSegments.Parse("由题设 \\(k_1k_2=3\\), 得 \\(1+\\frac{4k}{b}=3\n\\quad\\Longrightarrow\\quad b=2k.\\)");
TeXRenderer.LayoutResult? ml = null;
foreach (var sg in segs)
{
    if (sg.Kind == MathSegments.InlineMath && sg.Content.Contains("Longrightarrow"))
        ml = TeXRenderer.Layout(sg.Content, 30, 0xFF000000, false, measurer);
}
Check("multiline inline renders", ml != null && ml.Width > 0, ml == null ? "null" : ml.Width + "x" + ml.Height);
segs = MathSegments.Parse("由题设 $k_1k_2=3$，得\n\\[1+\\frac{4k}{b}=3 \\quad\\Longrightarrow\\quad b=2k.\\]");
bool leak = false;
foreach (var sg in segs) if (sg.Kind == MathSegments.Text && sg.Content.Contains("Longrightarrow")) leak = true;
Check("no raw Longrightarrow in text", !leak, segs.Count + " segs");

// Radical: one connected polyline, not a glyph plus a floating bar.
void CheckSqrt(string name, string tex)
{
    canvas.Clear();
    var r = TeXRenderer.Layout(tex, 30, 0xFF000000, false, measurer);
    r?.Draw(canvas);
    var ls = canvas.Lines;
    bool ok = r != null && ls.Count == 3;
    for (int i = 0; ok && i + 1 < ls.Count; i++)
    {
        float[] a = ls[i], b = ls[i + 1];
        ok = Math.Abs(a[2] - b[0]) < 0.6f && Math.Abs(a[3] - b[1]) < 0.6f;
    }
    float[]? bar = ok ? ls[2] : null;
    ok = ok && bar != null && Math.Abs(bar[1] - bar[3]) < 0.6f; // bar is horizontal
    foreach (string t in canvas.Texts) if (t.Contains("\u221A")) ok = false;
    ok = ok && canvas.Rects.Count == 0;
    int ti = canvas.Texts.IndexOf("3");
    if (ok && ti >= 0 && tex.Contains("{3}"))
    {
        float tx = canvas.TextXY[ti][0];
        float ty = canvas.TextXY[ti][1];
        ok = bar![0] < tx && bar[2] > tx && bar[1] < ty;
    }
    Check("sqrt connected :: " + name, ok,
        r == null ? "null" : "lines=" + ls.Count + " rects=" + canvas.Rects.Count);
}
CheckSqrt("sqrt 3", "\\sqrt{3}");
CheckSqrt("sqrt in expr", "(y-\\sqrt{3})^2");
CheckSqrt("sqrt with degree", "\\sqrt[3]{x}");

// ================= Gateway probe (1.0.4 regression: the HttpClient
// probe mistook a proxy-stalled first request for a hung gateway) =================
int FreePort()
{
    var l = new TcpListener(System.Net.IPAddress.Loopback, 0);
    l.Start();
    int p = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
    l.Stop();
    return p;
}
Check("probe none (nothing listening)",
    GatewayProbe.Probe("127.0.0.1", FreePort(), TimeSpan.FromSeconds(2)) == GatewayProbeState.None);

// Hung: accept the connection and never send anything.
{
    var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    var accepted = listener.AcceptTcpClientAsync();
    var state = GatewayProbe.Probe("127.0.0.1", port, TimeSpan.FromSeconds(2));
    Check("probe hung (accepts, never answers)", state == GatewayProbeState.Hung, state + "");
    accepted.Wait(TimeSpan.FromSeconds(3));
    if (accepted.IsCompletedSuccessfully) accepted.Result.Close();
    listener.Stop();
}

// Live: answer a minimal HTTP 200.
{
    var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    var serve = Task.Run(async () =>
    {
        using var c = await listener.AcceptTcpClientAsync();
        using var s = c.GetStream();
        var buf = new byte[4096];
        try { await s.ReadAsync(buf, 0, buf.Length); } catch { }
        byte[] resp = System.Text.Encoding.ASCII.GetBytes("HTTP/1.0 200 OK\r\nContent-Length: 2\r\n\r\nok");
        await s.WriteAsync(resp, 0, resp.Length);
    });
    var state = GatewayProbe.Probe("127.0.0.1", port, TimeSpan.FromSeconds(2));
    Check("probe live (answers HTTP)", state == GatewayProbeState.Live, state + "");
    serve.Wait(TimeSpan.FromSeconds(3));
    listener.Stop();
}

Console.WriteLine(fails == 0 ? "ALL OK" : fails + " FAILED");
return fails == 0 ? 0 : 1;

// ---------- stub measurer: fixed-width, no missing glyphs ----------
sealed class StubMeasurer : ITextMeasurer
{
    public float MeasureText(string text, float sizePx, int style) => text.Length * sizePx * 0.6f;
    public (float Above, float Below) FontMetrics(float sizePx, int style) => (sizePx * 0.8f, sizePx * 0.2f);
    public bool HasGlyph(string text, float sizePx) => true;
}

// ---------- recording canvas: tracks the transform stack ----------
sealed class RecCanvas : ICanvas
{
    public List<float[]> Lines = new();
    public List<string> Texts = new();
    public List<float[]> TextXY = new();
    public List<float[]> Rects = new();
    private readonly Stack<(float Tx, float Ty, float Sx, float Sy)> _stack = new();
    private float _tx, _ty = 0, _sx = 1, _sy = 1;

    public void Clear() { Lines.Clear(); Texts.Clear(); TextXY.Clear(); Rects.Clear(); _stack.Clear(); _tx = _ty = 0; _sx = _sy = 1; }
    private (float, float) Xf(float x, float y) => (x * _sx + _tx, y * _sy + _ty);

    public void DrawText(string text, float x, float y, float sizePx, int style, uint color)
    {
        var (px, py) = Xf(x, y);
        Texts.Add(text);
        TextXY.Add(new[] { px, py });
    }
    public void DrawLine(float x1, float y1, float x2, float y2, float strokeWidth, uint color)
    {
        var (ax, ay) = Xf(x1, y1);
        var (bx, by) = Xf(x2, y2);
        Lines.Add(new[] { ax, ay, bx, by });
    }
    public void FillRect(float x, float y, float w, float h, uint color)
    {
        var (px, py) = Xf(x, y);
        Rects.Add(new[] { px, py, w * _sx, h * _sy });
    }
    public void DrawRectStroke(float x, float y, float w, float h, float strokeWidth, uint color)
    {
        var (px, py) = Xf(x, y);
        Rects.Add(new[] { px, py, w * _sx, h * _sy });
    }
    public void Save() => _stack.Push((_tx, _ty, _sx, _sy));
    public void Restore()
    {
        if (_stack.Count > 0) (_tx, _ty, _sx, _sy) = _stack.Pop();
    }
    public void Translate(float x, float y) { _tx += _sx * x; _ty += _sy * y; }
    public void Scale(float sx, float sy) { _sx *= sx; _sy *= sy; }
}

