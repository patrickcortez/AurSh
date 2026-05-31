using System.Globalization;
using System.Text;
using AurShell.Utils;

namespace AurShell.BlackBoxView;

/// <summary>
/// Visual layout density. Selected automatically from the current terminal
/// width by <see cref="BlackBoxRenderer.ResolveTier"/>.
///
/// Full     (>= 60 cols)  : header + side borders + footer (default desktop look).
/// Compact  (30..59 cols)  : header + footer only; body has no left/right '│'
///                           borders so narrow phone screens get back two cols
///                           of horizontal space per line.
/// Bar      (&lt; 30 cols)   : single-line header ("▸ ls — running") and
///                           single-line footer ("└ exit:0 12ms"). Body is
///                           rendered raw, no decoration. Survives ≈20-col
///                           terminals (split-screen Android, tiny WSL panes).
/// </summary>
public enum LayoutTier
{
    Full,
    Compact,
    Bar,
}

public sealed class BlackBoxRenderer
{
    private readonly BlackBoxConfig _config;

    public BlackBoxRenderer(BlackBoxConfig config)
    {
        _config = config;
    }

    /// <summary>Current adaptive layout tier (derived from terminal width).</summary>
    public LayoutTier Tier => ResolveTier();

    /// <summary>
    /// Render only the box header (top border), for passthrough mode where the
    /// child writes directly to the terminal between the header and footer.
    /// </summary>
    public void RenderHeaderOnly(BlackBoxSession session, System.IO.TextWriter writer)
    {
        var glyphs = BoxChars.From(_config.Border);
        var tier = ResolveTier();
        int outerWidth = ResolveOuterWidth();
        int innerWidth = System.Math.Max(4, outerWidth - (tier == LayoutTier.Bar ? 0 : 2));

        var sb = new StringBuilder();
        RenderTop(sb, glyphs, session, outerWidth, innerWidth, tier);
        writer.Write(sb.ToString());
        writer.Flush();
    }

    /// <summary>
    /// Render only the box footer (bottom border), for passthrough mode after
    /// the child has finished writing directly to the terminal.
    /// </summary>
    public void RenderFooterOnly(BlackBoxSession session, System.IO.TextWriter writer)
    {
        var glyphs = BoxChars.From(_config.Border);
        var tier = ResolveTier();
        int outerWidth = ResolveOuterWidth();
        int innerWidth = System.Math.Max(4, outerWidth - (tier == LayoutTier.Bar ? 0 : 2));

        var sb = new StringBuilder();
        RenderBottom(sb, glyphs, session, outerWidth, innerWidth, 0, 0, tier);
        writer.Write(sb.ToString());
        writer.Flush();
    }

    public List<string> RenderBodyRows(BufferLine line)
    {
        var glyphs = BoxChars.From(_config.Border);
        var tier = ResolveTier();
        int outerWidth = ResolveOuterWidth();

        if (tier == LayoutTier.Bar)
        {
            // No decoration. Just wrap to width.
            return FormatBodyLines(line, System.Math.Max(1, outerWidth));
        }

        if (tier == LayoutTier.Compact)
        {
            int contentWidth = System.Math.Max(1, outerWidth);
            List<string> compactLines = FormatBodyLines(line, contentWidth);
            var results = new List<string>(compactLines.Count);
            
            foreach (string c in compactLines)
            {
                var sbCompact = new StringBuilder();
                if (!string.IsNullOrEmpty(_config.BackgroundColor))
                    sbCompact.Append(_config.BackgroundColor);
                    
                sbCompact.Append(c);
                
                if (!string.IsNullOrEmpty(_config.BackgroundColor))
                    sbCompact.Append(_config.BackgroundColor);
                    
                int vis = Ansi.VisibleLength(c);
                int padC = System.Math.Max(0, contentWidth - vis);
                if (padC > 0)
                    sbCompact.Append(' ', padC);
                    
                if (!string.IsNullOrEmpty(_config.BackgroundColor))
                    sbCompact.Append(Ansi.Reset);
                    
                results.Add(sbCompact.ToString());
            }
            return results;
        }

        int innerWidth = System.Math.Max(4, outerWidth - 2);
        List<string> lines = FormatBodyLines(line, innerWidth - 2);
        var res = new List<string>(lines.Count);
        
        foreach (string content in lines)
        {
            var sb = new StringBuilder();
            sb.Append(_config.BorderColor);
            sb.Append(glyphs.Vertical);
            sb.Append(Ansi.Reset);

            if (!string.IsNullOrEmpty(_config.BackgroundColor))
                sb.Append(_config.BackgroundColor);

            sb.Append(' ');

            sb.Append(content);

            if (!string.IsNullOrEmpty(_config.BackgroundColor))
                sb.Append(_config.BackgroundColor);

            int visible = Ansi.VisibleLength(content);
            int pad = System.Math.Max(0, innerWidth - 2 - visible);
            if (pad > 0)
                sb.Append(' ', pad);

            sb.Append(' ');

            if (!string.IsNullOrEmpty(_config.BackgroundColor))
                sb.Append(Ansi.Reset);
            sb.Append(_config.BorderColor);
            sb.Append(glyphs.Vertical);
            sb.Append(Ansi.Reset);
            
            res.Add(sb.ToString());
        }
        
        return res;
    }

