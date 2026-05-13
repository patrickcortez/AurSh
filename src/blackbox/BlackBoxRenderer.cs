using System.Globalization;
using System.Text;
using AurShell.Utils;

namespace AurShell.BlackBoxView;

public sealed class BlackBoxRenderer
{
    private readonly BlackBoxConfig _config;

    public BlackBoxRenderer(BlackBoxConfig config)
    {
        _config = config;
    }

    public void Render(BlackBoxSession session, System.IO.TextWriter writer)
    {
        var glyphs = BoxChars.From(_config.Border);

        int outerWidth = ResolveOuterWidth();
        int innerWidth = System.Math.Max(4, outerWidth - 2);

        // Y-axis autoscale: render every line the buffer has. No height cap, no
        // scrolling. Older rows naturally scroll into the terminal's scrollback
        // if the box exceeds the visible viewport.
        int bodyRows = System.Math.Max(_config.MinHeight, session.Buffer.Count);
        int top = 0;

        var sb = new StringBuilder();
        RenderTop(sb, glyphs, session, outerWidth, innerWidth);
        RenderBody(sb, glyphs, session.Buffer, top, bodyRows, innerWidth);
        RenderBottom(sb, glyphs, session, outerWidth, innerWidth, top, bodyRows);

        writer.Write(sb.ToString());
        writer.Flush();
    }

    private int ResolveOuterWidth()
    {
        int w = Platform.TerminalWidth;
        if (w < 20) w = 80;
        return w;
    }

    private int ComputeScrollTop(BlackBoxBuffer buffer, int visibleRows)
    {
        if (buffer.Count <= visibleRows) return 0;

        if (buffer.TopLineIdx > 0 && buffer.TopLineIdx <= buffer.Count - visibleRows)
            return buffer.TopLineIdx;

        return buffer.Count - visibleRows;
    }

    private void RenderTop(StringBuilder sb, BoxGlyphs g, BlackBoxSession session, int outerWidth, int innerWidth)
    {
        string title = $" {_config.TitleColor}BlackBox{Ansi.Reset}{_config.BorderColor} :: {_config.TitleColor}{TruncateForHeader(session.CommandTitle, innerWidth / 2)}{Ansi.Reset}{_config.BorderColor} ";

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

    private void RenderBody(StringBuilder sb, BoxGlyphs g, BlackBoxBuffer buffer, int top, int rows, int innerWidth)
    {
        int rendered = 0;
        foreach (var line in buffer.Window(top, rows))
        {
            sb.Append(_config.BorderColor);
            sb.Append(g.Vertical);
            sb.Append(Ansi.Reset);
            sb.Append(' ');

            string content = FormatBodyLine(line, innerWidth - 2);
            sb.Append(content);

            int visible = Ansi.VisibleLength(content);
            int pad = System.Math.Max(0, innerWidth - 2 - visible);
            if (pad > 0)
                sb.Append(' ', pad);

            sb.Append(' ');
            sb.Append(_config.BorderColor);
            sb.Append(g.Vertical);
            sb.Append(Ansi.Reset);
            sb.Append('\n');

            rendered++;
        }

        while (rendered < rows)
        {
            sb.Append(_config.BorderColor);
            sb.Append(g.Vertical);
            sb.Append(Ansi.Reset);
            sb.Append(' ', innerWidth);
            sb.Append(_config.BorderColor);
            sb.Append(g.Vertical);
            sb.Append(Ansi.Reset);
            sb.Append('\n');
            rendered++;
        }
    }

    private string FormatBodyLine(BufferLine line, int maxVisible)
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
        string combined = prefix + body;

        if (Ansi.VisibleLength(combined) > maxVisible)
            combined = Truncate(combined, maxVisible);

        if (string.IsNullOrEmpty(color))
            return combined;

        return $"{color}{combined}{Ansi.Reset}";
    }

    private void RenderBottom(StringBuilder sb, BoxGlyphs g, BlackBoxSession session, int outerWidth, int innerWidth, int top, int bodyRows)
    {
        string exitLabel = BuildExitLabel(session);
        string elapsed = BuildElapsedLabel(session);

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

    private static string Truncate(string text, int max)
    {
        if (max <= 0) return "";
        string stripped = Ansi.Strip(text);
        if (stripped.Length <= max) return text;
        if (max <= 1) return stripped.Substring(0, max);
        return stripped.Substring(0, max - 1) + "\u2026";
    }
}
