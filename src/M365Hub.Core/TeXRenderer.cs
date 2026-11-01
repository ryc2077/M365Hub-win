// Native TeX math renderer (no WebView, no KaTeX). Parses the LaTeX
// subset upstream actually returns for math questions and typesets
// it with a TeX-style box model: fractions with a rule, radicals
// with a one-stroke vinculum, scripts, big operators with limits,
// scaled delimiters, accents, boxed results. Anything outside the
// subset throws, and the caller falls back to showing the raw LaTeX
// text, so content is never lost. Direct port of Android
// TeXRenderer.java; drawing is abstracted behind ICanvas /
// ITextMeasurer so the WinUI layer can implement it with Win2D and
// the tests can use a recording stub.
//
// Supported: \frac \dfrac \tfrac \sqrt (with [n]) \sum \int \prod
// \lim \binom \boxed \text \mathrm \mathbf \mathit \mathbb
// \left \right delimiters, ^ _ scripts, \hat \bar \vec \tilde \dot,
// \quad \qquad \, \; \: \!, Greek letters, common operators
// (\pm \times \div \cdot \cdots \ldots \leq \geq \neq \approx
// \equiv \to \Rightarrow \infty \partial \nabla \in \cup \cap
// \emptyset \forall \exists \sin \cos \tan \log \ln \exp \min \max),
// and escaped \% \$ \& \# \_ \{ \}.
namespace M365Hub.Core;

/// <summary>Text measurement. Style: 0 normal, 1 bold, 2 italic.</summary>
public interface ITextMeasurer
{
    float MeasureText(string text, float sizePx, int style);
    /// <summary>(above, below): px above / below the baseline, both &gt;= 0.</summary>
    (float Above, float Below) FontMetrics(float sizePx, int style);
    bool HasGlyph(string text, float sizePx);
}

/// <summary>
/// Drawing surface. Coordinates are in the layout's px space; the
/// implementation scales to device pixels. DrawLine uses round caps
/// and joins (the radical's one-stroke check relies on it). DrawText
/// draws with the text baseline at y.
/// </summary>
public interface ICanvas
{
    void DrawText(string text, float x, float y, float sizePx, int style, uint color);
    void DrawLine(float x1, float y1, float x2, float y2, float strokeWidth, uint color);
    void FillRect(float x, float y, float w, float h, uint color);
    void DrawRectStroke(float x, float y, float w, float h, float strokeWidth, uint color);
    void Save();
    void Restore();
    void Translate(float x, float y);
    void Scale(float sx, float sy);
}

public static class TeXRenderer
{
    public const int StyleNormal = 0;
    public const int StyleBold = 1;
    public const int StyleItalic = 2;

    public sealed class LayoutResult
    {
        public float Width;
        public float Height;
        public float Ascent;  // px above the baseline inside the layout
        public float Descent; // px below the baseline inside the layout
        internal Box Root = null!;
        internal float Pad;

        public void Draw(ICanvas canvas) => Root.Draw(canvas, Pad, Pad + Root.H);
    }

    private const int CacheMax = 120;
    private static readonly Dictionary<string, LayoutResult> Cache = new();
    private static readonly object CacheLock = new();

    /// <summary>Layout only (no drawing). Returns null on parse failure.</summary>
    public static LayoutResult? Layout(string tex, float textSizePx, uint color, bool display,
        ITextMeasurer measurer)
    {
        string key = tex + "|" + (int)textSizePx + "|" + color + "|" + display;
        lock (CacheLock)
        {
            if (Cache.TryGetValue(key, out var hit)) return hit;
        }
        LayoutResult? outp = null;
        try
        {
            var p = new Parser(tex, textSizePx, color, display, measurer);
            Box? box = p.ParseExpr('\0');
            if (box == null) return null;
            float pad = Math.Max(2, textSizePx * 0.08f);
            float w = (float)Math.Ceiling(box.W + pad * 2);
            float h = (float)Math.Ceiling(box.H + box.D + pad * 2);
            if (w <= 0 || h <= 0 || w > 4096 || h > 4096) return null;
            outp = new LayoutResult
            {
                Width = w, Height = h,
                Ascent = pad + box.H, Descent = pad + box.D,
                Root = box, Pad = pad,
            };
            lock (CacheLock)
            {
                if (Cache.Count > CacheMax) Cache.Clear();
                Cache[key] = outp;
            }
        }
        catch
        {
            outp = null;
        }
        return outp;
    }

    private sealed class ParseException : Exception
    {
        public ParseException(string m) : base(m) { }
    }

    internal abstract class Box
    {
        public float W, H, D; // width, height above baseline, depth below baseline
        public abstract void Draw(ICanvas c, float x, float baseline);
    }

    private sealed class TextBox : Box
    {
        readonly string _text;
        readonly float _size;
        readonly int _style;
        readonly uint _color;