    /// <summary>
    /// Return the footer (bottom border) as a string (without trailing \n).
    /// </summary>
    public string RenderFooterToString(BlackBoxSession session)
    {
        var glyphs = BoxChars.From(_config.Border);
        var tier = ResolveTier();
        int outerWidth = ResolveOuterWidth();
        int innerWidth = System.Math.Max(4, outerWidth - (tier == LayoutTier.Bar ? 0 : 2));

        var sb = new StringBuilder();
        RenderBottom(sb, glyphs, session, outerWidth, innerWidth, 0, 0, tier);
        // RenderBottom appends a trailing \n; strip it so callers can place
        // their own line terminator.
        string s = sb.ToString();
        if (s.EndsWith('\n')) s = s.Substring(0, s.Length - 1);
        return s;
    }

    public void Render(BlackBoxSession session, System.IO.TextWriter writer)
    {
        var glyphs = BoxChars.From(_config.Border);
        var tier = ResolveTier();

        int outerWidth = ResolveOuterWidth();
        int innerWidth = System.Math.Max(4, outerWidth - (tier == LayoutTier.Bar ? 0 : 2));

        // Y-axis autoscale: render every line the buffer has. No height cap, no
        // scrolling. Older rows naturally scroll into the terminal's scrollback
        // if the box exceeds the visible viewport.
        int bodyRows = System.Math.Max(_config.MinHeight, session.Buffer.Count);
        int top = 0;

        var sb = new StringBuilder();
        RenderTop(sb, glyphs, session, outerWidth, innerWidth, tier);
        RenderBody(sb, glyphs, session.Buffer, top, bodyRows, innerWidth, tier);
        RenderBottom(sb, glyphs, session, outerWidth, innerWidth, top, bodyRows, tier);

        writer.Write(sb.ToString());
        writer.Flush();
    }

    private int ResolveOuterWidth()
    {
        int w = TerminalSize.Width;
        // Min usable width: 20. Below that, callers still render but content
        // gets aggressively truncated. We *don't* silently fall back to 80
        // here because that hides real failures from the user.
        if (w < 20) w = 20;
        return w;
    }

    private LayoutTier ResolveTier()
    {
        int w = TerminalSize.Width;
        if (w < 30) return LayoutTier.Bar;
        if (w < 60) return LayoutTier.Compact;
        return LayoutTier.Full;
    }

    private int ComputeScrollTop(BlackBoxBuffer buffer, int visibleRows)
    {
        if (buffer.Count <= visibleRows) return 0;

        if (buffer.TopLineIdx > 0 && buffer.TopLineIdx <= buffer.Count - visibleRows)
            return buffer.TopLineIdx;

        return buffer.Count - visibleRows;
    }

    private void RenderTop(StringBuilder sb, BoxGlyphs g, BlackBoxSession session, int outerWidth, int innerWidth, LayoutTier tier)
    {
        string displayTitle = _config.Title ?? "BlackBox";

        if (tier == LayoutTier.Bar)
        {
            // Single-line header, no corners: "▸ BlackBox :: ls".
            int budget = System.Math.Max(1, outerWidth);
            string left = $"{_config.BorderColor}▸ {_config.TitleColor}{displayTitle}{Ansi.Reset}{_config.BorderColor} :: {_config.TitleColor}";
            string titleText = TruncateForHeader(session.CommandTitle, System.Math.Max(1, budget - 12));
            string line = left + titleText + Ansi.Reset;
            sb.Append(line);
            sb.Append('\n');
            return;
        }

        string title = _config.ShowTitle 
            ? $" {_config.TitleColor}{displayTitle}{Ansi.Reset}{_config.BorderColor} :: {_config.TitleColor}{TruncateForHeader(session.CommandTitle, innerWidth / 2)}{Ansi.Reset}{_config.BorderColor} "
            : "";

        string right = BuildHeaderRightLabel(session);
        string rightPadded = string.IsNullOrEmpty(right) ? "" : $" {_config.MetaColor}{right}{Ansi.Reset}{_config.BorderColor} ";

        int titleVisible = Ansi.VisibleLength(title);
        int rightVisible = Ansi.VisibleLength(rightPadded);

        int dashLeft = 1;
        int dashRight = System.Math.Max(1, innerWidth - titleVisible - rightVisible - dashLeft);
        if (dashRight < 1)
        {
            rightPadded = "";
            rightVisible = 0;
            dashRight = System.Math.Max(0, innerWidth - titleVisible - dashLeft);
        }

        sb.Append(_config.BorderColor);
        sb.Append(g.TopLeft);
        sb.Append(Repeat(g.Horizontal, dashLeft));
        sb.Append(title);
        sb.Append(Repeat(g.Horizontal, dashRight));
        sb.Append(rightPadded);
        sb.Append(g.TopRight);
        sb.Append(Ansi.Reset);
        sb.Append('\n');
    }

