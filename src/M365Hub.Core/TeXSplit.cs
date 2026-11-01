// Pure string splitting for TeX multi-line environments
// (aligned / gathered / cases / array). No WinUI types, so it is
// unit-testable on any host. Direct port of Android TeXSplit.java.
//
// Rules: rows split on \\ at brace depth 0 (an optional [..] row gap
// after the break is dropped); cells split on & at brace depth 0
// (\& does not split); a backslash always escapes the next character
// for splitting purposes, so \{ \} \& \\ inside braces stay intact.
namespace M365Hub.Core;

public static class TeXSplit
{
    /** Split an environment body into rows. Empty rows are dropped. */
    public static List<string> SplitRows(string body)
    {
        var rows = new List<string>();
        var cur = new System.Text.StringBuilder();
        int depth = 0;
        int i = 0;
        int n = body.Length;
        while (i < n)
        {
            char c = body[i];
            if (c == '\\' && i + 1 < n && body[i + 1] == '\\' && depth == 0)
            {
                string row = cur.ToString().Trim();
                if (row.Length > 0) rows.Add(row);
                cur.Clear();
                i += 2;
                // Skip spaces and an optional [..] row gap after the break.
                while (i < n && body[i] == ' ') i++;
                if (i < n && body[i] == '[')
                {
                    int close = body.IndexOf(']', i + 1);
                    if (close >= 0) i = close + 1;
                }
                continue;
            }
            if (c == '\\' && i + 1 < n)
            {
                cur.Append(c);
                cur.Append(body[i + 1]);
                i += 2;
                continue;
            }
            if (c == '{') depth++;
            else if (c == '}') depth = Math.Max(0, depth - 1);
            cur.Append(c);
            i++;
        }
        string last = cur.ToString().Trim();
        if (last.Length > 0) rows.Add(last);
        return rows;
    }

    /** Split one row into cells on & (brace-aware, \& does not split). */
    public static List<string> SplitCells(string row)
    {
        var cells = new List<string>();
        var cur = new System.Text.StringBuilder();
        int depth = 0;
        int i = 0;
        int n = row.Length;
        while (i < n)
        {
            char c = row[i];
            if (c == '\\' && i + 1 < n)
            {
                cur.Append(c);
                cur.Append(row[i + 1]);
                i += 2;
                continue;
            }
            if (c == '{') depth++;
            else if (c == '}') depth = Math.Max(0, depth - 1);
            if (c == '&' && depth == 0)
            {
                cells.Add(cur.ToString().Trim());
                cur.Clear();
                i++;
                continue;
            }
            cur.Append(c);
            i++;
        }
        cells.Add(cur.ToString().Trim());
        return cells;
    }
}