        internal string Text => _text;
        internal float Size => _size;
        internal uint Color => _color;

        public TextBox(string text, float size, int style, uint color, ITextMeasurer m)
        {
            _text = text; _size = size; _style = style; _color = color;
            var fm = m.FontMetrics(size, style);
            W = m.MeasureText(text, size, style);
            H = fm.Above;
            D = fm.Below;
        }

        public override void Draw(ICanvas c, float x, float baseline)
            => c.DrawText(_text, x, baseline, _size, _style, _color);
    }

    private sealed class SpaceBox : Box
    {
        public SpaceBox(float w) { W = w; H = 0; D = 0; }
        public override void Draw(ICanvas c, float x, float baseline) { }
    }

    private sealed class HBox : Box
    {
        public readonly List<Box> Kids = new();

        public void Add(Box? b)
        {
            if (b == null) return;
            Kids.Add(b);
            W += b.W;
            H = Math.Max(H, b.H);
            D = Math.Max(D, b.D);
        }

        public override void Draw(ICanvas c, float x, float baseline)
        {
            float cx = x;
            foreach (var b in Kids)
            {
                b.Draw(c, cx, baseline);
                cx += b.W;
            }
        }
    }

    // Multi-line environments (aligned / gathered / cases / array):
    // rows of cells, laid out with TeX aligned-style column alignment
    // (even columns right-aligned, odd columns left-aligned); gathered
    // centers every row. The baseline is the first row's baseline.
    private sealed class AlignBox : Box
    {
        readonly List<List<Box>> _rows;
        readonly float[] _colW;
        readonly float[] _rowBase; // baseline offset of each row from the box baseline
        readonly float _colGap;
        readonly bool _centered;

        public AlignBox(List<List<Box>> rows, float colGap, float rowGap, bool centered)
        {
            _rows = rows;
            _colGap = colGap;
            _centered = centered;
            int ncols = 0;
            foreach (var r in rows) ncols = Math.Max(ncols, r.Count);
            _colW = new float[ncols];
            foreach (var r in rows)
            {
                for (int j = 0; j < r.Count; j++) _colW[j] = Math.Max(_colW[j], r[j].W);
            }
            W = 0;
            for (int j = 0; j < ncols; j++)
            {
                W += _colW[j];
                if (j > 0) W += _colGap;
            }
            _rowBase = new float[rows.Count];
            H = rows.Count == 0 ? 0 : RowH(0);
            D = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (i > 0) _rowBase[i] = _rowBase[i - 1] + RowD(i - 1) + rowGap + RowH(i);
                D = Math.Max(D, _rowBase[i] + RowD(i));
            }
        }

        private float RowH(int i)
        {
            float m = 0;
            foreach (var b in _rows[i]) m = Math.Max(m, b.H);
            return m;
        }

        private float RowD(int i)
        {
            float m = 0;
            foreach (var b in _rows[i]) m = Math.Max(m, b.D);
            return m;
        }

        private float RowW(int i)
        {
            float w = 0;
            var r = _rows[i];
            for (int j = 0; j < r.Count; j++)
            {
                w += _colW[j];
                if (j > 0) w += _colGap;
            }
            return w;
        }