    private string BuildHeaderRightLabel(BlackBoxSession session)
    {
        string cwd = string.IsNullOrEmpty(session.WorkingDirectory)
            ? ""
            : Platform.ShortenPath(session.WorkingDirectory);

        string time = session.StartedAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

        if (string.IsNullOrEmpty(cwd))
            return time;

        return $"{cwd}  {time}";
    }

    private void RenderBody(StringBuilder sb, BoxGlyphs g, BlackBoxBuffer buffer, int top, int rows, int innerWidth, LayoutTier tier)
    {
        int rendered = 0;
        foreach (var line in buffer.Window(top, rows))
        {
            foreach (string renderedRow in RenderBodyRows(line))
            {
                sb.Append(renderedRow);
                sb.Append('\n');
                rendered++;
                if (rendered >= rows) break;
            }
            if (rendered >= rows) break;
        }

        while (rendered < rows)
        {
            if (tier == LayoutTier.Full)
            {
                sb.Append(_config.BorderColor);
                sb.Append(g.Vertical);
                sb.Append(Ansi.Reset);

                if (!string.IsNullOrEmpty(_config.BackgroundColor))
                    sb.Append(_config.BackgroundColor);

                sb.Append(' ', innerWidth);

                if (!string.IsNullOrEmpty(_config.BackgroundColor))
                    sb.Append(Ansi.Reset);

                sb.Append(_config.BorderColor);
                sb.Append(g.Vertical);
                sb.Append(Ansi.Reset);
            }
            else if (tier == LayoutTier.Compact)
            {
                if (!string.IsNullOrEmpty(_config.BackgroundColor))
                    sb.Append(_config.BackgroundColor);
                    
                sb.Append(' ', System.Math.Max(0, innerWidth));
                
                if (!string.IsNullOrEmpty(_config.BackgroundColor))
                    sb.Append(Ansi.Reset);
            }
            sb.Append('\n');
            rendered++;
        }
    }

    private List<string> FormatBodyLines(BufferLine line, int maxVisible)
    {
        string prefix = "";
        string color = "";

        switch (line.Kind)
        {
            case LineKind.Stderr:
                color = _config.StderrColor;
                if (line.StageIndex is int s2)
                    prefix = $"[{s2 + 1}!] ";
                else
                    prefix = "! ";
                break;
            case LineKind.StdinEcho:
                color = _config.MetaColor;
                prefix = "> ";
                break;
            case LineKind.Meta:
                color = _config.MetaColor;
                prefix = "";
                break;
            case LineKind.Stdout:
            default:
                color = "";
                if (line.StageIndex is int s1)
                    prefix = $"[{s1 + 1}] ";
                break;
        }

        string body = line.Text ?? "";

        int lastCr = body.LastIndexOf('\r');
        if (lastCr >= 0)
            body = body.Substring(lastCr + 1);

        string combined = prefix + body;

        int effectiveStartCol = (ResolveTier() == LayoutTier.Full) ? 2 : 0;
        combined = Ansi.ExpandTabs(combined, tabStop: 8, startCol: effectiveStartCol);

        var chunks = Ansi.SplitVisible(combined, maxVisible);
        if (chunks.Count == 0) chunks.Add("");
        
        var results = new List<string>(chunks.Count);
        foreach (string chunk in chunks)
        {
            if (string.IsNullOrEmpty(color))
            {
                if (chunk.Contains('\x1b'))
                    results.Add(chunk + Ansi.Reset);
                else
                    results.Add(chunk);
            }
            else
            {
                results.Add($"{color}{chunk}{Ansi.Reset}");
            }
        }
        
        return results;
    }

    private void RenderBottom(StringBuilder sb, BoxGlyphs g, BlackBoxSession session, int outerWidth, int innerWidth, int top, int bodyRows, LayoutTier tier)
    {
        string exitLabel = BuildExitLabel(session);
        string elapsed = BuildElapsedLabel(session);

        if (tier == LayoutTier.Bar)
        {
            // Single-line footer: "└ exit:0 12ms" (or "└ running 12ms").
            string leftBar = $"{_config.BorderColor}└ ";
            string midBar = string.IsNullOrEmpty(exitLabel) ? "" : exitLabel + " ";
            string rightBar = string.IsNullOrEmpty(elapsed) ? "" : $"{_config.MetaColor}{elapsed}{Ansi.Reset}";
            sb.Append(leftBar);
            sb.Append(midBar);
            sb.Append(rightBar);
            sb.Append('\n');
            return;
        }


        // Scroll indicator removed: body always renders entire buffer (y-axis autoscale).
        string left = "";
        string mid = string.IsNullOrEmpty(exitLabel) ? "" : $" {exitLabel}{_config.BorderColor} ";
        string right = string.IsNullOrEmpty(elapsed) ? "" : $" {_config.MetaColor}{elapsed}{Ansi.Reset}{_config.BorderColor} ";

        int leftVis = Ansi.VisibleLength(left);
        int midVis = Ansi.VisibleLength(mid);
        int rightVis = Ansi.VisibleLength(right);

        int remaining = innerWidth - leftVis - midVis - rightVis;
        if (remaining < 2)
        {
            remaining = innerWidth - midVis - rightVis;
            if (remaining < 2)
            {
                mid = "";
                midVis = 0;
                remaining = System.Math.Max(0, innerWidth - rightVis);
            }
        }

        int dashLeft = System.Math.Max(0, remaining / 2);
        int dashRight = System.Math.Max(0, remaining - dashLeft);

        sb.Append(_config.BorderColor);
        sb.Append(g.BottomLeft);
        sb.Append(left);
        sb.Append(Repeat(g.Horizontal, dashLeft));
        sb.Append(mid);
        sb.Append(Repeat(g.Horizontal, dashRight));
        sb.Append(right);
        sb.Append(g.BottomRight);
        sb.Append(Ansi.Reset);
        sb.Append('\n');
    }

    private string BuildScrollIndicator(BlackBoxBuffer buffer, int top, int bodyRows)
    {
        if (buffer.Count <= bodyRows) return "";
        int firstVisible = top + 1;
        int lastVisible = System.Math.Min(buffer.Count, top + bodyRows);
        return $"\u2195 {firstVisible}-{lastVisible}/{buffer.Count}";
    }

    private string BuildExitLabel(BlackBoxSession session)
    {
        switch (session.State)
        {
            case BlackBoxState.Running:
                return $"{_config.MetaColor}running{Ansi.Reset}";
            case BlackBoxState.Aborted:
                return $"{_config.ExitErrColor}aborted{Ansi.Reset}";
            case BlackBoxState.Finished:
                int code = session.ExitCode ?? 0;
                string color = code == 0 ? _config.ExitOkColor : _config.ExitErrColor;
                return $"{color}exit: {code}{Ansi.Reset}";
            default:
                return "";
        }
    }

    private string BuildElapsedLabel(BlackBoxSession session)
    {
        var span = session.Elapsed;
        if (span.TotalSeconds < 1.0)
            return $"{span.TotalMilliseconds:F0}ms";
        if (span.TotalSeconds < 60.0)
            return $"{span.TotalSeconds:F1}s";
        int mins = (int)span.TotalMinutes;
        double secs = span.TotalSeconds - mins * 60;
        return $"{mins}m{secs:F0}s";
    }

    private static string Repeat(string s, int count)
    {
        if (count <= 0) return "";
        if (s.Length == 1) return new string(s[0], count);

        var sb = new StringBuilder(s.Length * count);
        for (int i = 0; i < count; i++) sb.Append(s);
        return sb.ToString();
    }

    private static string TruncateForHeader(string text, int max)
    {
        if (max <= 0) return "";
        text = text.Replace('\n', ' ').Replace('\r', ' ');
        if (text.Length <= max) return text;
        if (max <= 1) return text.Substring(0, max);
        return text.Substring(0, max - 1) + "\u2026";
    }

    /// <summary>
    /// Legacy entrypoint kept for callers that don't need colour-preserving
    /// truncation. New code should call <see cref="Ansi.TruncateVisible"/>
    /// directly.
    /// </summary>
    private static string Truncate(string text, int max)
    {
        if (max <= 0) return "";
        return Ansi.TruncateVisible(text, max);
    }
}