        public override void Draw(ICanvas c, float x, float baseline)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                var r = _rows[i];
                float rx = _centered ? x + (W - RowW(i)) / 2 : x;
                float cx = rx;
                for (int j = 0; j < r.Count; j++)
                {
                    var cell = r[j];
                    float cellX = cx;
                    if (!_centered && j % 2 == 0) cellX = cx + (_colW[j] - cell.W);
                    cell.Draw(c, cellX, baseline + _rowBase[i]);
                    cx += _colW[j] + _colGap;
                }
            }
        }
    }

    private sealed class FracBox : Box
    {
        readonly Box _num, _den;
        readonly bool _rule;
        readonly float _gap, _ruleThick, _axis;
        readonly uint _color;

        public FracBox(Box num, Box den, bool rule, float size, uint color)
        {
            _num = num; _den = den; _rule = rule; _color = color;
            _gap = size * 0.12f;
            _ruleThick = Math.Max(1.5f, size * 0.045f);
            _axis = size * 0.25f;
            float numBase = -_axis - _gap - num.D;
            float denBase = -_axis + _gap + den.H;
            H = -(numBase - num.H);
            D = denBase + den.D;
            W = Math.Max(num.W, den.W) + size * 0.2f;
        }

        public override void Draw(ICanvas c, float x, float baseline)
        {
            float barY = baseline - _axis;
            float numBase = barY - _gap - _num.D;
            float denBase = barY + _gap + _den.H;
            _num.Draw(c, x + (W - _num.W) / 2, numBase);
            _den.Draw(c, x + (W - _den.W) / 2, denBase);
            if (_rule)
            {
                c.FillRect(x + _ruleThick, barY - _ruleThick / 2,
                    W - _ruleThick * 2, _ruleThick, _color);
            }
        }
    }

    private sealed class SqrtBox : Box
    {
        readonly Box _inner;
        readonly Box? _degree;
        readonly float _signW, _size;
        readonly float _strokeWidth;
        readonly uint _color;

        public SqrtBox(Box inner, Box? degree, float size, uint color)
        {
            _inner = inner;
            _degree = degree;
            _size = size;
            _color = color;
            float totalH = inner.H + inner.D;
            _signW = size * 0.7f;
            H = totalH + size * 0.18f;
            D = size * 0.18f; // room for the check's vertex dip below the baseline
            W = _signW + inner.W + size * 0.12f;
            if (degree != null) W += degree.W;
            _strokeWidth = Math.Max(1.5f, size * 0.055f);
        }

        public override void Draw(ICanvas c, float x, float baseline)
        {
            // One polyline, like TeX: short arm down to the vertex, long
            // arm up to the bar, bar across the radicand. The old code
            // drew a "√" glyph plus a separate rect for the bar, and
            // the two never met: the bar floated above the check, starting
            // mid-glyph, which is the disconnected look in the screenshot.
            float barY = baseline - _inner.H - _size * 0.10f;
            float vx = x + _signW * 0.68f; // top of the long arm, start of the bar
            float barEnd = x + _signW + _inner.W + _size * 0.12f;
            float vx0 = x + _signW * 0.05f; // start of the short arm
            float vy0 = baseline - _inner.H * 0.35f;
            float px = x + _signW * 0.30f; // vertex
            float py = baseline + _size * 0.08f;
            c.DrawLine(vx0, vy0, px, py, _strokeWidth, _color);
            c.DrawLine(px, py, vx, barY, _strokeWidth, _color);
            c.DrawLine(vx, barY, barEnd, barY, _strokeWidth, _color);
            _inner.Draw(c, x + _signW, baseline);
            if (_degree != null)
            {
                _degree.Draw(c, x + _signW * 0.1f, baseline - _inner.H - _size * 0.1f);
            }
        }
    }

    private sealed class ScriptBox : Box
    {
        readonly Box _base;
        readonly Box? _sup, _sub;
        readonly float _supShift, _subShift;

        public ScriptBox(Box bas, Box? sup, Box? sub, float size)
        {
            _base = bas;
            _sup = sup;
            _sub = sub;
            _supShift = size * 0.45f;
            _subShift = size * 0.25f;
            float sw = 0;
            H = bas.H;
            D = bas.D;
            if (sup != null)
            {
                sw = Math.Max(sw, sup.W);
                H = Math.Max(H, _supShift + sup.H);
            }
            if (sub != null)
            {
                sw = Math.Max(sw, sub.W);
                D = Math.Max(D, _subShift + sub.D);
            }
            W = bas.W + sw;
        }

        public override void Draw(ICanvas c, float x, float baseline)
        {
            _base.Draw(c, x, baseline);
            if (_sup != null) _sup.Draw(c, x + _base.W, baseline - _supShift);
            if (_sub != null) _sub.Draw(c, x + _base.W, baseline + _subShift);
        }
    }

    private sealed class BigOpBox : Box
    {
        readonly Box _op;
        readonly Box? _sup, _sub;
        readonly bool _aboveBelow;

        public BigOpBox(Box op, Box? sup, Box? sub, bool aboveBelow)
        {
            _op = op; _sup = sup; _sub = sub; _aboveBelow = aboveBelow;
            if (aboveBelow)
            {
                W = Math.Max(op.W, Math.Max(sup == null ? 0 : sup.W, sub == null ? 0 : sub.W));
                H = op.H + (sup == null ? 0 : sup.H + sup.D + 2);
                D = op.D + (sub == null ? 0 : sub.H + sub.D + 2);
            }
            else
            {
                float sw = 0;
                H = op.H;
                D = op.D;
                if (sup != null) { sw = Math.Max(sw, sup.W); H = Math.Max(H, sup.H); }
                if (sub != null) { sw = Math.Max(sw, sub.W); D = Math.Max(D, sub.D); }
                W = op.W + sw;
            }
        }

        public override void Draw(ICanvas c, float x, float baseline)
        {
            if (_aboveBelow)
            {
                float cx = x + (W - _op.W) / 2;
                _op.Draw(c, cx, baseline);
                if (_sup != null) _sup.Draw(c, x + (W - _sup.W) / 2, baseline - _op.H - 2 - _sup.D);
                if (_sub != null) _sub.Draw(c, x + (W - _sub.W) / 2, baseline + _op.D + 2 + _sub.H);
            }
            else
            {
                _op.Draw(c, x, baseline);
                if (_sup != null) _sup.Draw(c, x + _op.W, baseline - _op.H * 0.5f);
                if (_sub != null) _sub.Draw(c, x + _op.W, baseline + _op.D * 0.5f + _sub.H * 0.5f);
            }
        }
    }

    private sealed class DelimBox : Box
    {
        readonly string? _left, _right;
        readonly Box _inner;
        readonly float _size;
        readonly uint _color;
        readonly ITextMeasurer _m;

        public DelimBox(string? left, string? right, Box inner, float size, uint color, ITextMeasurer m)
        {
            _left = left; _right = right; _inner = inner; _size = size; _color = color; _m = m;
            float glyphW = m.MeasureText(left ?? "", size, StyleNormal);
            float glyphW2 = m.MeasureText(right ?? "", size, StyleNormal);
            H = inner.H + size * 0.1f;
            D = inner.D + size * 0.1f;
            W = (_left == null ? 0 : glyphW * 0.7f) + inner.W + (_right == null ? 0 : glyphW2 * 0.7f);
        }

        private void DrawDelim(ICanvas c, string? g, float x, float baseline, float totalH)
        {
            if (g == null) return;
            var fm = _m.FontMetrics(_size, StyleNormal);
            float glyphH = fm.Above + fm.Below;
            float scaleY = Math.Max(1f, totalH / Math.Max(1, glyphH));
            c.Save();
            c.Translate(x, baseline);
            c.Scale(0.9f, scaleY);
            c.DrawText(g, 0, 0, _size, StyleNormal, _color);
            c.Restore();
        }

        public override void Draw(ICanvas c, float x, float baseline)
        {
            float totalH = H + D;
            float cx = x;
            if (_left != null)
            {
                DrawDelim(c, _left, cx, baseline, totalH);
                cx += _m.MeasureText(_left, _size, StyleNormal) * 0.7f;
            }
            _inner.Draw(c, cx, baseline);
            cx += _inner.W;
            if (_right != null) DrawDelim(c, _right, cx, baseline, totalH);
        }
    }

    private sealed class AccentBox : Box
    {
        readonly Box _base;
        readonly string _accent;
        readonly float _size;
        readonly uint _color;
        readonly ITextMeasurer _m;

        public AccentBox(Box bas, string accent, float size, uint color, ITextMeasurer m)
        {
            _base = bas; _accent = accent; _size = size; _color = color; _m = m;
            float aw = m.MeasureText(accent, size, StyleNormal);
            W = Math.Max(bas.W, aw);
            H = bas.H + size * 0.35f;
            D = bas.D;
        }

        public override void Draw(ICanvas c, float x, float baseline)
        {
            _base.Draw(c, x + (W - _base.W) / 2, baseline);
            float ax = x + (W - _m.MeasureText(_accent, _size, StyleNormal)) / 2;
            c.DrawText(_accent, ax, baseline - _base.H - _size * 0.05f, _size, StyleNormal, _color);
        }
    }

    private sealed class BoxedBox : Box
    {
        readonly Box _inner;
        readonly float _pad;
        readonly float _strokeWidth;
        readonly uint _color;

        public BoxedBox(Box inner, float size, uint color)
        {
            _inner = inner; _color = color;
            _pad = size * 0.15f;
            _strokeWidth = Math.Max(1.5f, size * 0.045f);
            W = inner.W + _pad * 2;
            H = inner.H + _pad;
            D = inner.D + _pad;
        }

        public override void Draw(ICanvas c, float x, float baseline)
        {
            c.DrawRectStroke(x, baseline - H, W, H + D, _strokeWidth, _color);
            _inner.Draw(c, x + _pad, baseline);
        }
    }

    private sealed class ScaledBox : Box
    {
        readonly Box _inner;
        readonly float _f;

        public ScaledBox(Box inner, float f)
        {
            _inner = inner; _f = f;
            W = inner.W * f;
            H = inner.H * f;
            D = inner.D * f;
        }

        public override void Draw(ICanvas c, float x, float baseline)
        {
            c.Save();
            c.Translate(x, baseline);
            c.Scale(_f, _f);
            _inner.Draw(c, 0, 0);
            c.Restore();
        }
    }

    private sealed class Parser
    {
        readonly string _s;
        int _pos;
        readonly float _size;
        readonly uint _color;
        readonly bool _display;
        readonly ITextMeasurer _m;
        string? _innerDelimRight;

        public Parser(string s, float size, uint color, bool display, ITextMeasurer m)
        {
            _s = s; _size = size; _color = color; _display = display; _m = m;
        }

        Box TextBox(string text, float scale, int style)
            => new TextBox(text, _size * scale, style, _color, _m);

        void SkipSpaces()
        {
            while (_pos < _s.Length && _s[_pos] == ' ') _pos++;
        }

        char Peek() => _pos < _s.Length ? _s[_pos] : '\0';

        public Box ParseExpr(char stop)
        {
            var outp = new HBox();
            while (_pos < _s.Length)
            {
                char c = _s[_pos];
                if (stop != '\0' && c == stop) { _pos++; return outp; }
                if (char.IsWhiteSpace(c)) { _pos++; continue; }
                Box atom = ParseAtom();
                // Scripts attached to this atom.
                Box? sup = null, sub = null;
                while (true)
                {
                    int save = _pos;
                    SkipSpaces();
                    if (_pos < _s.Length && (_s[_pos] == '^' || _s[_pos] == '_'))
                    {
                        bool isSup = _s[_pos] == '^';
                        _pos++;
                        SkipSpaces();
                        Box script = ParseScriptArg();
                        if (isSup) sup = script; else sub = script;
                    }
                    else
                    {
                        _pos = save;
                        break;
                    }
                }
                if (sup != null || sub != null)
                {
                    if (atom is BigOpPending bp)
                    {
                        atom = new BigOpBox(bp.Op, sup, sub, bp.AboveBelow);
                    }
                    else
                    {
                        atom = new ScriptBox(atom, sup, sub, _size);
                    }
                }
                outp.Add(atom);
            }
            if (stop != '\0') throw new ParseException("unclosed group");
            return outp;
        }

        Box ParseScriptArg()
        {
            SkipSpaces();
            if (Peek() == '{')
            {
                _pos++;
                return new ScaledBox(ParseExpr('}'), 0.7f);
            }
            return new ScaledBox(ParseAtom(), 0.7f);
        }

        Box ParseGroup()
        {
            if (Peek() != '{') throw new ParseException("expected {");
            _pos++;
            return ParseExpr('}');
        }

        string ParseRawGroup()
        {
            if (Peek() != '{') throw new ParseException("expected {");
            _pos++;
            int start = _pos;
            int depth = 1;
            while (_pos < _s.Length && depth > 0)
            {
                char c = _s[_pos];
                if (c == '{') depth++;
                else if (c == '}') depth--;
                _pos++;
            }
            if (depth != 0) throw new ParseException("unclosed group");
            return _s.Substring(start, _pos - 1 - start);
        }

        string ParseCommand()
        {
            // pos points at backslash.
            _pos++;
            if (_pos >= _s.Length) return "";
            char c = _s[_pos];
            if (!char.IsLetter(c))
            {
                _pos++;
                return c.ToString();
            }
            int start = _pos;
            while (_pos < _s.Length && char.IsLetter(_s[_pos])) _pos++;
            return _s.Substring(start, _pos - start);
        }

        Box ParseAtom()
        {
            char c = Peek();
            if (c == '{') return ParseGroup();
            if (c == '\\')
            {
                string cmd = ParseCommand();
                return CommandBox(cmd);
            }
            if (c == '(' || c == ')' || c == '[' || c == ']' || c == '|')
            {
                _pos++;
                return TextBox(c.ToString(), 1f, StyleNormal);
            }
            _pos++;
            if (char.IsLetter(c))
            {
                return TextBox(c.ToString(), 1f, StyleItalic);
            }
            return TextBox(c.ToString(), 1f, StyleNormal);
        }

        static string? Symbol(string cmd) => cmd switch
        {
            "alpha" => "\u03B1",
            "beta" => "\u03B2",
            "gamma" => "\u03B3",
            "delta" => "\u03B4",
            "epsilon" => "\u03B5",
            "zeta" => "\u03B6",
            "eta" => "\u03B7",
            "theta" => "\u03B8",
            "iota" => "\u03B9",
            "kappa" => "\u03BA",
            "lambda" => "\u03BB",
            "mu" => "\u03BC",
            "nu" => "\u03BD",
            "xi" => "\u03BE",
            "pi" => "\u03C0",
            "rho" => "\u03C1",
            "sigma" => "\u03C3",
            "tau" => "\u03C4",
            "phi" => "\u03C6",
            "chi" => "\u03C7",
            "psi" => "\u03C8",
            "omega" => "\u03C9",
            "Gamma" => "\u0393",
            "Delta" => "\u0394",
            "Theta" => "\u0398",
            "Lambda" => "\u039B",
            "Xi" => "\u039E",
            "Pi" => "\u03A0",
            "Sigma" => "\u03A3",
            "Phi" => "\u03A6",
            "Psi" => "\u03A8",
            "Omega" => "\u03A9",
            "pm" => "\u00B1",
            "mp" => "\u2213",
            "times" => "\u00D7",
            "div" => "\u00F7",
            "cdot" => "\u00B7",
            "cdots" => "\u22EF",
            "ldots" => "\u2026",
            "leq" or "le" => "\u2264",
            "geq" or "ge" => "\u2265",
            "neq" or "ne" => "\u2260",
            "approx" => "\u2248",
            "equiv" => "\u2261",
            "propto" => "\u221D",
            "to" or "rightarrow" => "\u2192",
            "leftarrow" => "\u2190",
            "uparrow" => "\u2191",
            "downarrow" => "\u2193",
            "leftrightarrow" => "\u2194",
            "Rightarrow" => "\u21D2",
            "Leftarrow" => "\u21D0",
            "Uparrow" => "\u21D1",
            "Downarrow" => "\u21D3",
            "Leftrightarrow" or "iff" => "\u21D4",
            "longleftarrow" => "\u27F5",
            "longrightarrow" => "\u27F6",
            "longleftrightarrow" => "\u27F7",
            "Longleftarrow" => "\u27F8",
            "Longrightarrow" or "implies" => "\u27F9",
            "mapsto" => "\u21A6",
            "hookrightarrow" => "\u21AA",
            "sim" => "\u223C",
            "simeq" => "\u2243",
            "cong" => "\u2245",
            "ll" => "\u226A",
            "gg" => "\u226B",
            "supset" => "\u2283",
            "subseteq" => "\u2286",
            "supseteq" => "\u2287",
            "vee" => "\u2228",
            "wedge" => "\u2227",
            "neg" => "\u00AC",
            "therefore" => "\u2234",
            "because" => "\u2235",
            "vdots" => "\u22EE",
            "ddots" => "\u22F1",
            "dots" => "\u2026",
            "oplus" => "\u2295",
            "otimes" => "\u2297",
            "circ" => "\u2218",
            "bullet" => "\u2219",
            "ast" => "\u2217",
            "mid" => "\u2223",
            "parallel" => "\u2225",
            "ni" => "\u220B",
            "aleph" => "\u2135",
            "infty" => "\u221E",
            "partial" => "\u2202",
            "nabla" => "\u2207",
            "in" => "\u2208",
            "notin" => "\u2209",
            "subset" => "\u2282",
            "cup" => "\u222A",
            "cap" => "\u2229",
            "emptyset" => "\u2205",
            "forall" => "\u2200",
            "exists" => "\u2203",
            "angle" => "\u2220",
            "perp" => "\u22A5",
            _ => null,
        };

        Box CommandBox(string cmd)
        {
            if (cmd == "begin")
            {
                string env = ParseRawGroup();
                return EnvBox(env);
            }
            if (cmd == "end") throw new ParseException("stray \\end");
            if (cmd == "frac" || cmd == "dfrac" || cmd == "tfrac")
            {
                Box num = ParseGroupOrAtom();
                Box den = ParseGroupOrAtom();
                bool big = cmd == "dfrac" || (cmd == "frac" && _display);
                float scale = big ? 1f : 0.7f;
                return new FracBox(new ScaledBox(num, scale), new ScaledBox(den, scale), true, _size, _color);
            }
            if (cmd == "binom")
            {
                Box num = ParseGroupOrAtom();
                Box den = ParseGroupOrAtom();
                float scale = _display ? 1f : 0.7f;
                Box f = new FracBox(new ScaledBox(num, scale), new ScaledBox(den, scale), false, _size, _color);
                return new DelimBox("(", ")", f, _size, _color, _m);
            }
            if (cmd == "sqrt")
            {
                SkipSpaces();
                Box? degree = null;
                if (Peek() == '[')
                {
                    _pos++;
                    int start = _pos;
                    while (_pos < _s.Length && _s[_pos] != ']') _pos++;
                    if (_pos >= _s.Length) throw new ParseException("unclosed [");
                    string deg = _s.Substring(start, _pos - start);
                    _pos++;
                    var dp = new Parser(deg, _size, _color, _display, _m);
                    degree = new ScaledBox(dp.ParseExpr('\0'), 0.7f);
                }
                Box inner = ParseGroupOrAtom();
                return new SqrtBox(inner, degree, _size, _color);
            }
            if (cmd == "sum" || cmd == "prod" || cmd == "int")
            {
                string glyph = cmd == "sum" ? "\u2211" : cmd == "prod" ? "\u220F" : "\u222B";
                float gscale = cmd == "int" ? 1.2f : (_display ? 1.5f : 1.2f);
                Box op = new ScaledBox(TextBox(glyph, 1f, StyleNormal), gscale);
                return new BigOpPending(op, cmd == "int" ? false : _display);
            }
            if (cmd == "lim")
            {
                Box op = TextBox("lim", 1f, StyleNormal);
                return new BigOpPending(op, _display);
            }
            if (cmd == "boxed")
            {
                Box inner = ParseGroupOrAtom();
                return new BoxedBox(inner, _size, _color);
            }
            if (cmd == "text" || cmd == "mathrm")
            {
                string raw = ParseRawGroup();
                return TextBox(raw, 1f, StyleNormal);
            }
            if (cmd == "mathbf")
            {
                Box inner = ParseGroupOrAtom();
                return Restyle(inner, StyleBold);
            }
            if (cmd == "mathit")
            {
                Box inner = ParseGroupOrAtom();
                return Restyle(inner, StyleItalic);
            }
            if (cmd == "mathbb")
            {
                string raw = ParseRawGroup();
                var sb = new System.Text.StringBuilder();
                foreach (char ch in raw)
                {
                    if (ch == 'R') sb.Append("\u211D");
                    else if (ch == 'Z') sb.Append("\u2124");
                    else if (ch == 'N') sb.Append("\u2115");
                    else if (ch == 'Q') sb.Append("\u211A");
                    else if (ch == 'C') sb.Append("\u2102");
                    else sb.Append(ch);
                }
                return TextBox(sb.ToString(), 1f, StyleNormal);
            }
            if (cmd == "left")
            {
                SkipSpaces();
                string? d = ParseDelim();
                Box inner = ParseUntilRight();
                return new DelimBox(d, _innerDelimRight, inner, _size, _color, _m);
            }
            if (cmd == "right") throw new ParseException("stray \\right");
            if (cmd == "hat") return AccentBox("\u0302");
            if (cmd == "bar") return AccentBox("\u0304");
            if (cmd == "vec") return AccentBox("\u2192");
            if (cmd == "tilde") return AccentBox("\u0303");
            if (cmd == "dot") return AccentBox("\u0307");
            if (cmd == "quad") return new SpaceBox(_size);
            if (cmd == "qquad") return new SpaceBox(_size * 2);
            if (cmd == ",") return new SpaceBox(_size * 0.17f);
            if (cmd == ";") return new SpaceBox(_size * 0.28f);
            if (cmd == ":") return new SpaceBox(_size * 0.22f);
            if (cmd == "!") return new SpaceBox(-_size * 0.17f);
            if (cmd == " ") return new SpaceBox(_size * 0.33f);
            if (cmd == "thinspace") return new SpaceBox(_size * 0.17f);
            if (cmd == "medspace") return new SpaceBox(_size * 0.22f);
            if (cmd == "thickspace") return new SpaceBox(_size * 0.28f);
            if (cmd == "enspace") return new SpaceBox(_size * 0.5f);
            if (cmd == "displaystyle" || cmd == "textstyle"
                    || cmd == "limits" || cmd == "nolimits")
            {
                return new SpaceBox(0);
            }
            if (cmd is "sin" or "cos" or "tan" or "csc" or "sec" or "cot"
                    or "log" or "ln" or "exp" or "min" or "max" or "det"
                    or "deg" or "gcd" or "sup" or "inf")
            {
                return TextBox(cmd, 1f, StyleNormal);
            }
            string? sym = Symbol(cmd);
            if (sym != null)
            {
                // Avoid tofu boxes for glyphs the font lacks.
                if (!_m.HasGlyph(sym, _size))
                {
                    sym = sym switch
                    {
                        "\u2264" => "<=",
                        "\u2265" => ">=",
                        "\u2260" => "!=",
                        "\u2248" => "~",
                        "\u2261" => "=",
                        "\u2192" => "->",
                        "\u2190" => "<-",
                        "\u2194" => "<->",
                        "\u21D2" => "=>",
                        "\u21D4" => "<=>",
                        "\u27F9" => "==>",
                        "\u27F8" => "<==",
                        "\u27F6" => "-->",
                        "\u27F5" => "<--",
                        "\u21A6" => "|->",
                        _ => sym,
                    };
                }
                return TextBox(sym, 1f, StyleNormal);
            }
            if (cmd is "%" or "$" or "&" or "#" or "_" or "{" or "}")
            {
                return TextBox(cmd, 1f, StyleNormal);
            }
            throw new ParseException("unsupported command: \\" + cmd);
        }

        // \begin{aligned|gathered|cases|array} ... \end{...}. Each cell is
        // parsed by a fresh sub-parser, so fractions, scripts and roots
        // work inside cells. A row break \\ ends the row; & splits
        // cells (TeXSplit is brace-aware, so \& and \{..\} stay intact).
        // Unknown environments throw, and the caller falls back to raw text.
        Box EnvBox(string env)
        {
            bool cases = env == "cases";
            bool centered = env == "gathered" || env == "gathered*";
            if (env != "aligned" && env != "aligned*" && !centered
                    && !cases && env != "array")
            {
                throw new ParseException("unsupported environment: " + env);
            }
            if (env == "array")
            {
                // Column spec, e.g. {cc|c}: parsed and discarded; layout as aligned.
                ParseRawGroup();
            }
            string endMark = "\\end{" + env + "}";
            int end = _s.IndexOf(endMark, _pos, StringComparison.Ordinal);
            if (end < 0) throw new ParseException("unclosed \\begin{" + env + "}");
            string body = _s.Substring(_pos, end - _pos);
            _pos = end + endMark.Length;
            var rows = new List<List<Box>>();
            foreach (string rowStr in TeXSplit.SplitRows(body))
            {
                var row = new List<Box>();
                foreach (string cell in TeXSplit.SplitCells(rowStr))
                {
                    var sub = new Parser(cell, _size, _color, _display, _m);
                    row.Add(sub.ParseExpr('\0'));
                }
                bool any = false;
                foreach (var b in row)
                {
                    if (b.W > 0 || b.H > 0 || b.D > 0) { any = true; break; }
                }
                if (any) rows.Add(row);
            }
            if (rows.Count == 0) return new SpaceBox(0);
            var ab = new AlignBox(rows, _size * 0.5f, _size * 0.25f, centered);
            if (cases)
            {
                return new DelimBox("{", null, ab, _size, _color, _m);
            }
            return ab;
        }

        Box AccentBox(string accent)
        {
            Box bas = ParseGroupOrAtom();
            return new AccentBox(bas, accent, _size, _color, _m);
        }

        Box ParseGroupOrAtom()
        {
            SkipSpaces();
            if (Peek() == '{') return ParseGroup();
            return ParseAtom();
        }

        string? ParseDelim()
        {
            if (_pos >= _s.Length) throw new ParseException("expected delimiter");
            char c = _s[_pos];
            if (c == '\\')
            {
                string dcmd = ParseCommand();
                if (dcmd == "{") return "{";
                if (dcmd == "}") return "}";
                if (dcmd == "|") return "|";
                if (dcmd == "lfloor") return "\u230A";
                if (dcmd == "rfloor") return "\u230B";
                if (dcmd == "lceil") return "\u2308";
                if (dcmd == "rceil") return "\u2309";
                if (dcmd == "langle") return "\u27E8";
                if (dcmd == "rangle") return "\u27E9";
                if (dcmd == "Vert") return "\u2016";
                throw new ParseException("bad delimiter");
            }
            if (c == '.') { _pos++; return null; }
            _pos++;
            return c.ToString();
        }

        Box ParseUntilRight()
        {
            // Parse until the matching \right (consumed here).
            var outp = new HBox();
            _innerDelimRight = null;
            while (_pos < _s.Length)
            {
                if (_s[_pos] == '\\')
                {
                    int save = _pos;
                    string dcmd = ParseCommand();
                    if (dcmd == "right")
                    {
                        SkipSpaces();
                        _innerDelimRight = ParseDelim();
                        return outp;
                    }
                    _pos = save;
                }
                Box atom = ParseAtom();
                Box? sup = null, sub = null;
                while (true)
                {
                    int save = _pos;
                    SkipSpaces();
                    if (_pos < _s.Length && (_s[_pos] == '^' || _s[_pos] == '_'))
                    {
                        bool isSup = _s[_pos] == '^';
                        _pos++;
                        SkipSpaces();
                        Box script = ParseScriptArg();
                        if (isSup) sup = script; else sub = script;
                    }
                    else
                    {
                        _pos = save;
                        break;
                    }
                }
                if (sup != null || sub != null)
                {
                    if (atom is BigOpPending bp)
                    {
                        atom = new BigOpBox(bp.Op, sup, sub, bp.AboveBelow);
                    }
                    else
                    {
                        atom = new ScriptBox(atom, sup, sub, _size);
                    }
                }
                outp.Add(atom);
            }
            throw new ParseException("missing \\right");
        }

        // BigOpPending: a big operator; ParseExpr rewrites following
        // scripts into a BigOpBox (limits above/below in display mode).
        private sealed class BigOpPending : Box
        {
            public readonly Box Op;
            public readonly bool AboveBelow;

            public BigOpPending(Box op, bool aboveBelow)
            {
                Op = op;
                AboveBelow = aboveBelow;
                W = op.W;
                H = op.H;
                D = op.D;
            }

            public override void Draw(ICanvas c, float x, float baseline)
                => Op.Draw(c, x, baseline);
        }

        Box Restyle(Box b, int style)
        {
            // Restyling a whole subtree is complex; only TextBox leaves
            // change. Walk HBox children recursively.
            if (b is TextBox t)
            {
                return new TextBox(t.Text, t.Size, style, t.Color, _m);
            }
            if (b is HBox h)
            {
                var outp = new HBox();
                foreach (var k in h.Kids) outp.Add(Restyle(k, style));
                return outp;
            }
            return b;
        }
    }
}
